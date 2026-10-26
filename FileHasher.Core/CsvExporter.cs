using System.Text;

namespace FileHasher;

/// <summary>
/// Writes a run's results as CSV.
///
/// Lives in Core for the same reason <see cref="FileTypeFilter"/> does: this
/// was WinForms-only, so the CLI's documented <c>--csv</c> accepted a path,
/// set the options and wrote nothing at all, and the Linux app had no export
/// to offer. One implementation is the only way three front ends produce a
/// file a user can compare across platforms.
/// </summary>
public static class CsvExporter
{
    /// <summary>
    /// Sorted, headed, and written as UTF-8 with a BOM, which is what makes
    /// Excel open it without putting the user through the import wizard.
    /// </summary>
    public static void Write(string path, IEnumerable<HashResult> results, HashOptions opts)
        => File.WriteAllText(path, Build(results, opts),
                             new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    /// <summary>
    /// The whole document as a string. Rows are buffered rather than streamed
    /// so the order is deterministic: a spreadsheet is compared and diffed, and
    /// walk order is not stable between platforms or runs. For a tree large
    /// enough for that to matter, the hashing dominates the memory anyway.
    /// </summary>
    public static string Build(IEnumerable<HashResult> results, HashOptions opts)
    {
        var sb = new StringBuilder(4096);

        sb.Append("Path").Append(',').Append(opts.Algorithm);
        if (opts.IncludeMetadata) sb.Append(",LengthBytes,LastWriteUtc");
        if (opts.DescendIntoMsi)  sb.Append(",Container,MsiDirectoryId");
        sb.AppendLine();

        // Failures are deliberately absent: a CSV of hashes is something people
        // diff and re-verify against, and a row with an empty hash column reads
        // as a file that hashed to nothing. The log and the results table carry
        // the errors.
        foreach (var r in results.Where(r => r.Success)
                                 .OrderBy(r => r.Container ?? "", StringComparer.Ordinal)
                                 .ThenBy(r => r.FilePath, StringComparer.Ordinal))
        {
            sb.Append(Escape(r.FilePath)).Append(',').Append(r.Hash);
            if (opts.IncludeMetadata)
            {
                sb.Append(',').Append(r.Length);
                sb.Append(',').Append(r.LastWriteUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            }
            if (opts.DescendIntoMsi)
            {
                sb.Append(',').Append(Escape(r.Container ?? ""));
                sb.Append(',').Append(Escape(r.MsiDirectoryId ?? ""));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// RFC 4180 quoting. A path can contain a comma on every platform and a
    /// newline on Linux and macOS, so this is not theoretical.
    /// </summary>
    public static string Escape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
}
