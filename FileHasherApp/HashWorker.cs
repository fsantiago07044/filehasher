using System.Security.Cryptography;
using System.Text;

namespace FileHasher;

/// <summary>
/// Background worker that enumerates and hashes files according to <see cref="HashOptions"/>.
/// All public methods are thread-safe to call from the UI thread; events are raised on the
/// thread-pool thread — callers must marshal to the UI thread as needed.
/// </summary>
internal sealed class HashWorker
{
    private readonly HashOptions _opts;
    private readonly Logger      _logger;

    public event Action<string>?     WarningRaised;
    public event Action<HashResult>? FileHashed;

    /// <summary>
    /// When <see cref="HashOptions.WriteSidecarHashes"/> is true, called once per file that
    /// already has a sidecar.  The argument is the full sidecar path.  The callee must
    /// marshal to the UI thread if needed.  Null means always overwrite (legacy behaviour).
    /// </summary>
    public Func<string, SidecarConflictAction>? SidecarConflictResolver { get; set; }

    public int SidecarSkippedCount     { get; private set; }
    public int SidecarOverwrittenCount { get; private set; }

    private SidecarConflictAction? _batchSidecarAction;

    public HashWorker(HashOptions opts, Logger logger)
    {
        _opts   = opts;
        _logger = logger;
    }

    // ── Phase 1: enumeration (returns before any hashing begins) ─────────────

    public Task<List<string>> EnumerateAsync(CancellationToken ct)
        => Task.Run(() => CollectFiles(ct), ct);

    // ── Phase 2: hashing ─────────────────────────────────────────────────────

    public async Task HashAllAsync(List<string> files, IProgress<int> progress, CancellationToken ct)
    {
        int done = 0;
        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();

            if (_opts.WriteSidecarHashes && SidecarConflictResolver is not null)
            {
                // A previous "Skip All" decision skips all remaining files unconditionally.
                if (_batchSidecarAction == SidecarConflictAction.SkipAll)
                {
                    SidecarSkippedCount++;
                    progress.Report(++done);
                    continue;
                }

                var sidecarPath = path + _opts.SidecarExtension;
                if (File.Exists(sidecarPath))
                {
                    if (ShouldSkipDueToSidecar(path, sidecarPath))
                    {
                        progress.Report(++done);
                        continue;
                    }
                    SidecarOverwrittenCount++;
                }
            }

            var result = await HashFileAsync(path, ct);
            _logger.LogResult(result, _opts.Algorithm);
            FileHashed?.Invoke(result);
            progress.Report(++done);
        }
    }

    /// <summary>Returns true when the file should be skipped entirely.</summary>
    private bool ShouldSkipDueToSidecar(string filePath, string sidecarPath)
    {
        if (_batchSidecarAction == SidecarConflictAction.OverwriteAll)
            return false;

        var action = SidecarConflictResolver!(sidecarPath);

        switch (action)
        {
            case SidecarConflictAction.SkipAll:
                _batchSidecarAction = SidecarConflictAction.SkipAll;
                goto case SidecarConflictAction.Skip;

            case SidecarConflictAction.Skip:
                SidecarSkippedCount++;
                WarningRaised?.Invoke($"Skipped — existing sidecar: {filePath}");
                return true;

            case SidecarConflictAction.OverwriteAll:
                _batchSidecarAction = SidecarConflictAction.OverwriteAll;
                return false;

            default: // Overwrite (once)
                return false;
        }
    }

    // ── File enumeration ─────────────────────────────────────────────────────

    private List<string> CollectFiles(CancellationToken ct)
    {
        if (_opts.IsFile)
            return new List<string> { _opts.TargetPath };

        var extensions = _opts.AllFileTypes
            ? null
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi" };

        var results = new List<string>();
        var stack   = new Stack<string>();
        stack.Push(_opts.TargetPath);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (extensions is null || extensions.Contains(Path.GetExtension(f)))
                        results.Add(f);
                }
            }
            catch (Exception ex)
            {
                WarningRaised?.Invoke($"Cannot list files in: {dir}  ({ex.Message})");
            }

            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir))
                    stack.Push(d);
            }
            catch (Exception ex)
            {
                WarningRaised?.Invoke($"Cannot list subdirectories in: {dir}  ({ex.Message})");
            }
        }

        return results;
    }

    // ── Single-file hashing ──────────────────────────────────────────────────

    private async Task<HashResult> HashFileAsync(string filePath, CancellationToken ct)
    {
        try
        {
            byte[] hashBytes;

            using (var algo   = CreateAlgorithm())
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite, bufferSize: 65_536,
                                               useAsync: true))
            {
                hashBytes = await algo.ComputeHashAsync(stream, ct);
            }

            var hash = Convert.ToHexString(hashBytes);  // uppercase

            var fi = new FileInfo(filePath);

            if (_opts.WriteSidecarHashes)
                await WriteSidecarAsync(filePath, hash, ct);

            return new HashResult(
                FilePath:     filePath,
                Hash:         hash,
                Length:       _opts.IncludeMetadata ? fi.Length          : null,
                LastWriteUtc: _opts.IncludeMetadata ? fi.LastWriteTimeUtc : null,
                Success:      true,
                ErrorMessage: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new HashResult(filePath, string.Empty, null, null, false, ex.Message);
        }
    }

    private async Task WriteSidecarAsync(string filePath, string hash, CancellationToken ct)
    {
        try
        {
            var sidecarPath = filePath + _opts.SidecarExtension;
            var content     = _opts.SidecarFormat == "hashonly"
                ? hash
                : $"{hash} *{Path.GetFileName(filePath)}";

            await File.WriteAllTextAsync(sidecarPath,
                                         content + Environment.NewLine,
                                         new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                                         ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WarningRaised?.Invoke($"Sidecar write failed for: {filePath}  ({ex.Message})");
        }
    }

    // ── Algorithm factory ────────────────────────────────────────────────────

    private HashAlgorithm CreateAlgorithm() => _opts.Algorithm switch
    {
        "MD5"    => MD5.Create(),
        "SHA1"   => SHA1.Create(),
        "SHA512" => SHA512.Create(),
        _        => SHA256.Create()     // default / "SHA256"
    };
}

internal enum SidecarConflictAction
{
    Overwrite,
    OverwriteAll,
    Skip,
    SkipAll
}
