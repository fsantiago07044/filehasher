namespace FileHasher;

/// <summary>Immutable snapshot of all user-selected options passed to the worker.</summary>
internal sealed record HashOptions(
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
    bool    DescendIntoMsi      // EXPERIMENTAL (feature branch): when true, .msi files are also
                                // extracted via the Windows Installer database API and the inner
                                // files are hashed individually in addition to the MSI itself.
);
