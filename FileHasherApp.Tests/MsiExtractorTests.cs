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

    // ── Tier 3: end-to-end adversarial MSI synthesis ──
    //
    // These tests use MaliciousMsiBuilder to fabricate genuinely-adversarial
    // MSIs at runtime by copying the benign template fixture into a temp file
    // and mutating specific table rows in place. The mutation goes through
    // WiX DTF's Direct-mode database API and writes valid bytes — the MSI
    // remains structurally well-formed and parseable, only its content is
    // hostile.

    [Fact]
    public async Task Adversarial_PathTraversalInFileName_DoesNotEscapeSandbox()
    {
        AssertFixturePresent();
        using var malicious = new MaliciousMsiBuilder(FixtureMsiPath);
        malicious.SetFirstFileName(@"..\..\evil.exe");

        using var extractor = new MsiExtractor();
        var safeFiles = await extractor.ExtractAsync(malicious.Path, CancellationToken.None);

        // Every entry the extractor returns must canonically live under the
        // extract directory. If WiX DTF actually wrote the file outside the
        // sandbox, our Phase 4 IsPathOutsideDirectory guard would have deleted
        // it and excluded it from this list. Either way: nothing in the
        // returned set is unsafe.
        var canonicalExtractDir = Path.GetFullPath(extractor.ExtractDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var f in safeFiles)
        {
            Assert.StartsWith(canonicalExtractDir, f, StringComparison.OrdinalIgnoreCase);
        }

        // And confirm directly on disk that the malicious filename did not
        // surface in the extract dir's parent (the obvious escape target for
        // "..\..\evil.exe"). %TEMP% can contain stale "evil.exe" files from
        // earlier failed runs, so qualify by recency: anything created in the
        // last 30 seconds at that path is from THIS test.
        var parentDir = Path.GetDirectoryName(extractor.ExtractDirectory)!;
        var escapeCandidate = Path.Combine(parentDir, "evil.exe");
        if (File.Exists(escapeCandidate))
        {
            var age = DateTime.UtcNow - File.GetCreationTimeUtc(escapeCandidate);
            Assert.True(age > TimeSpan.FromSeconds(30),
                $"Path-traversal entry escaped to {escapeCandidate} (created {age.TotalSeconds:F1}s ago).");
        }
    }

    [Fact]
    public async Task Adversarial_PathTraversalInDirectoryDefaultDir_DoesNotEscapeSandbox()
    {
        AssertFixturePresent();
        using var malicious = new MaliciousMsiBuilder(FixtureMsiPath);
        malicious.InjectDirectoryTraversal();

        using var extractor = new MsiExtractor();
        IReadOnlyList<string> safeFiles;
        try
        {
            safeFiles = await extractor.ExtractAsync(malicious.Path, CancellationToken.None);
        }
        catch
        {
            // It is acceptable for WiX DTF to reject a malformed Directory
            // table outright — that's a different correct outcome (extraction
            // never produces anything to escape with). What matters is the
            // negative property below.
            safeFiles = Array.Empty<string>();
        }

        var canonicalExtractDir = Path.GetFullPath(extractor.ExtractDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var f in safeFiles)
        {
            Assert.StartsWith(canonicalExtractDir, f, StringComparison.OrdinalIgnoreCase);
        }

        // Walk the extract dir's parent and grandparent for anything containing
        // "evil_dir" in its path created in the last 30 seconds.
        var parentDir = Path.GetDirectoryName(extractor.ExtractDirectory)!;
        foreach (var probe in new[] { parentDir, Path.GetDirectoryName(parentDir) })
        {
            if (string.IsNullOrEmpty(probe) || !Directory.Exists(probe)) continue;
            var hits = Directory.GetDirectories(probe, "evil_dir", SearchOption.TopDirectoryOnly)
                .Where(d => DateTime.UtcNow - Directory.GetCreationTimeUtc(d) <= TimeSpan.FromSeconds(30))
                .ToList();
            Assert.Empty(hits);
        }
    }

    [Fact]
    public async Task Adversarial_OversizeDeclaredFileSize_TripsPerFileCap()
    {
        AssertFixturePresent();
        using var malicious = new MaliciousMsiBuilder(FixtureMsiPath);
        // 3 GB > the 2 GB per-file cap default. The cap check reads only
        // the declared FileSize column, so it doesn't matter that the cabinet
        // doesn't actually contain 3 GB of data.
        malicious.SetFirstFileSize((long)int.MaxValue);   // ~2 GB, just above the 2 GB cap

        using var extractor = new MsiExtractor();
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => extractor.ExtractAsync(malicious.Path, CancellationToken.None));
        Assert.Contains("per-file cap", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(extractor.ExtractDirectory),
            "Cap rejection must occur before the temp dir is created.");
    }

    [Fact]
    public async Task Adversarial_TooManyFiles_TripsFileCountCap()
    {
        AssertFixturePresent();
        using var malicious = new MaliciousMsiBuilder(FixtureMsiPath);
        // Default file-count cap is 10_000. Template has 9 rows; insert
        // enough synthetic rows to put us above 10_000. The cap check reads
        // only the count, so the synthetic rows don't need real cabinet
        // backing.
        malicious.InsertSyntheticFileRows(10_000);

        using var extractor = new MsiExtractor();
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => extractor.ExtractAsync(malicious.Path, CancellationToken.None));
        Assert.Contains("cap of 10,000", ex.Message);
        Assert.False(Directory.Exists(extractor.ExtractDirectory));
    }

    // ── helpers ──

    private static void AssertFixturePresent()
    {
        Assert.True(File.Exists(FixtureMsiPath),
            $"Test fixture not found at '{FixtureMsiPath}'. Confirm FileHasherApp.Tests.csproj " +
            "ships fixtures/ via <Content Include=\"fixtures\\**\\*\" CopyToOutputDirectory=...>.");
    }
}
