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
            var result = await HashFileAsync(path, ct, container: null);
            _logger.LogResult(result, _opts.Algorithm);
            FileHashed?.Invoke(result);
            progress.Report(++done);

            // EXPERIMENTAL: when DescendIntoMsi is on and the file is an MSI,
            // extract its contents to a sandboxed temp dir and hash each inner
            // file individually. The MSI itself has already been hashed above
            // as a normal file; this only adds inner-file rows.
            if (_opts.DescendIntoMsi &&
                result.Success &&
                path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                done = await HashMsiInnerFilesAsync(path, progress, ct, done);
            }
        }
    }

    /// <summary>
    /// Extract <paramref name="msiPath"/> to a sandboxed temp dir, hash each
    /// inner file, emit one <see cref="HashResult"/> per file with the
    /// FilePath rewritten relative to the extract dir, and return the
    /// progress counter advanced by the number of inner files processed.
    /// </summary>
    private async Task<int> HashMsiInnerFilesAsync(
        string msiPath, IProgress<int> progress, CancellationToken ct, int done)
    {
        // Each MSI gets its own extractor instance so the temp dir lifetime
        // is scoped exactly to this MSI's inner-file pass. `using` guarantees
        // cleanup even if hashing throws.
        MsiExtractor extractor;
        try
        {
            extractor = new MsiExtractor();
        }
        catch (Exception ex)
        {
            WarningRaised?.Invoke($"MSI extractor init failed for {msiPath}: {ex.Message}");
            return done;
        }

        using (extractor)
        {
            IReadOnlyList<string> innerFiles;
            try
            {
                innerFiles = await extractor.ExtractAsync(msiPath, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                WarningRaised?.Invoke($"MSI extraction failed for {msiPath}: {ex.Message}");
                return done;
            }

            foreach (var inner in innerFiles)
            {
                ct.ThrowIfCancellationRequested();
                var innerResult = await HashFileAsync(inner, ct, container: msiPath);

                // Rewrite FilePath in the emitted result to be relative to the
                // extract dir; users get the MSI-internal install layout
                // ("Program Files/Foo/bar.exe") rather than a useless temp-dir
                // absolute path. The original absolute path never leaves this
                // method and is unreachable to consumers once the extractor is
                // disposed and its temp dir is deleted.
                var relative = Path.GetRelativePath(extractor.ExtractDirectory, inner);
                innerResult = innerResult with { FilePath = relative };

                _logger.LogResult(innerResult, _opts.Algorithm);
                FileHashed?.Invoke(innerResult);
                progress.Report(++done);
            }
        }
        // Extractor's temp dir is now deleted.

        return done;
    }

    // ── File enumeration ─────────────────────────────────────────────────────

    private List<string> CollectFiles(CancellationToken ct)
    {
        if (_opts.IsFile)
            return new List<string> { _opts.TargetPath };

        var extensions = _opts.AllFileTypes
            ? null
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi" };

        // When writing sidecars, never treat sidecar files themselves as targets —
        // that would create .sha256.sha256 chains on repeated runs.
        var sidecarExt = _opts.WriteSidecarHashes ? _opts.SidecarExtension : null;

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
                    if (sidecarExt != null &&
                        f.EndsWith(sidecarExt, StringComparison.OrdinalIgnoreCase))
                        continue;

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

    private async Task<HashResult> HashFileAsync(string filePath, CancellationToken ct, string? container)
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

            // Sidecar writes are intentionally skipped for files extracted
            // from an MSI: those files live in a temp directory that will be
            // deleted as soon as this MSI's inner-file pass finishes, so a
            // sidecar there would be orphaned within seconds. The CSV export
            // is the durable record for inner-file hashes.
            if (_opts.WriteSidecarHashes && container is null)
                await WriteSidecarAsync(filePath, hash, ct);

            return new HashResult(
                FilePath:     filePath,
                Hash:         hash,
                Length:       _opts.IncludeMetadata ? fi.Length          : null,
                LastWriteUtc: _opts.IncludeMetadata ? fi.LastWriteTimeUtc : null,
                Success:      true,
                ErrorMessage: null,
                Container:    container);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new HashResult(filePath, string.Empty, null, null, false, ex.Message, container);
        }
    }

    private async Task WriteSidecarAsync(string filePath, string hash, CancellationToken ct)
    {
        // Guard: never write a sidecar for a file that is itself a sidecar.
        if (filePath.EndsWith(_opts.SidecarExtension, StringComparison.OrdinalIgnoreCase))
            return;

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
