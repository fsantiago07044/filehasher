namespace FileHasher;

/// <summary>Immutable snapshot of all user-selected options passed to the worker.</summary>
public sealed record HashOptions(
    string  TargetPath,
    bool    IsFile,
    string  Algorithm,          // "MD5" | "SHA1" | "SHA256" | "SHA512"
    bool    IncludeMetadata,
    bool    WriteSidecarHashes,
    string  SidecarExtension,   // e.g. ".sha256"
    string  SidecarFormat,      // "sha256sum" (HASH *filename — the {algo}sum tool line format)
                                // | "hashonly" (bare hash)
                                // | "extended" (HASH *filename *lastModifiedIso8601Utc *sizeBytes)
    bool    ExportCsv,
    string  CsvPath,
    bool    AllFileTypes,       // false = .exe/.msi only when scanning a folder
    bool    DescendIntoMsi,     // EXPERIMENTAL (feature branch): when true, .msi files are also
                                // extracted via the Windows Installer database API and the inner
                                // files are hashed individually in addition to the MSI itself.

    // How far to descend below a folder target.
    //
    //   null  unlimited (the default, and what every Windows release has done)
    //   0     the target folder only, no subdirectories
    //   N     N levels below the target
    //
    // Defaulted so existing callers keep the unbounded walk they already had.
    // Negative values are treated as 0 by the walk rather than throwing, since
    // this is a user-facing setting and a clamp is friendlier than a crash.
    int?    MaxDepth = null
);
