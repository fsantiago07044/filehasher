using FileHasher;
using Xunit;

namespace FileHasher.Cli.Tests;

/// <summary>
/// Pins option 2 as chosen on 2026-10-25: refuse only the descent that would
/// never terminate, and change nothing else. The second test is the important
/// one, because it is what separates this from the visited-set approach that
/// was rejected.
/// </summary>
public sealed class SymlinkGuardTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fh-guard-" + Guid.NewGuid().ToString("N"));

    public SymlinkGuardTests() => Directory.CreateDirectory(Path.Combine(_root, "data", "sub"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    string Data => Path.Combine(_root, "data");

    [Fact]
    public void A_symlink_to_its_own_parent_is_refused()
    {
        var link = Path.Combine(Data, "loop");
        Directory.CreateSymbolicLink(link, Data);
        Assert.True(SymlinkGuard.WouldLoop(link, Data));
    }

    [Fact]
    public void A_symlink_to_a_higher_ancestor_is_refused()
    {
        var deep = Path.Combine(Data, "sub");
        var link = Path.Combine(deep, "up");
        Directory.CreateSymbolicLink(link, Data);     // target is an ancestor of deep
        Assert.True(SymlinkGuard.WouldLoop(link, deep));
    }

    [Fact]
    public void A_symlink_that_merely_duplicates_a_sibling_is_still_followed()
    {
        // This is the whole reason ancestor-only was chosen over a visited set:
        // two routes to the same directory is not a loop, and pruning it would
        // change results for trees that terminate perfectly well today.
        var link = Path.Combine(Data, "alias");
        Directory.CreateSymbolicLink(link, Path.Combine(Data, "sub"));
        Assert.False(SymlinkGuard.WouldLoop(link, Data));
    }

    [Fact]
    public void An_ordinary_directory_is_never_a_loop()
        => Assert.False(SymlinkGuard.WouldLoop(Path.Combine(Data, "sub"), Data));

    [Fact]
    public void A_broken_link_is_not_reported_as_a_loop()
    {
        // It is an error for the caller's own handling to report, not a reason
        // to silently prune a directory.
        var link = Path.Combine(Data, "dangling");
        Directory.CreateSymbolicLink(link, Path.Combine(_root, "does-not-exist"));
        Assert.False(SymlinkGuard.WouldLoop(link, Data));
    }
}
