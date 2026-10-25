using System.Text.Json;

namespace FileHasher.Cli;

public static class VerifyCommand
{
    public static async Task<int> RunAsync(ParsedArgs a, bool isFile, CancellationToken ct)
    {
        using var logger = new Logger();
        var verifier = new SidecarVerifier(
            targetPath:       a.TargetPath,
            isFile:           isFile,
            sidecarExtension: a.SidecarExt ?? ".sha256",
            allFileTypes:     a.AllTypes,
            logger:           logger,
            maxDepth:         a.MaxDepth,
            fileTypes:        FileTypeFilter.Parse(a.FileTypes));

        verifier.WarningRaised += w =>
        {
            if (!a.Quiet && !a.Json) Console.Error.WriteLine($"warning: {w}");
        };

        verifier.SidecarVerified += r =>
        {
            if (a.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new VerifyEvent(
                    Type:      "file",
                    Path:      r.FilePath,
                    Sidecar:   r.SidecarPath.Length == 0 ? null : r.SidecarPath,
                    Status:    r.Status.ToString().ToUpperInvariant(),
                    Algorithm: r.Algorithm,
                    Computed:  r.ComputedHash,
                    Detail:    r.Detail), CliJsonContext.Default.VerifyEvent));
            }
            else if (!a.Quiet || r.Status is not VerifyStatus.Ok)
            {
                var label = r.Status switch
                {
                    VerifyStatus.Ok          => "OK",
                    VerifyStatus.Mismatch    => "MISMATCH",
                    VerifyStatus.MissingFile => "MISSING FILE",
                    VerifyStatus.NoSidecar   => "NO SIDECAR",
                    VerifyStatus.ParseError  => "PARSE ERROR",
                    _                        => "READ ERROR",
                };
                var detail = r.Detail is { Length: > 0 } ? $"  ({r.Detail})" : "";
                Console.WriteLine($"{label,-13} {r.FilePath}{detail}");
            }
        };

        var items = await verifier.EnumerateAsync(ct);
        if (!a.Quiet && !a.Json)
            Console.Error.WriteLine($"{items.Count} item(s) to verify.");

        var s = await verifier.VerifyAllAsync(items, new Progress<int>(), ct);

        Output.Summary(a, new Dictionary<string, int>
        {
            ["ok"]           = s.Ok,
            ["mismatch"]     = s.Mismatch,
            ["missing_file"] = s.MissingFile,
            ["no_sidecar"]   = s.NoSidecar,
            ["parse_error"]  = s.ParseError,
            ["read_error"]   = s.ReadError,
        });

        // ParseError counts as unreadable: the sidecar exists but could not be
        // understood, which is a failure to verify rather than a clean pass.
        var fail =
            (a.FailOn.HasFlag(FailOn.Mismatch)   && s.Mismatch > 0) ||
            (a.FailOn.HasFlag(FailOn.Missing)    && s.MissingFile > 0) ||
            (a.FailOn.HasFlag(FailOn.NoSidecar)  && s.NoSidecar > 0) ||
            (a.FailOn.HasFlag(FailOn.Unreadable) && (s.ReadError > 0 || s.ParseError > 0)) ||
            (a.FailOn.HasFlag(FailOn.Empty)      && items.Count == 0);

        return fail ? Program.ExitFindings : Program.ExitOk;
    }
}
