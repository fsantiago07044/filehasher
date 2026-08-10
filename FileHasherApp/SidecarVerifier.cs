using System.Globalization;
using System.Security.Cryptography;

namespace FileHasher;

/// <summary>
/// Background worker that verifies previously written sidecar hash files.
/// Mirrors <see cref="HashWorker"/>'s two-phase shape: <see cref="EnumerateAsync"/>
/// collects the work list, <see cref="VerifyAllAsync"/> processes it. Events are
/// raised on thread-pool threads — callers must marshal to the UI thread.
///
/// The hash algorithm is auto-detected per sidecar from its hash length
/// (32 hex chars = MD5, 40 = SHA1, 64 = SHA256, 128 = SHA512), so verification
/// is independent of the algorithm currently selected in the UI. All three
/// sidecar formats parse: bare hash, "HASH *filename", and the extended
/// "HASH *filename *lastModifiedIso8601Utc *sizeBytes". The hash alone decides
/// pass/fail; a differing embedded filename, date, or size on an otherwise-OK
/// row is surfaced as an informational note.
/// </summary>
internal sealed class SidecarVerifier
{
    /// <summary>One unit of verification work. Null SidecarPath = audit row for a file lacking a sidecar.</summary>
    internal sealed record VerifyWorkItem(string BaseFile, string? SidecarPath);

    private readonly string _targetPath;
    private readonly bool   _isFile;
    private readonly string _sidecarExtension;
    private readonly bool   _allFileTypes;
    private readonly Logger _logger;

    public event Action<string>?       WarningRaised;
    public event Action<VerifyResult>? SidecarVerified;

    public SidecarVerifier(string targetPath, bool isFile, string sidecarExtension,
                           bool allFileTypes, Logger logger)
    {
        _targetPath       = targetPath;
        _isFile           = isFile;
        _sidecarExtension = sidecarExtension;
        _allFileTypes     = allFileTypes;
        _logger           = logger;
    }

    // ── Phase 1: enumeration ─────────────────────────────────────────────────

    public Task<List<VerifyWorkItem>> EnumerateAsync(CancellationToken ct)
        => Task.Run(() => CollectWork(ct), ct);

    private List<VerifyWorkItem> CollectWork(CancellationToken ct)
    {
        var ext = _sidecarExtension;

        if (_isFile)
        {
            // A sidecar was targeted directly → verify it against its base file.
            if (_targetPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return new List<VerifyWorkItem> { new(_targetPath[..^ext.Length], _targetPath) };

            // A regular file was targeted → verify its sidecar, or report the gap.
            var sidecar = _targetPath + ext;
            return new List<VerifyWorkItem> { new(_targetPath, File.Exists(sidecar) ? sidecar : null) };
        }

        // Folder: one recursive walk (same warning behavior as HashWorker),
        // then partition into sidecars and filter-matching files lacking one.
        var all   = new List<string>();
        var stack = new Stack<string>();
        stack.Push(_targetPath);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            try
            {
                all.AddRange(Directory.EnumerateFiles(dir));
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

        var items = new List<VerifyWorkItem>();
        var bases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in all)
        {
            if (!f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                continue;
            var baseFile = f[..^ext.Length];
            bases.Add(baseFile);
            items.Add(new VerifyWorkItem(baseFile, f));
        }

        // Completeness audit: files the hashing scan filter would pick up
        // (same rule as HashWorker.CollectFiles) that have no sidecar.
        var extensions = _allFileTypes
            ? null
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi" };

        foreach (var f in all)
        {
            if (f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                continue;
            if (extensions is not null && !extensions.Contains(Path.GetExtension(f)))
                continue;
            if (!bases.Contains(f))
                items.Add(new VerifyWorkItem(f, null));
        }

        items.Sort((a, b) => string.Compare(a.BaseFile, b.BaseFile, StringComparison.OrdinalIgnoreCase));
        return items;
    }

    // ── Phase 2: verification ────────────────────────────────────────────────

    public async Task<VerifySummary> VerifyAllAsync(
        List<VerifyWorkItem> items, IProgress<int> progress, CancellationToken ct)
    {
        int ok = 0, mismatch = 0, missing = 0, noSidecar = 0, parse = 0, read = 0;
        int done = 0;

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var r = await VerifyOneAsync(item, ct);

            switch (r.Status)
            {
                case VerifyStatus.Ok:          ok++;        break;
                case VerifyStatus.Mismatch:    mismatch++;  break;
                case VerifyStatus.MissingFile: missing++;   break;
                case VerifyStatus.NoSidecar:   noSidecar++; break;
                case VerifyStatus.ParseError:  parse++;     break;
                default:                       read++;      break;
            }

            _logger.LogInfo($"VERIFY {r.Status}: {r.FilePath}" +
                            (r.Detail is null ? "" : $"  ({r.Detail})"));

            SidecarVerified?.Invoke(r);
            progress.Report(++done);
        }

        return new VerifySummary(ok, mismatch, missing, noSidecar, parse, read);
    }

    private async Task<VerifyResult> VerifyOneAsync(VerifyWorkItem item, CancellationToken ct)
    {
        if (item.SidecarPath is null)
            return new VerifyResult(item.BaseFile, "", VerifyStatus.NoSidecar,
                                    null, null, "no sidecar found for this file");

        string line;
        try
        {
            var lines = await File.ReadAllLinesAsync(item.SidecarPath, ct);
            line = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new VerifyResult(item.BaseFile, item.SidecarPath, VerifyStatus.ReadError,
                                    null, null, $"cannot read sidecar: {ex.Message}");
        }

        // All three formats are "HASH" optionally followed by " *"-prefixed
        // fields (filename, then ISO date and size for extended). A filename
        // containing the literal sequence " *" would split wrong; accepted —
        // it only affects the informational notes, never pass/fail.
        var fields   = line.Split(" *", StringSplitOptions.None);
        var expected = fields[0].Trim();

        if (expected.Length == 0 || !expected.All(Uri.IsHexDigit) ||
            AlgorithmFromHashLength(expected.Length) is not string algorithm)
        {
            return new VerifyResult(item.BaseFile, item.SidecarPath, VerifyStatus.ParseError,
                                    null, null, $"unrecognized sidecar content: \"{Truncate(line, 60)}\"");
        }

        if (!File.Exists(item.BaseFile))
            return new VerifyResult(item.BaseFile, item.SidecarPath, VerifyStatus.MissingFile,
                                    algorithm, null, "sidecar present but the file is missing");

        string actual;
        try
        {
            actual = await ComputeHashAsync(item.BaseFile, algorithm, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new VerifyResult(item.BaseFile, item.SidecarPath, VerifyStatus.ReadError,
                                    algorithm, null, $"cannot read file: {ex.Message}");
        }

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return new VerifyResult(item.BaseFile, item.SidecarPath, VerifyStatus.Mismatch,
                                    algorithm, actual, $"expected {expected}, computed {actual}");

        // Hash matches — remaining fields are informational only.
        var notes = new List<string>();
        try
        {
            var fi = new FileInfo(item.BaseFile);

            if (fields.Length >= 2 && fields[1].Length > 0 &&
                !fields[1].Equals(fi.Name, StringComparison.OrdinalIgnoreCase))
                notes.Add($"sidecar filename \"{fields[1]}\" differs from \"{fi.Name}\"");

            if (fields.Length >= 4)
            {
                if (DateTime.TryParseExact(fields[2], "yyyy-MM-dd'T'HH:mm:ss'Z'",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var sidecarUtc))
                {
                    // Compare at whole-second precision — that's all the sidecar stores.
                    var fileUtc = fi.LastWriteTimeUtc;
                    fileUtc = fileUtc.AddTicks(-(fileUtc.Ticks % TimeSpan.TicksPerSecond));
                    if (sidecarUtc != fileUtc)
                        notes.Add($"modified date differs (sidecar {fields[2]}, file {fileUtc:yyyy-MM-ddTHH:mm:ssZ})");
                }

                if (long.TryParse(fields[3], out var sidecarSize) && sidecarSize != fi.Length)
                    notes.Add($"size differs (sidecar {sidecarSize:N0}, file {fi.Length:N0})");
            }
        }
        catch
        {
            // Notes are best-effort; a metadata read failure never demotes an OK row.
        }

        return new VerifyResult(item.BaseFile, item.SidecarPath, VerifyStatus.Ok,
                                algorithm, actual,
                                notes.Count > 0 ? string.Join("; ", notes) : null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? AlgorithmFromHashLength(int hexChars) => hexChars switch
    {
        32  => "MD5",
        40  => "SHA1",
        64  => "SHA256",
        128 => "SHA512",
        _   => null
    };

    private static async Task<string> ComputeHashAsync(string filePath, string algorithm, CancellationToken ct)
    {
        using HashAlgorithm algo = algorithm switch
        {
            "MD5"    => MD5.Create(),
            "SHA1"   => SHA1.Create(),
            "SHA512" => SHA512.Create(),
            _        => SHA256.Create()
        };
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                          FileShare.ReadWrite, bufferSize: 65_536,
                                          useAsync: true);
        return Convert.ToHexString(await algo.ComputeHashAsync(stream, ct));
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}
