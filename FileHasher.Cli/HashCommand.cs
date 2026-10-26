using System.Text.Json;

namespace FileHasher.Cli;

public static class HashCommand
{
    public static async Task<int> RunAsync(ParsedArgs a, bool isFile, CancellationToken ct)
    {
        var opts = new HashOptions(
            TargetPath:         a.TargetPath,
            IsFile:             isFile,
            Algorithm:          a.Algorithm,
            IncludeMetadata:    a.Metadata,
            WriteSidecarHashes: a.Sidecar,
            SidecarExtension:   a.SidecarExt ?? CommandLine.DefaultSidecarExt(a.Algorithm),
            SidecarFormat:      a.SidecarFormat,
            ExportCsv:          a.CsvPath is not null,
            CsvPath:            a.CsvPath ?? string.Empty,
            AllFileTypes:       a.AllTypes,
            FileTypes:          FileTypeFilter.Parse(a.FileTypes),
            DescendIntoMsi:     a.MsiInner,
            MaxDepth:           a.MaxDepth);

        using var logger = new Logger();
        var worker = new HashWorker(opts, logger);

        var unreadable = 0;
        var hashed     = 0;

        // Only collected when a CSV was asked for. Everything else about this
        // command streams, and holding a hundred thousand results for a run
        // nobody wants a spreadsheet from would be a pointless cost.
        var forCsv = opts.ExportCsv ? new List<HashResult>() : null;

        worker.WarningRaised += w =>
        {
            if (!a.Quiet && !a.Json) Console.Error.WriteLine($"warning: {w}");
        };

        worker.FileHashed += r =>
        {
            if (r.Success) hashed++; else unreadable++;
            forCsv?.Add(r);

            // Streaming, one object per line: a hundred-thousand-file tree must
            // not be buffered before the caller sees anything
            // (docs/cli-design.md, decision 2).
            if (a.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new HashEvent(
                    Type:      "file",
                    Path:      r.FilePath,
                    Hash:      r.Hash.Length == 0 ? null : r.Hash,
                    Algorithm: a.Algorithm,
                    Size:      r.Length,
                    Modified:  r.LastWriteUtc,
                    Container: r.Container,
                    Ok:        r.Success,
                    Error:     r.ErrorMessage), CliJsonContext.Default.HashEvent));
            }
            else if (!a.Quiet)
            {
                Console.WriteLine(r.Success
                    ? $"{r.Hash}  {r.FilePath}"
                    : $"{"ERROR",-64}  {r.FilePath}  ({r.ErrorMessage})");
            }
        };

        var files = await worker.EnumerateAsync(ct);
        if (!a.Quiet && !a.Json)
            Console.Error.WriteLine($"{files.Count} file(s) to hash.");

        await worker.HashAllAsync(files, new Progress<int>(), ct);

        if (forCsv is not null)
        {
            try
            {
                CsvExporter.Write(opts.CsvPath, forCsv, opts);
                if (!a.Quiet && !a.Json)
                    Console.Error.WriteLine($"CSV written: {opts.CsvPath}");
            }
            catch (Exception ex)
            {
                // The hashes are already out on stdout and are the point of the
                // run; a failed CSV is a tool failure, not a finding, so it
                // takes exit 3 rather than quietly leaving no file behind.
                Console.Error.WriteLine($"error: cannot write CSV to {opts.CsvPath}: {ex.Message}");
                return Program.ExitFailed;
            }
        }

        var empty = files.Count == 0;
        Output.Summary(a, new Dictionary<string, int>
        {
            ["hashed"]     = hashed,
            ["unreadable"] = unreadable,
        });

        // Defaults reproduce the exit-code table, so passing no --fail-on
        // behaves exactly as documented before the option existed.
        var fail =
            (a.FailOn.HasFlag(FailOn.Unreadable) && unreadable > 0) ||
            (a.FailOn.HasFlag(FailOn.Empty)      && empty);

        return fail ? Program.ExitFindings : Program.ExitOk;
    }
}
