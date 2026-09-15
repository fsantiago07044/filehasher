using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Direct unit tests against <see cref="MainForm.SeedFromPath"/>, the helper
/// that decides where the Browse dialogs open when the path box they belong to
/// already holds a value: the deepest surviving ancestor directory, plus the
/// leaf name when it is a child of that directory.
///
/// These tests do NOT drive the UI and do NOT require an interactive desktop
/// session or a MainForm instance; the helper is static and reachable via
/// <c>InternalsVisibleTo</c>. The dialogs themselves are shell windows and
/// stay outside the FlaUI suite's reach, so this is where the seeding logic
/// is pinned.
/// </summary>
public sealed class MainFormPathSeedTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyBox_SeedsNothing(string? input)
    {
        var (folder, file) = MainForm.SeedFromPath(input);
        Assert.Null(folder);
        Assert.Null(file);
    }

    [Fact]
    public void MalformedPath_SeedsNothing()
    {
        // An embedded null is the one thing Path.GetFullPath still rejects
        // outright; the helper must swallow it rather than throw at click time.
        var (folder, file) = MainForm.SeedFromPath("C:\\temp\\bad\0name.txt");
        Assert.Null(folder);
        Assert.Null(file);
    }

    [Fact]
    public void ExistingFolder_SeedsThatFolderAndNoName()
    {
        var dir = CreateTempDir();
        try
        {
            var (folder, file) = MainForm.SeedFromPath(dir);
            Assert.Equal(dir, folder, ignoreCase: true);
            Assert.Null(file);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ExistingFolderWithTrailingSeparator_SeedsThatFolderWithoutIt()
    {
        var dir = CreateTempDir();
        try
        {
            // The separator has to be stripped, or FolderBrowserDialog reads
            // the empty last segment and opens the parent instead.
            var (folder, file) = MainForm.SeedFromPath(dir + Path.DirectorySeparatorChar);
            Assert.Equal(dir, folder, ignoreCase: true);
            Assert.Null(file);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ExistingFile_SeedsItsFolderAndItsName()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "payload.exe");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            var (folder, file) = MainForm.SeedFromPath(path);
            Assert.Equal(dir, folder, ignoreCase: true);
            Assert.Equal("payload.exe", file);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void QuotedPath_IsUnquotedFirst()
    {
        // Explorer's "Copy as path" wraps the path in double quotes.
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "payload.exe");
            File.WriteAllBytes(path, new byte[] { 1 });

            var (folder, file) = MainForm.SeedFromPath($"\"{path}\"");
            Assert.Equal(dir, folder, ignoreCase: true);
            Assert.Equal("payload.exe", file);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MissingFileInExistingFolder_KeepsTheFolderAndTheName()
    {
        // The CSV box's normal state: the export does not exist yet.
        var dir = CreateTempDir();
        try
        {
            var (folder, file) = MainForm.SeedFromPath(Path.Combine(dir, "results.csv"));
            Assert.Equal(dir, folder, ignoreCase: true);
            Assert.Equal("results.csv", file);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MissingFolders_FallBackToNearestSurvivingAncestor()
    {
        var dir = CreateTempDir();
        try
        {
            var stale = Path.Combine(dir, "gone", "deeper", "results.csv");

            var (folder, file) = MainForm.SeedFromPath(stale);
            Assert.Equal(dir, folder, ignoreCase: true);
            // The name belonged inside a folder that is no longer there.
            Assert.Null(file);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MissingChildFolder_OpensItsParentWithTheNameKept()
    {
        var dir = CreateTempDir();
        var child = Path.Combine(dir, "scanned");
        try
        {
            var (folder, file) = MainForm.SeedFromPath(child);
            Assert.Equal(dir, folder, ignoreCase: true);
            Assert.Equal("scanned", file);   // the parent is there, so the leaf survives
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fh-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        // Normalize: GetTempPath can hand back an 8.3 short path on some boxes,
        // while SeedFromPath returns whatever GetFullPath produced.
        return Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
    }
}
