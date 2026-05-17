namespace FileHasher;

/// <summary>Outcome of hashing a single file.</summary>
internal sealed record HashResult(
    string   FilePath,
    string   Hash,            // uppercase hex; empty on failure
    long?    Length,          // null when IncludeMetadata is false or on failure
    DateTime? LastWriteUtc,   // null when IncludeMetadata is false or on failure
    bool     Success,
    string?  ErrorMessage,
    string?  Container       = null, // null for top-level files; for files extracted from an MSI
                                     // installer, the full path of the parent .msi.
    string?  MsiDirectoryId  = null  // null for top-level files; for files extracted from an MSI,
                                     // the raw MSI Directory-table identifier the file sits under
                                     // (e.g. "PFiles64", "ProgramFiles64Folder", "INSTALLDIR").
                                     // FilePath is rewritten to a human-readable equivalent
                                     // where possible; this field preserves the original token.
);
