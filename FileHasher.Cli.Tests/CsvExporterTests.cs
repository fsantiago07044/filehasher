using FileHasher;
using Xunit;

namespace FileHasher.Cli.Tests;

/// <summary>
/// The CSV document used to live in the WinForms app, which is why the CLI's
/// documented --csv accepted a path and wrote nothing and the Linux app had no
/// export at all. These pin the format now that all three front ends share it,
/// because a CSV is something people diff between machines.
/// </summary>
public sealed class CsvExporterTests
{
    static HashOptions Opts(bool metadata = false, bool msi = false, string algo = "SHA256") =>
        new(TargetPath: "/t", IsFile: false, Algorithm: algo, IncludeMetadata: metadata,
            WriteSidecarHashes: false, SidecarExtension: ".sha256", SidecarFormat: "sum",
            ExportCsv: true, CsvPath: "/t/out.csv", AllFileTypes: true,
            DescendIntoMsi: msi, MaxDepth: null);

    static HashResult Ok(string path, string hash, long? len = null, DateTime? when = null,
                         string? container = null, string? dirId = null)
        => new(path, hash, len, when, true, null, container, dirId);

    static string[] Lines(string csv) =>
        csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

    [Fact]
    public void The_header_names_the_algorithm_that_was_used()
    {
        var csv = CsvExporter.Build([Ok("/t/a", "AA")], Opts(algo: "SHA512"));
        Assert.Equal("Path,SHA512", Lines(csv)[0]);
    }

    [Fact]
    public void Metadata_columns_appear_only_when_metadata_was_collected()
    {
        Assert.Equal("Path,SHA256", Lines(CsvExporter.Build([], Opts()))[0]);
        Assert.Equal("Path,SHA256,LengthBytes,LastWriteUtc",
                     Lines(CsvExporter.Build([], Opts(metadata: true)))[0]);
    }

    [Fact]
    public void Times_are_iso_8601_in_utc()
    {
        var csv = CsvExporter.Build(
            [Ok("/t/a", "AA", 7, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))],
            Opts(metadata: true));
        Assert.Equal("/t/a,AA,7,2026-01-02T03:04:05Z", Lines(csv)[1]);
    }

    [Fact]
    public void Failures_are_left_out()
    {
        // A row with an empty hash column reads as a file that hashed to
        // nothing. The log and the results table carry the errors.
        var results = new[]
        {
            Ok("/t/good", "AA"),
            new HashResult("/t/bad", "", null, null, false, "denied"),
        };
        var lines = Lines(CsvExporter.Build(results, Opts()));
        Assert.Equal(2, lines.Length);
        Assert.DoesNotContain(lines, l => l.Contains("/t/bad"));
    }

    [Fact]
    public void Rows_are_sorted_so_two_machines_produce_the_same_file()
    {
        var csv = CsvExporter.Build([Ok("/t/c", "CC"), Ok("/t/a", "AA"), Ok("/t/b", "BB")], Opts());
        Assert.Equal(["Path,SHA256", "/t/a,AA", "/t/b,BB", "/t/c,CC"], Lines(csv));
    }

    [Theory]
    [InlineData("/t/a,comma.txt",   "\"/t/a,comma.txt\"")]
    [InlineData("/t/say \"hi\".txt", "\"/t/say \"\"hi\"\".txt\"")]
    [InlineData("/t/plain.txt",     "/t/plain.txt")]
    public void Paths_are_quoted_when_they_have_to_be(string path, string expected)
        => Assert.Equal(expected, CsvExporter.Escape(path));

    [Fact]
    public void A_newline_in_a_path_is_quoted_rather_than_breaking_the_row()
    {
        // Legal on Linux and macOS, so not theoretical.
        var csv = CsvExporter.Build([Ok("/t/two\nlines.txt", "AA")], Opts());
        Assert.Contains("\"/t/two\nlines.txt\",AA", csv);
    }

    [Fact]
    public void Msi_columns_appear_only_for_an_inner_scan()
    {
        var r = Ok("/t/a.dll", "AA", container: "/t/p.msi", dirId: "INSTALLDIR");
        Assert.Equal("Path,SHA256,Container,MsiDirectoryId",
                     Lines(CsvExporter.Build([r], Opts(msi: true)))[0]);
        Assert.Equal("/t/a.dll,AA", Lines(CsvExporter.Build([r], Opts()))[1]);
    }

    [Fact]
    public void Verify_has_no_csv_so_the_option_is_refused_rather_than_ignored()
    {
        var ex = Assert.Throws<UsageException>(
            () => CommandLine.Parse(["verify", "/t", "--csv", "/t/out.csv"]));
        Assert.Contains("applies to 'hash'", ex.Message);
    }
}
