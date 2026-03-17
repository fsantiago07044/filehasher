namespace FileHasher;

/// <summary>Outcome of hashing a single file.</summary>
internal sealed record HashResult(
    string   FilePath,
    string   Hash,           // uppercase hex; empty on failure
    long?    Length,         // null when IncludeMetadata is false or on failure
    DateTime? LastWriteUtc, // null when IncludeMetadata is false or on failure
    bool     Success,
    string?  ErrorMessage
);
