using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Direct unit tests for <see cref="SettingsStore"/> and <see cref="AppSettings"/>.
/// No UI, no desktop session: these run anywhere the test project does.
///
/// The emphasis is on the failure paths rather than the round trip, because the
/// settings file lives in the user's profile as plain JSON, which means it can
/// be hand-edited, truncated by a crash, or written by a newer build. None of
/// those may stop the app from starting.
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public SettingsStoreTests()
    {
        _dir  = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = SettingsStore.Load(Path.Combine(_dir, "nope.json"));

        Assert.Equal("SHA256", s.Algorithm);
        Assert.Equal(0, s.DepthMode);
        Assert.Equal(1, s.DepthLevels);
        Assert.Equal(".sha256", s.SidecarExtension);
        Assert.Equal("sha256sum", s.SidecarFormat);
        Assert.False(s.IncludeMetadata);
        Assert.False(s.AllFileTypes);
        Assert.False(s.DescendIntoMsi);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var original = new AppSettings
        {
            Algorithm        = "SHA512",
            IncludeMetadata  = true,
            AllFileTypes     = true,
            DescendIntoMsi   = true,
            DepthMode        = 2,
            DepthLevels      = 7,
            SidecarExtension = ".sha512",
            SidecarFormat    = "extended"
        };

        Assert.True(SettingsStore.Save(original, _file));
        var loaded = SettingsStore.Load(_file);

        Assert.Equal(original.Algorithm,        loaded.Algorithm);
        Assert.Equal(original.IncludeMetadata,  loaded.IncludeMetadata);
        Assert.Equal(original.AllFileTypes,     loaded.AllFileTypes);
        Assert.Equal(original.DescendIntoMsi,   loaded.DescendIntoMsi);
        Assert.Equal(original.DepthMode,        loaded.DepthMode);
        Assert.Equal(original.DepthLevels,      loaded.DepthLevels);
        Assert.Equal(original.SidecarExtension, loaded.SidecarExtension);
        Assert.Equal(original.SidecarFormat,    loaded.SidecarFormat);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    public void Load_CorruptFile_ReturnsDefaultsWithoutThrowing(string content)
    {
        File.WriteAllText(_file, content);

        var s = SettingsStore.Load(_file);

        Assert.Equal("SHA256", s.Algorithm);
        Assert.Equal(AppSettings.CurrentSchemaVersion, s.SchemaVersion);
    }

    [Fact]
    public void Load_FileFromNewerSchema_FallsBackToDefaults()
    {
        // A newer build may give an existing field a new meaning, so the safe
        // response is defaults rather than a confident misreading.
        File.WriteAllText(_file,
            $$"""{"SchemaVersion":{{AppSettings.CurrentSchemaVersion + 1}},"Algorithm":"MD5","DepthMode":2}""");

        var s = SettingsStore.Load(_file);

        Assert.Equal("SHA256", s.Algorithm);
        Assert.Equal(0, s.DepthMode);
    }

    [Theory]
    [InlineData("\"Algorithm\":\"ROT13\"", "SHA256")]
    [InlineData("\"Algorithm\":\"\"",      "SHA256")]
    [InlineData("\"Algorithm\":\"SHA1\"",  "SHA1")]
    public void Load_UnknownAlgorithm_FallsBackButKeepsValidOnes(string fragment, string expected)
    {
        File.WriteAllText(_file, "{" + fragment + "}");

        Assert.Equal(expected, SettingsStore.Load(_file).Algorithm);
    }

    [Theory]
    [InlineData(-5,  0)]
    [InlineData(3,   0)]
    [InlineData(99,  0)]
    [InlineData(2,   2)]
    public void Load_DepthModeOutOfRange_FallsBackToUnlimited(int stored, int expected)
    {
        File.WriteAllText(_file, $$"""{"DepthMode":{{stored}}}""");

        Assert.Equal(expected, SettingsStore.Load(_file).DepthMode);
    }

    [Theory]
    [InlineData(0,    1)]
    [InlineData(-3,   1)]
    [InlineData(1000, 64)]
    [InlineData(64,   64)]
    public void Load_DepthLevelsOutOfRange_IsClampedToTheSpinnerRange(int stored, int expected)
    {
        File.WriteAllText(_file, $$"""{"DepthLevels":{{stored}}}""");

        Assert.Equal(expected, SettingsStore.Load(_file).DepthLevels);
    }

    [Fact]
    public void Load_BlankSidecarExtensionOrUnknownFormat_FallsBack()
    {
        File.WriteAllText(_file, """{"SidecarExtension":"   ","SidecarFormat":"yaml"}""");

        var s = SettingsStore.Load(_file);

        Assert.Equal(".sha256", s.SidecarExtension);
        Assert.Equal("sha256sum", s.SidecarFormat);
    }

    [Fact]
    public void Save_CreatesMissingDirectories()
    {
        var nested = Path.Combine(_dir, "a", "b", "settings.json");

        Assert.True(SettingsStore.Save(new AppSettings { Algorithm = "MD5" }, nested));
        Assert.True(File.Exists(nested));
        Assert.Equal("MD5", SettingsStore.Load(nested).Algorithm);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        Assert.True(SettingsStore.Save(new AppSettings(), _file));

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Save_OverwritesAnExistingFileInPlace()
    {
        Assert.True(SettingsStore.Save(new AppSettings { Algorithm = "MD5" }, _file));
        Assert.True(SettingsStore.Save(new AppSettings { Algorithm = "SHA1" }, _file));

        Assert.Equal("SHA1", SettingsStore.Load(_file).Algorithm);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Save_UnwritableTarget_ReturnsFalseWithoutThrowing()
    {
        // The directory itself is not a writable file path.
        Assert.False(SettingsStore.Save(new AppSettings(), _dir));
    }

    [Fact]
    public void Save_StampsTheCurrentSchemaVersion()
    {
        Assert.True(SettingsStore.Save(new AppSettings { SchemaVersion = 0 }, _file));

        Assert.Contains($"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}", File.ReadAllText(_file));
    }
}
