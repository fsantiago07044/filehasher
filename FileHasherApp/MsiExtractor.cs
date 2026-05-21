using WixToolset.Dtf.WindowsInstaller;
using WixToolset.Dtf.WindowsInstaller.Package;

// WixToolset.Dtf.WindowsInstaller exports its own FileAttributes enum (mapping
// the MSI File table's Attributes column — Vital, ReadOnly, Hidden, etc.) that
// collides with System.IO.FileAttributes. This file only ever wants the System
// one (for reparse-point / read-only checks against the extracted files on
// disk), so alias FileAttributes -> System.IO.FileAttributes at the file level.
using FileAttributes = System.IO.FileAttributes;

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

        // Saturated addition: a caller passing a huge _minFreeDiskBytes (or a
        // pathological MSI with a near-int64 totalSize) would otherwise wrap
        // `required` to a negative value, and `AvailableFreeSpace < negative`
        // is always false — silently turning the disk-space cap into a no-op.
        // Detect the overflow ahead of the sum and clamp to long.MaxValue,
        // which makes the comparison correctly fire because no real disk
        // reports that much free.
        long required = (_minFreeDiskBytes > long.MaxValue - totalSize)
            ? long.MaxValue
            : totalSize + _minFreeDiskBytes;

        if (driveInfo.AvailableFreeSpace < required)
            throw new IOException(
                $"Insufficient free disk space on {driveInfo.Name}: need {required:N0} bytes (payload + safety margin), have {driveInfo.AvailableFreeSpace:N0}.");

        // ── Phase 3: extract via WiX DTF InstallPackage ──────────────────────
        Directory.CreateDirectory(_extractDir);

        // Snapshot the contents of the extract dir's parent immediately before
        // extraction. WiX DTF's InstallPackage uses the MSI's Directory table
        // to compute output paths, and a malicious DefaultDir like "..\..\foo"
        // can cause it to create files or directories ALONGSIDE our extract
        // dir rather than inside it. Phase 4's IsPathOutsideDirectory only
        // sees what we enumerate from inside the extract dir, so it cannot
        // catch sibling-level escapes on its own. By snapshotting beforehand
        // we can compute the precise set of new siblings that appeared during
        // extraction and treat them as escapes to be cleaned up.
        var parentDir = Path.GetDirectoryName(_extractDir);
        HashSet<string>? preExtractSiblings = null;
        if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
        {
            preExtractSiblings = new HashSet<string>(
                Directory.EnumerateFileSystemEntries(parentDir),
                StringComparer.OrdinalIgnoreCase);
        }

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

        // ── Phase 3.5: clean up sibling-level escapes ────────────────────────
        // Anything that appeared in the parent dir during our extraction and
        // isn't our extract dir was created by WiX DTF following an adversarial
        // Directory.DefaultDir or File.FileName value. Delete it. This is a
        // best-effort cleanup — if another process happens to create a temp
        // file in the same parent dir during our extraction window (rare in
        // practice for a few-seconds-long extraction in %TEMP%), it could be
        // caught by this sweep. The alternative — leaving the escape in place
        // — is strictly worse.
        if (preExtractSiblings is not null && !string.IsNullOrEmpty(parentDir))
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(parentDir))
            {
                if (preExtractSiblings.Contains(item)) continue;
                if (string.Equals(item, _extractDir, StringComparison.OrdinalIgnoreCase)) continue;
                TryDeleteAny(item);
            }
        }

        // ── Phase 4: post-extraction validation per file ─────────────────────
        // Canonicalize the extract dir with a trailing separator so prefix
        // comparisons cannot be fooled by ".../FileHasher_msi_XXX_evil/".
        var canonicalExtractDir =
            Path.GetFullPath(_extractDir).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var safeFiles = new List<string>();
        foreach (var f in Directory.EnumerateFiles(_extractDir, "*", SearchOption.AllDirectories))
        {
            if (IsReparsePoint(f) || IsPathOutsideDirectory(f, canonicalExtractDir))
            {
                TryDeleteFile(f);
                continue;
            }
            safeFiles.Add(Path.GetFullPath(f));
        }

        return safeFiles;
    }

    private static void TryDeleteFile(string path)
    {
        try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
        try { File.Delete(path); } catch { }
    }

    /// <summary>
    /// Deletes a filesystem entry whether it's a file, a directory, or doesn't
    /// exist any more. Used by Phase 3.5 to clean up sibling-level escapes
    /// without caring about which kind of entry WiX DTF created.
    /// </summary>
    private static void TryDeleteAny(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                // Clear read-only flags so Directory.Delete doesn't choke.
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* best-effort */ }
                }
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                try { File.SetAttributes(path, FileAttributes.Normal); } catch { /* best-effort */ }
                File.Delete(path);
            }
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// True when the file at <paramref name="filePath"/> is a reparse point
    /// (symbolic link, junction, mount point, or any other entry whose
    /// <see cref="System.IO.FileAttributes.ReparsePoint"/> bit is set). Used
    /// to exclude extracted entries whose attributes would silently redirect
    /// a subsequent file read outside the sandbox.
    ///
    /// Exposed as internal so it can be unit-tested directly; the production
    /// call site is the post-extraction loop in <see cref="ExtractAsync"/>.
    /// </summary>
    internal static bool IsReparsePoint(string filePath)
    {
        var attrs = File.GetAttributes(filePath);
        return (attrs & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>
    /// True when <paramref name="filePath"/>'s canonical absolute form is
    /// NOT under <paramref name="canonicalDirectoryWithTrailingSeparator"/>,
    /// i.e. the file escaped the sandbox via a "..\" segment or by being
    /// written to an entirely unrelated location.
    ///
    /// The caller must canonicalize the directory ahead of time (via
    /// <see cref="Path.GetFullPath(string)"/>) and append the platform's
    /// directory separator. Without the trailing separator a file in a
    /// sibling directory whose name shares a prefix — e.g.
    /// <c>"C:\Temp\foobar.txt"</c> against <c>"C:\Temp\foo"</c> — would be
    /// falsely accepted.
    ///
    /// Exposed as internal so the prefix-comparison edge cases can be
    /// unit-tested with synthetic paths; the production call site is the
    /// post-extraction loop in <see cref="ExtractAsync"/>.
    /// </summary>
    internal static bool IsPathOutsideDirectory(string filePath, string canonicalDirectoryWithTrailingSeparator)
    {
        var canonical = Path.GetFullPath(filePath);
        return !canonical.StartsWith(canonicalDirectoryWithTrailingSeparator, StringComparison.OrdinalIgnoreCase);
    }

    // ── MSI Directory-table identifier resolution ────────────────────────────
    //
    // When InstallPackage.ExtractFiles drops files under the working directory,
    // it uses the MSI Directory table's primary-key identifiers as folder names
    // (e.g. "PFiles64", "ProgramFilesFolder", "INSTALLDIR"). Most MSI authors
    // pick from a small set of Microsoft-documented "well-known" identifiers
    // that map to actual Windows shell folders at install time. For display
    // purposes we substitute the resolved Windows-friendly name; the original
    // identifier is preserved separately so audits can still see what the MSI
    // author wrote.

    /// <summary>
    /// Map of well-known MSI Directory-table identifiers to their resolved
    /// Windows-friendly equivalent (on a 64-bit Windows install). Lookup is
    /// case-insensitive. Any identifier NOT in this map is treated as an MSI
    /// author's custom name (e.g. "INSTALLDIR") and left in the path as-is.
    /// </summary>
    private static readonly Dictionary<string, string> WellKnownMsiDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "TARGETDIR",            ""                            },  // root — strip
            { "WindowsVolume",        ""                            },  // drive root — strip
            { "ProgramFilesFolder",   "Program Files (x86)"         },
            { "PFiles",               "Program Files (x86)"         },
            { "ProgramFiles64Folder", "Program Files"               },
            { "PFiles64",             "Program Files"               },
            { "CommonFilesFolder",    @"Program Files (x86)\Common Files" },
            { "CommonFiles64Folder",  @"Program Files\Common Files" },
            { "WindowsFolder",        "Windows"                     },
            { "SystemFolder",         @"Windows\System32"           },
            { "System64Folder",       @"Windows\System32"           },
            { "AppDataFolder",        @"AppData\Roaming"            },
            { "LocalAppDataFolder",   @"AppData\Local"              },
            { "CommonAppDataFolder",  @"ProgramData"                },
            { "DesktopFolder",        "Desktop"                     },
            { "StartMenuFolder",      "Start Menu"                  },
            { "ProgramMenuFolder",    @"Start Menu\Programs"        },
            { "StartupFolder",        @"Start Menu\Programs\Startup"},
            { "MyPicturesFolder",     "Pictures"                    },
            { "PersonalFolder",       "Documents"                   },
            { "FontsFolder",          @"Windows\Fonts"              },
            { "TempFolder",           "Temp"                        },
        };

    /// <summary>
    /// Given a path relative to the extract directory (e.g.
    /// <c>"PFiles64\msi-test\7za.exe"</c>), split off the leading MSI
    /// Directory-table identifier and return a tuple of
    /// <c>(humanReadablePath, originalIdentifier)</c>. The identifier is
    /// looked up against <see cref="WellKnownMsiDirectoryNames"/>; if it's
    /// well-known, the identifier is replaced in the returned path with its
    /// resolved Windows name. If it's not well-known (e.g. an MSI author's
    /// custom <c>INSTALLDIR</c>), the identifier is left in place. In all
    /// cases the second tuple element is the original raw identifier so
    /// callers can record it for audit display.
    ///
    /// Returns <c>(relativePath, null)</c> unchanged when the input has no
    /// directory component (the file sits at the extract-dir root).
    /// </summary>
    public static (string FilePath, string? MsiDirectoryId) ResolveDisplayPath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return (relativePath, null);

        var parts = relativePath.Split(Path.DirectorySeparatorChar, 2);
        if (parts.Length < 2)
            return (relativePath, null);  // bare filename — no identifier to extract

        var identifier = parts[0];
        var rest       = parts[1];

        if (WellKnownMsiDirectoryNames.TryGetValue(identifier, out var friendly))
        {
            var displayPath = string.IsNullOrEmpty(friendly)
                ? rest
                : Path.Combine(friendly, rest);
            return (displayPath, identifier);
        }

        // Unknown identifier — leave it in the displayed path, but still
        // surface it separately so the caller can show it in its own column.
        return (relativePath, identifier);
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
