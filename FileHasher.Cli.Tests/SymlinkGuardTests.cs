using FileHasher;
using Xunit;

namespace FileHasher.Cli.Tests;

/// <summary>
/// Pins option 2 as chosen on 2026-10-25: refuse only the descent that would
/// never terminate, and change nothing else. The sibling test is what
/// separates this from the visited-set approach that was rejected, and the
/// symlinked-ancestor test is the bug that shipped in the first version.
/// </summary>
public sealed class SymlinkGuardTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fh-guard-" + Guid.NewGuid().ToString("N"));

    public SymlinkGuardTests() => Directory.CreateDirectory(Path.Combine(_root, "data", "sub"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    string Data => Path.Combine(_root, "data");

    // The walks canonicalise the root once and carry the result down, so the
    // guard is always handed a canonical current directory.
    static bool Loops(string sub, string current) =>
        SymlinkGuard.WouldLoop(sub, SymlinkGuard.Canonical(current), out _);

    [Fact]
    public void A_symlink_to_its_own_parent_is_refused()
    {
        var link = Path.Combine(Data, "loop");
        Directory.CreateSymbolicLink(link, Data);
        Assert.True(Loops(link, Data));
    }

    [Fact]
    public void A_symlink_to_a_higher_ancestor_is_refused()
    {
        var deep = Path.Combine(Data, "sub");
        var link = Path.Combine(deep, "up");
        Directory.CreateSymbolicLink(link, Data);     // target is an ancestor of deep
        Assert.True(Loops(link, deep));
    }

    [Fact]
    public void A_loop_is_caught_through_a_symlinked_ancestor()
    {
        // The regression. Path.GetFullPath leaves a symlinked ancestor alone
        // while resolving a link resolves the whole path, so the first version
        // compared "/gate/data" against "/<real>/data", matched nothing, and
        // descended until the operating system refused at about sixteen
        // levels. Reaching a folder through a symlinked parent is the default
        // on macOS, where /tmp is a link, and one accident away on Linux.
        var gate = Path.Combine(_root, "gate");
        Directory.CreateSymbolicLink(gate, Data);

        var viaGate = Path.Combine(gate, "sub");       // same directory, symlinked route
        var link    = Path.Combine(viaGate, "up");
        Directory.CreateSymbolicLink(link, Data);

        Assert.True(Loops(link, viaGate));
    }

    [Fact]
    public void Canonical_resolves_a_symlinked_ancestor()
    {
        var gate = Path.Combine(_root, "gate2");
        Directory.CreateSymbolicLink(gate, Data);

        Assert.Equal(SymlinkGuard.Canonical(Path.Combine(Data, "sub")),
                     SymlinkGuard.Canonical(Path.Combine(gate, "sub")));
    }

    [Fact]
    public void A_relative_path_is_treated_as_the_absolute_one()
    {
        var before = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_root);
            var link = Path.Combine(Data, "rel");
            Directory.CreateSymbolicLink(link, Data);

            Assert.True(SymlinkGuard.WouldLoop(Path.Combine("data", "rel"),
                                               SymlinkGuard.Canonical("data"), out _));
        }
        finally { Directory.SetCurrentDirectory(before); }
    }

    [Fact]
    public void A_symlink_that_merely_duplicates_a_sibling_is_still_followed()
    {
        // This is the whole reason ancestor-only was chosen over a visited set:
        // two routes to the same directory is not a loop, and pruning it would
        // change results for trees that terminate perfectly well today.
        var link = Path.Combine(Data, "alias");
        Directory.CreateSymbolicLink(link, Path.Combine(Data, "sub"));
        Assert.False(Loops(link, Data));
    }

    [Fact]
    public void A_followed_symlink_reports_its_target_as_the_next_canonical_path()
    {
        // Without this the level below the link would compare against a path
        // that still contains the link, and a loop one level deeper would be
        // missed for the same reason the ancestor bug was.
        var link = Path.Combine(Data, "alias2");
        Directory.CreateSymbolicLink(link, Path.Combine(Data, "sub"));

        Assert.False(SymlinkGuard.WouldLoop(link, SymlinkGuard.Canonical(Data), out var next));
        Assert.Equal(SymlinkGuard.Canonical(Path.Combine(Data, "sub")), next);
    }

    [Fact]
    public void An_ordinary_directory_is_never_a_loop()
        => Assert.False(Loops(Path.Combine(Data, "sub"), Data));

    [Fact]
    public void An_ordinary_directory_extends_its_parents_canonical_path()
    {
        Assert.False(SymlinkGuard.WouldLoop(Path.Combine(Data, "sub"),
                                            SymlinkGuard.Canonical(Data), out var next));
        Assert.Equal(SymlinkGuard.Canonical(Path.Combine(Data, "sub")), next);
    }

    [Fact]
    public void A_broken_link_is_not_reported_as_a_loop()
    {
        // It is an error for the caller's own handling to report, not a reason
        // to silently prune a directory.
        var link = Path.Combine(Data, "dangling");
        Directory.CreateSymbolicLink(link, Path.Combine(_root, "does-not-exist"));
        Assert.False(Loops(link, Data));
    }
}
