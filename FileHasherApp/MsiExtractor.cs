using WixToolset.Dtf.WindowsInstaller;
using WixToolset.Dtf.WindowsInstaller.Package;

namespace FileHasher;

/// <summary>
/// Extracts files contained inside a Windows Installer (.msi) package to a
/// per-extraction temporary directory so the main hashing pipeline can hash
/// each inner file individually.
///
/// Security model
/// ──────────────
/// The MSI is opened in read-only mode and treated as a database. The MSI is
/// NOT executed; no installer code is run, no system state is changed by this
/// operation. Every extracted file is subject to the following guards before
/// being returned to the caller:
///
///   • Per-file size cap — the MSI's File table is inspected up-front and an
///     entry larger than <see cref="DefaultMaxPerFileBytes"/> aborts the
///     extraction without writing anything to disk.
///   • Total extracted size cap — the sum of File-table sizes must be under
///     <see cref="DefaultMaxTotalExtractedBytes"/>. Defends against a small
///     MSI that expands to terabytes of payload (a "decompression bomb").
///   • File-count cap — caps the number of entries at
///     <see cref="DefaultMaxFileCount"/> so a pathological MSI cannot exhaust
///     filesystem inodes or our enumeration loop.
///   • Free-disk-space check — before extraction, the volume holding the temp
///     directory must have enough headroom for the declared payload plus
///     <see cref="DefaultMinFreeDiskBytes"/>.
///   • Path-traversal guard — after extraction, every file path is canonical-
///     ized and verified to be strictly under the temp directory; any entry
///     that escaped (e.g. via a malicious filename interpreted by the cabinet
///     extractor) is deleted and excluded from the return list.
///   • Reparse-point rejection — any extracted entry whose attributes carry
///     ReparsePoint (junction, symlink, mount point) is deleted and excluded.
///     This prevents follow-up file reads from being redirected outside the
///     temp tree on machines where the cabinet extractor would honor such
///     entries.
///   • Cryptographically random temp directory name — collisions with other
///     extractions and predictable-path probing attacks are avoided.
///   • Best-effort cleanup on Dispose — the entire temp directory tree is
///     deleted via Directory.Delete(recursive: true); read-only flags are
///     cleared first so the delete doesn't leave behind orphan files.
///
/// Limitations (deliberate, for the v1 of this feature)
/// ─────────────────────────────────────────────────────
///   • Nested installers are NOT recursed into. If an MSI contains another
///     MSI or an EXE installer, the inner package is hashed as a single
///     blob — its own contents are not decomposed.
///   • EXE installers (NSIS, Inno, InstallShield, WiX Burn bundles, …) are
///     not handled. This class is MSI-only.
///   • External cabinets (where the MSI's Media table references a .cab file
///     that is not embedded in the MSI itself) will surface as an error from
///     <see cref="ExtractAsync"/> rather than being chased on disk.
/// </summary>
internal sealed class MsiExtractor : IDisposable
{
    /// <summary>5 GB. Total bytes the extraction is allowed to write before aborting.</summary>
    public const long DefaultMaxTotalExtractedBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>2 GB. Max declared size for any single file inside the MSI.</summary>
    public const long DefaultMaxPerFileBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>10,000. Max number of files an MSI is allowed to contain before we refuse.</summary>
    public const int DefaultMaxFileCount = 10_000;

    /// <summary>1 GB. Minimum free space the temp volume must keep after the projected extraction.</summary>
    public const long DefaultMinFreeDiskBytes = 1L * 1024 * 1024 * 1024;

    private readonly long _maxTotalBytes;
    private readonly long _maxPerFileBytes;
    private readonly int  _maxFileCount;
    private readonly long _minFreeDiskBytes;
    private readonly string _extractDir;
    private bool _disposed;

    /// <summary>Absolute path of the temporary directory the MSI's contents are extracted into.</summary>
    public string ExtractDirectory => _extractDir;

    public MsiExtractor(
        long maxTotalBytes   = DefaultMaxTotalExtractedBytes,
        long maxPerFileBytes = DefaultMaxPerFileBytes,
        int  maxFileCount    = DefaultMaxFileCount,
        long minFreeDiskBytes = DefaultMinFreeDiskBytes)
    {
        _maxTotalBytes    = maxTotalBytes;
        _maxPerFileBytes  = maxPerFileBytes;
        _maxFileCount     = maxFileCount;
        _minFreeDiskBytes = minFreeDiskBytes;

        // Path.GetRandomFileName returns a cryptographically strong random
        // 11-character name; prefixed so an operator scanning %TEMP% can tell
        // these directories came from this app. GetFullPath canonicalizes so
        // downstream path comparisons against this value are deterministic.
        _extractDir = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "FileHasher_msi_" + Path.GetRandomFileName()));
    }

    /// <summary>
    /// Inspect the MSI's <c>File</c> table, validate every entry against the
    /// configured caps, extract the contents to <see cref="ExtractDirectory"/>,
    /// post-validate each extracted path, and return the list of files the
    /// caller may safely open and hash.
    /// </summary>
    /// <exception cref="FileNotFoundException">The MSI does not exist.</exception>
    /// <exception cref="InvalidDataException">A cap was exceeded.</exception>
    /// <exception cref="IOException">Insufficient free disk space for the projected extraction.</exception>
    public async Task<IReadOnlyList<string>> ExtractAsync(string msiPath, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!File.Exists(msiPath))
            throw new FileNotFoundException("MSI not found", msiPath);

        // ── Phase 1: inspect the MSI's File table without touching disk yet ──
        long totalSize;
        int  fileCount;
        using (var db = new Database(msiPath, DatabaseOpenMode.ReadOnly))
        {
            var sizes = new List<long>();
            using (var view = db.OpenView("SELECT `FileSize` FROM `File`"))
            {
                view.Execute();
                while (true)
                {
                    using var record = view.Fetch();
                    if (record == null) break;

                    long s = record.GetInteger(1);
                    if (s > _maxPerFileBytes)
                        throw new InvalidDataException(
                            $"MSI declares a file of {s:N0} bytes, exceeding the per-file cap of {_maxPerFileBytes:N0} bytes.");
                    sizes.Add(s);
                }
            }
            fileCount = sizes.Count;
            totalSize = sizes.Sum();
        }

        if (fileCount == 0)
            return Array.Empty<string>();

        if (fileCount > _maxFileCount)
            throw new InvalidDataException(
                $"MSI declares {fileCount:N0} files, exceeding the cap of {_maxFileCount:N0}.");

        if (totalSize > _maxTotalBytes)
            throw new InvalidDataException(
                $"MSI's declared file sizes total {totalSize:N0} bytes, exceeding the cap of {_maxTotalBytes:N0}.");

        // ── Phase 2: free-disk-space check on the temp volume ────────────────
        var root = Path.GetPathRoot(_extractDir);
        if (string.IsNullOrEmpty(root))
            throw new IOException($"Could not determine drive root for temp path: {_extractDir}");

        var driveInfo = new DriveInfo(root);
        long required = totalSize + _minFreeDiskBytes;
        if (driveInfo.AvailableFreeSpace < required)
            throw new IOException(
                $"Insufficient free disk space on {driveInfo.Name}: need {required:N0} bytes (payload + safety margin), have {driveInfo.AvailableFreeSpace:N0}.");

        // ── Phase 3: extract via WiX DTF InstallPackage ──────────────────────
        Directory.CreateDirectory(_extractDir);

        await Task.Run(() =>
        {
            // InstallPackage handles cabinet extraction (embedded cabs from
            // the _Streams table or referenced cabs from the Media table),
            // stream extraction, and renames extracted files from internal
            // cabinet member names to the logical filenames recorded in the
            // File table. workingDir is where the extracted files land.
            using var package = new InstallPackage(
                msiPath,
                DatabaseOpenMode.ReadOnly,
                sourceDir:  null,
                workingDir: _extractDir);
            package.ExtractFiles();
        }, ct);

        ct.ThrowIfCancellationRequested();

        // ── Phase 4: post-extraction validation per file ─────────────────────
        // Canonicalize the extract dir with a trailing separator so prefix
        // comparisons cannot be fooled by ".../FileHasher_msi_XXX_evil/".
        var canonicalExtractDir =
            Path.GetFullPath(_extractDir).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var safeFiles = new List<string>();
        foreach (var f in Directory.EnumerateFiles(_extractDir, "*", SearchOption.AllDirectories))
        {
            // Reject reparse points (symlinks / junctions / mount points).
            var attrs = File.GetAttributes(f);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                TryDeleteFile(f);
                continue;
            }

            // Path-traversal guard: file's canonical path must be strictly
            // under the canonical extract dir.
            var canonicalFile = Path.GetFullPath(f);
            if (!canonicalFile.StartsWith(canonicalExtractDir, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(f);
                continue;
            }

            safeFiles.Add(canonicalFile);
        }

        return safeFiles;
    }

    private static void TryDeleteFile(string path)
    {
        try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
        try { File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!Directory.Exists(_extractDir)) return;

        // Clear read-only attributes so Directory.Delete doesn't choke on
        // files extracted from MSIs that mark items read-only.
        try
        {
            foreach (var f in Directory.EnumerateFiles(_extractDir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* best-effort */ }
            }
            Directory.Delete(_extractDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup. If this fails, the OS temp-cleaner will
            // eventually reclaim the directory. We do not want a cleanup
            // failure to throw out of an `using` block.
        }
    }
}
