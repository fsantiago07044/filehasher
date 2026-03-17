namespace FileHasher;

/// <summary>Immutable snapshot of all user-selected options passed to the worker.</summary>
internal sealed record HashOptions(
    string  TargetPath,
    bool    IsFile,
    string  Algorithm,          // "MD5" | "SHA1" | "SHA256" | "SHA512"
    bool    IncludeMetadata,
    bool    WriteSidecarHashes,
    string  SidecarExtension,   // e.g. ".sha256"
    string  SidecarFormat,      // "sha256sum" | "hashonly"
    bool    ExportCsv,
    string  CsvPath,
    bool    AllFileTypes        // false = .exe/.msi only when scanning a folder
);
