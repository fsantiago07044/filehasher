using System.Diagnostics;
using FileHasher;
using Xunit;

namespace FileHasher.Cli.Tests;

/// <summary>
/// A named pipe with no writer blocks forever when anything opens it, so a
/// folder containing one used to wedge the whole run: no error, no progress,
/// nothing to explain it. /tmp on both Linux test machines holds a dozen,
/// left by .NET's own diagnostics, which is how this was found.
///
/// Unix only, and skipped rather than failed on Windows, where the shape does
/// not exist and the whole guard is a no-op.
/// </summary>
public sealed class SpecialFileGuardTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "fh-pipe-" + Guid.NewGuid().ToString("N"));
    static bool OnUnix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public SpecialFileGuardTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    string MakeFifo(string name)
    {
        var path = Path.Combine(_dir, name);
        using var p = Process.Start(new ProcessStartInfo("mkfifo", [path]) { UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        return path;
    }

    string MakeFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_named_pipe_is_not_a_regular_file()
    {
        if (!OnUnix) return;
        Assert.False(SpecialFileGuard.IsRegularFile(MakeFifo("pipe"), 0));
    }

    [Fact]
    public void An_empty_file_still_is_one()
    {
        // The guard only probes zero-length entries, so this is the case it
        // must not get wrong: an empty file is perfectly hashable.
        Assert.True(SpecialFileGuard.IsRegularFile(MakeFile("empty.txt", ""), 0));
    }

    [Fact]
    public void A_file_with_content_is_never_probed_at_all()
        => Assert.True(SpecialFileGuard.IsRegularFile(MakeFile("a.txt", "hello"), 5));

    [Fact]
    public void A_path_that_does_not_exist_is_left_to_the_read_to_report()
        => Assert.True(SpecialFileGuard.IsRegularFile(Path.Combine(_dir, "nope"), 0));

    [Fact]
    public void The_walk_skips_the_pipe_warns_and_hashes_everything_else()
    {
        if (!OnUnix) return;

        MakeFifo("pipe");
        MakeFile("a.txt", "alpha");
        MakeFile("empty.txt", "");

        var warnings = new List<string>();
        var skipped = SpecialFileGuard.RegularFilesIn(_dir, warnings.Add).Select(Path.GetFileName).Order().ToList();

        Assert.Equal(["a.txt", "empty.txt"], skipped);
        Assert.EndsWith("pipe", Assert.Single(warnings));
    }

    [Fact]
    public async Task A_run_over_a_folder_with_a_pipe_in_it_finishes()
    {
        if (!OnUnix) return;

        MakeFifo("pipe");
        MakeFile("a.txt", "alpha");

        var opts = new HashOptions(_dir, false, "SHA256", false, false, ".sha256", "sum",
                                   false, "", AllFileTypes: true, DescendIntoMsi: false, MaxDepth: 0);
        using var logger = new Logger();
        var worker = new HashWorker(opts, logger);

        var warned = new List<string>();
        worker.WarningRaised += warned.Add;

        // The regression was an indefinite hang, so the test has to be able to
        // fail rather than run forever.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var files = await worker.EnumerateAsync(cts.Token);

        Assert.Equal(["a.txt"], files.Select(Path.GetFileName));
        Assert.Contains(warned, w => w.Contains("not a regular file"));
    }
}
