using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Direct unit tests against the internal <see cref="MsiExtractor"/> class:
/// the security guards (per-file / total-size / file-count / free-disk caps,
/// path-traversal rejection, reparse-point rejection) and the extract-dir
/// lifecycle. Complementary to <c>MainFormMsiInnerScanTests</c>, which
/// exercises the same code path end-to-end through the WinForms UI.
///
/// These tests do NOT drive the UI and do NOT require an interactive desktop
/// session — they instantiate <see cref="MsiExtractor"/> directly via
/// <c>InternalsVisibleTo</c>. They're fast (no per-test process launch) and
/// don't share state, so xUnit can parallelize them across the standard test
/// runner.
/// </summary>
public sealed class MsiExtractorTests
{
    private static readonly string FixtureMsiPath =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "msi-test.msi");

    // ── Tier 1: cap enforcement (driven via constructor params, not a malicious MSI) ──

    [Fact]
    public async Task Cap_PerFileBytes_Tiny_AbortsBeforeExtraction()
    {
        AssertFixturePresent();
        using var extractor = new MsiExtractor(maxPerFileBytes: 1);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => extractor.ExtractAsync(FixtureMsiPath, CancellationToken.None));

        Assert.Contains("per-file cap", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Cap rejection happens before Phase 3 (Directory.CreateDirectory),
        // so the temp dir must not exist after the throw.
        Assert.False(Directory.Exists(extractor.ExtractDirectory),
            "Per-file cap rejection should occur before the temp dir is created.");
    }

    [Fact]
    public async Task Cap_TotalBytes_Tiny_AbortsBeforeExtraction()
    {
        AssertFixturePresent();
        using var extractor = new MsiExtractor(maxTotalBytes: 1);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => extractor.ExtractAsync(FixtureMsiPath, CancellationToken.None));

        Assert.Contains("total", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(extractor.ExtractDirectory));
    }

    [Fact]
    public async Task Cap_FileCount_Zero_AbortsBeforeExtraction()
    {
        AssertFixturePresent();
        using var extractor = new MsiExtractor(maxFileCount: 0);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => extractor.ExtractAsync(FixtureMsiPath, CancellationToken.None));

        Assert.Contains("cap of 0", ex.Message);
        Assert.False(Directory.Exists(extractor.ExtractDirectory));
    }

    [Fact]
    public async Task Cap_MinFreeDisk_Absurd_AbortsBeforeExtraction()
    {
        AssertFixturePresent();
        using var extractor = new MsiExtractor(minFreeDiskBytes: long.MaxValue);

        var ex = await Assert.ThrowsAsync<IOException>(
            () => extractor.ExtractAsync(FixtureMsiPath, CancellationToken.None));

        Assert.Contains("Insufficient free disk", ex.Message);
        Assert.False(Directory.Exists(extractor.ExtractDirectory));
    }

    // ── Tier 2: path-traversal guard logic ──

    [Theory]
    [InlineData(@"C:\extract\foo.txt",           false)]  // inside, root
    [InlineData(@"C:\extract\sub\foo.txt",       false)]  // inside, nested
    [InlineData(@"C:\extract\a\b\c\foo.txt",     false)]  // inside, deeply nested
    [InlineData(@"C:\extract\sub\..\foo.txt",    false)]  // canonicalizes to C:\extract\foo.txt
    [InlineData(@"C:\evil.txt",                  true)]   // outside (unrelated parent)
    [InlineData(@"C:\Windows\System32\evil.dll", true)]   // outside (different absolute path)
    [InlineData(@"C:\extract2\foo.txt",          true)]   // outside (similar-prefix sibling)
    [InlineData(@"C:\extract\..\evil.txt",       true)]   // canonicalizes to C:\evil.txt
    [InlineData(@"C:\extract\sub\..\..\evil",    true)]   // two-up canonicalizes to C:\evil
    public void IsPathOutsideDirectory_Cases(string filePath, bool expectedOutside)
    {
        // Mirror what ExtractAsync constructs: canonical directory with trailing separator.
        var canonicalDir = @"C:\extract" + Path.DirectorySeparatorChar;
        Assert.Equal(expectedOutside, MsiExtractor.IsPathOutsideDirectory(filePath, canonicalDir));
    }

    [Fact]
    public void IsPathOutsideDirectory_TrailingSeparatorRequired_DistinguishesPrefixSiblings()
    {
        // This documents the calling convention: the second arg MUST carry the
        // trailing separator, otherwise C:\extract2\foo.txt would falsely match
        // C:\extract because the path string starts with the literal prefix.
        // The production code always passes the slash-terminated form; these
        // two assertions show both halves of the contract.
        var withSlash    = @"C:\extract" + Path.DirectorySeparatorChar;
        var withoutSlash = @"C:\extract";

        Assert.True(MsiExtractor.IsPathOutsideDirectory(@"C:\extract2\foo.txt", withSlash));
        // The unsafe form would falsely accept the sibling — captured here as
        // a regression marker, not as endorsement of calling it this way:
        Assert.False(MsiExtractor.IsPathOutsideDirectory(@"C:\extract2\foo.txt", withoutSlash));
    }

    // ── Tier 2: reparse-point guard logic ──

    [Fact]
    public void IsReparsePoint_RegularFile_ReturnsFalse()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            Assert.False(MsiExtractor.IsReparsePoint(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void IsReparsePoint_Symlink_ReturnsTrue()
    {
        // Creating a symlink on Windows requires either SeCreateSymbolicLinkPrivilege
        // (which usually means running elevated) or Developer Mode being enabled.
        // If the test environment grants neither, File.CreateSymbolicLink throws
        // UnauthorizedAccessException or IOException — there's no way to fabricate
        // a reparse point without one of those, so we skip-by-early-return rather
        // than fail. The CI Windows runner is configured with the necessary
        // privilege; a developer box may not be, in which case this test passes
        // vacuously and the reparse-point path is only covered by IsReparsePoint_
        // RegularFile_ReturnsFalse above (which confirms the NEGATIVE case).
        var target   = Path.GetTempFileName();
        var linkPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var symlinkCreated = false;
        try
        {
            try
            {
                File.CreateSymbolicLink(linkPath, target);
                symlinkCreated = true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Console.Error.WriteLine(
                    "[MsiExtractorTests] IsReparsePoint_Symlink_ReturnsTrue: skipping — " +
                    $"symlink creation not permitted in this test environment ({ex.GetType().Name}: {ex.Message}). " +
                    "Run as administrator or enable Developer Mode to exercise this assertion.");
                return;
            }

            Assert.True(MsiExtractor.IsReparsePoint(linkPath),
                "Symlinks must be detected as reparse points so the extraction guard rejects them.");
        }
        finally
        {
            if (symlinkCreated && File.Exists(linkPath)) File.Delete(linkPath);
            if (File.Exists(target)) File.Delete(target);
        }
    }

    // ── ResolveDisplayPath: well-known MSI Directory-table identifier mapping ──

    [Theory]
    // PFiles64 / ProgramFiles64Folder both map to "Program Files" on a 64-bit Windows install.
    [InlineData(@"PFiles64\foo.exe",                    @"Program Files\foo.exe",       "PFiles64")]
    [InlineData(@"ProgramFiles64Folder\sub\foo.exe",    @"Program Files\sub\foo.exe",   "ProgramFiles64Folder")]
    // PFiles / ProgramFilesFolder map to the 32-bit "Program Files (x86)".
    [InlineData(@"PFiles\foo.exe",                      @"Program Files (x86)\foo.exe", "PFiles")]
    [InlineData(@"ProgramFilesFolder\app\foo.exe",      @"Program Files (x86)\app\foo.exe", "ProgramFilesFolder")]
    // Other common well-known shell folders.
    [InlineData(@"WindowsFolder\sysmon.exe",            @"Windows\sysmon.exe",          "WindowsFolder")]
    [InlineData(@"SystemFolder\cmd.exe",                @"Windows\System32\cmd.exe",    "SystemFolder")]
    [InlineData(@"FontsFolder\arial.ttf",               @"Windows\Fonts\arial.ttf",     "FontsFolder")]
    [InlineData(@"CommonFiles64Folder\shared.dll",      @"Program Files\Common Files\shared.dll", "CommonFiles64Folder")]
    // TARGETDIR maps to empty string — the identifier is dropped from the displayed path entirely.
    [InlineData(@"TARGETDIR\foo.exe",                   @"foo.exe",                     "TARGETDIR")]
    // Unknown identifier (an MSI author's custom name like INSTALLDIR): path is preserved verbatim,
    // identifier is still surfaced for audit display.
    [InlineData(@"INSTALLDIR\sub\foo.exe",              @"INSTALLDIR\sub\foo.exe",      "INSTALLDIR")]
    [InlineData(@"MyCustomFolder\bar.exe",              @"MyCustomFolder\bar.exe",      "MyCustomFolder")]
    // Edge cases: bare filename and empty input — no identifier to surface.
    [InlineData(@"bare-file.exe",                       @"bare-file.exe",               null)]
    [InlineData(@"",                                    @"",                            null)]
    public void ResolveDisplayPath_Cases(string input, string expectedDisplay, string? expectedId)
    {
        var (display, id) = MsiExtractor.ResolveDisplayPath(input);
        Assert.Equal(expectedDisplay, display);
        Assert.Equal(expectedId, id);
    }

    // ── Lifecycle / sanity ──

    [Fact]
    public async Task Dispose_RemovesExtractDirectory_AfterSuccessfulExtraction()
    {
        AssertFixturePresent();
        string extractDir;
        using (var extractor = new MsiExtractor())
        {
            extractDir = extractor.ExtractDirectory;
            await extractor.ExtractAsync(FixtureMsiPath, CancellationToken.None);
            Assert.True(Directory.Exists(extractDir),
                "Extract dir should exist while the extractor is still in scope.");
        }
        Assert.False(Directory.Exists(extractDir),
            "Extract dir must be removed when the extractor is disposed.");
    }

    [Fact]
    public async Task ExtractAsync_NonExistentMsi_ThrowsFileNotFound()
    {
        var bogus = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".msi");
        Assert.False(File.Exists(bogus));

        using var extractor = new MsiExtractor();
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => extractor.ExtractAsync(bogus, CancellationToken.None));
    }

    // ── helpers ──

    private static void AssertFixturePresent()
    {
        Assert.True(File.Exists(FixtureMsiPath),
            $"Test fixture not found at '{FixtureMsiPath}'. Confirm FileHasherApp.Tests.csproj " +
            "ships fixtures/ via <Content Include=\"fixtures\\**\\*\" CopyToOutputDirectory=...>.");
    }
}
