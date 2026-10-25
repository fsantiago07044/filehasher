using FileHasher;
using Xunit;

namespace FileHasher.Cli.Tests;

/// <summary>
/// Parsing lives in Core so all three front ends agree about what a typed
/// list means. These pin that agreement.
/// </summary>
public sealed class FileTypeFilterTests
{
    [Theory]
    [InlineData("deb")]
    [InlineData(".deb")]
    [InlineData("DEB")]
    [InlineData("*.deb")]
    [InlineData("  deb  ")]
    public void A_type_is_recognised_however_it_is_written(string input)
        => Assert.Contains(".deb", FileTypeFilter.Parse(input)!);

    [Theory]
    [InlineData("deb,rpm")]
    [InlineData("deb, rpm")]
    [InlineData("deb;rpm")]
    [InlineData("deb rpm")]
    [InlineData("deb,,rpm,")]
    public void Separators_are_forgiving(string input)
    {
        var set = FileTypeFilter.Parse(input)!;
        Assert.Equal(2, set.Count);
        Assert.Contains(".rpm", set);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,,")]
    public void Nothing_usable_means_no_filter(string? input)
        => Assert.Null(FileTypeFilter.Parse(input));

    [Fact]
    public void A_multi_part_suffix_matches_the_whole_thing()
    {
        // Path.GetExtension("x.tar.gz") is ".gz", but a person typing tar.gz
        // means the pair, and ".gz" alone would also match a bare x.gz.
        var set = FileTypeFilter.Parse("tar.gz")!;
        Assert.True(FileTypeFilter.Matches("/tmp/archive.tar.gz", set));
        Assert.False(FileTypeFilter.Matches("/tmp/archive.gz", set));
    }

    [Fact]
    public void Matching_ignores_case_and_looks_at_the_name_not_the_directory()
    {
        var set = FileTypeFilter.Parse("deb")!;
        Assert.True(FileTypeFilter.Matches("/tmp/pkg.DEB", set));
        Assert.False(FileTypeFilter.Matches("/tmp/deb/notapackage.txt", set));
    }

    [Fact]
    public void An_AppImage_is_matched_by_its_suffix()
    {
        var set = FileTypeFilter.Parse("AppImage")!;
        Assert.True(FileTypeFilter.Matches("/tmp/FileHasher-1.0.0-x86_64.AppImage", set));
    }
}
