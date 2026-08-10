namespace FileHasher;

/// <summary>Outcome category for one sidecar-verification row.</summary>
internal enum VerifyStatus
{
    Ok,            // hash matches the sidecar
    Mismatch,      // hash differs from the sidecar
    MissingFile,   // sidecar exists but the file it attests to is gone
    NoSidecar,     // file matches the scan filter but has no sidecar (audit row)
    ParseError,    // sidecar content not recognized as any supported format
    ReadError      // the file or its sidecar could not be read
}

/// <summary>Outcome of verifying a single sidecar (or a file lacking one).</summary>
internal sealed record VerifyResult(
    string       FilePath,      // the file the sidecar attests to
    string       SidecarPath,   // the sidecar itself; "" for NoSidecar rows
    VerifyStatus Status,
    string?      Algorithm,     // auto-detected from the sidecar's hash length; null if parsing failed
    string?      ComputedHash,  // actual hash of the file, when one was computed
    string?      Detail         // human-readable notes: mismatch expected/computed, metadata
                                // differences on OK rows, error messages
);

/// <summary>Per-status counts for a completed verification run.</summary>
internal sealed record VerifySummary(
    int Ok,
    int Mismatch,
    int MissingFile,
    int NoSidecar,
    int ParseError,
    int ReadError
);
