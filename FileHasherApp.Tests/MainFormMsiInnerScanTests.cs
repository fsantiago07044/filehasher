using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// MSI inner-file scan (EXPERIMENTAL — feature/msi-inner-scan branch). Verifies
/// the new "Hash files inside MSI installers" option's default state, its on/off
/// effect on the results count, the [parent.msi] prefix on inner-file rows, the
/// dynamic enable state of the AllTypes checkbox when an MSI is the target, the
/// CSV's new Container / MsiDirectoryId columns, and that the per-extraction
/// temp directory under %TEMP% is cleaned up after a run.
///
/// Test fixture: FileHasherApp.Tests/fixtures/msi-test.msi (~7 MB). At least one
/// inner file is required for the on-state tests to be meaningful; the fixture's
/// exact contents are intentionally not asserted on so the suite stays robust to
/// fixture regenerations as long as the new MSI is non-empty.
///
/// NOT yet covered (need additional malicious fixtures, deferred):
///   • Per-file / total / count cap enforcement (over-cap MSI should surface a
///     [WARN] row without aborting the pipeline).
///   • Path-traversal entry rejection (no file lands outside the extract dir).
///   • Reparse-point rejection (symlink / junction entries excluded).
/// </summary>
[Collection("Serial")]
public sealed class MainFormMsiInnerScanTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    private static readonly string FixtureMsiPath =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "msi-test.msi");

    public MainFormMsiInnerScanTests() => _fixture = new AppFixture();
    public void Dispose()              => _fixture.Dispose();

    // ── Default state ────────────────────────────────────────────────────────

    [Fact]
    public void MsiChk_UncheckedByDefault()
    {
        var msiChk = Win.FindFirstDescendant(cf => cf.ByAutomationId("MsiChk")).AsCheckBox();
        Assert.NotNull(msiChk);
        Assert.True(msiChk.IsChecked != true,
            "MSI inner-scan checkbox should be unchecked at app launch (experimental opt-in).");
    }

    // ── Off-state ────────────────────────────────────────────────────────────

    [Fact]
    public void MsiInnerScan_OffByDefault_HashesMsiAsSingleFile()
    {
        AssertFixturePresent();
        TestHelpers.RunHashOnFile(Win, FixtureMsiPath);
        Assert.Equal(1, TestHelpers.GetResultsRowCount(Win));
    }

    // ── On-state ─────────────────────────────────────────────────────────────

    [Fact]
    public void MsiInnerScan_OnProducesInnerRows()
    {
        AssertFixturePresent();
        EnableMsiScan();
        TestHelpers.RunHashOnFile(Win, FixtureMsiPath);

        // 1 row for the MSI itself + N rows for the inner files; the fixture
        // is expected to contain at least one inner file.
        Assert.True(TestHelpers.GetResultsRowCount(Win) > 1,
            $"Expected the outer MSI row plus at least one inner-file row, got {TestHelpers.GetResultsRowCount(Win)}.");
    }

    [Fact]
    public void MsiInnerScan_OnInnerRowsArePrefixedWithContainerName()
    {
        AssertFixturePresent();
        EnableMsiScan();
        TestHelpers.RunHashOnFile(Win, FixtureMsiPath);

        var items = GetRowItems();
        var prefixed = items.Count(item =>
            (item.Name ?? string.Empty).StartsWith("[msi-test.msi]", StringComparison.OrdinalIgnoreCase));

        Assert.True(prefixed > 0,
            "Expected at least one inner-file row prefixed with '[msi-test.msi]'. " +
            $"Saw rows: [{string.Join(", ", items.Select(i => i.Name))}]");
    }

    // ── AllTypes enable-state interaction ────────────────────────────────────

    [Fact]
    public void AllTypesChk_EnabledWhenMsiFileSelectedAndDescendOn()
    {
        AssertFixturePresent();
        Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = FixtureMsiPath;
        EnableMsiScan();

        var allTypes = Win.FindFirstDescendant(cf => cf.ByAutomationId("AllTypesChk")).AsCheckBox();
        Assert.True(TestHelpers.WaitUntilEnabled(allTypes, TimeSpan.FromSeconds(2)),
            "AllTypes checkbox should be enabled when an MSI is selected AND MSI inner-scan is on.");
    }

    [Fact]
    public void AllTypesChk_DisabledWhenMsiFileSelectedAndDescendOff()
    {
        AssertFixturePresent();
        Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = FixtureMsiPath;
        // MsiChk left at its unchecked default.

        var allTypes = Win.FindFirstDescendant(cf => cf.ByAutomationId("AllTypesChk")).AsCheckBox();
        Assert.True(TestHelpers.WaitUntilDisabled(allTypes, TimeSpan.FromSeconds(2)),
            "AllTypes checkbox should be disabled when an MSI is selected without MSI inner-scan on.");
    }

    // ── Temp dir cleanup ─────────────────────────────────────────────────────

    [Fact]
    public void MsiInnerScan_TempDirCleanedUpAfterRun()
    {
        AssertFixturePresent();

        var tempDir = Path.GetTempPath();
        const string pattern = "FileHasher_msi_*";

        // Snapshot any pre-existing matches so we only assert on dirs this run created.
        var preRun = Directory.GetDirectories(tempDir, pattern).ToHashSet(StringComparer.OrdinalIgnoreCase);

        EnableMsiScan();
        TestHelpers.RunHashOnFile(Win, FixtureMsiPath);

        // The Dispose-time cleanup runs synchronously inside HashMsiInnerFilesAsync's
        // `using` block, but we give the FS a brief moment in case any deferred handles
        // are still releasing.
        Thread.Sleep(500);

        var postRun = Directory.GetDirectories(tempDir, pattern).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var leaked  = postRun.Except(preRun, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Empty(leaked);
    }

    // ── CSV export schema ────────────────────────────────────────────────────

    [Fact]
    public void MsiInnerScan_CsvHasContainerAndMsiDirectoryIdColumns()
    {
        AssertFixturePresent();

        var csv = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = FixtureMsiPath;
            EnableMsiScan();

            var csvChk = Win.FindFirstDescendant(cf => cf.ByAutomationId("CsvChk")).AsCheckBox();
            if (csvChk.IsChecked != true) csvChk.Toggle();
            Win.FindFirstDescendant(cf => cf.ByAutomationId("CsvPathBox")).AsTextBox().Text = csv;

            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();
            TestHelpers.DismissFirstButton(TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(60)));

            Assert.True(File.Exists(csv), "CSV file was not created.");
            var lines = File.ReadAllLines(csv);
            Assert.True(lines.Length > 1, "Expected a header row plus at least one data row.");

            var header = lines[0];
            Assert.Contains("Container",      header, StringComparison.Ordinal);
            Assert.Contains("MsiDirectoryId", header, StringComparison.Ordinal);

            // At least one data row should carry msi-test.msi in its Container cell —
            // that's the signal that inner-file rows are being emitted with the new
            // field populated.
            var anyContainerRow = lines.Skip(1).Any(line =>
                line.Contains("msi-test.msi", StringComparison.OrdinalIgnoreCase));
            Assert.True(anyContainerRow,
                "Expected at least one CSV data row to reference 'msi-test.msi' in its Container column.");
        }
        finally
        {
            if (File.Exists(csv)) File.Delete(csv);
        }
    }

    // ── AllTypes interaction with inner-file filtering ───────────────────────
    //
    // Fixture contents (msi-test.msi): 6 .exe files + 2 .jpg + 1 .txt = 9 inner files.
    // AllTypes off keeps the default .exe/.msi-only filter, so only the 6 .exe rows
    // appear; AllTypes on lifts the filter and all 9 inner files surface.

    [Fact]
    public void MsiInnerScan_AllTypesOff_OnlyHashesExeAndMsiInnerFiles()
    {
        AssertFixturePresent();
        EnableMsiScan();
        // Leave AllTypes off (its default state).
        TestHelpers.RunHashOnFile(Win, FixtureMsiPath);

        // 1 outer MSI row + 6 inner .exe rows = 7 total.
        Assert.Equal(7, TestHelpers.GetResultsRowCount(Win));
    }

    [Fact]
    public void MsiInnerScan_AllTypesOn_HashesEveryInnerFile()
    {
        AssertFixturePresent();
        EnableMsiScan();

        // Set the path first (UpdateAllTypesEnabled needs to see an .msi file
        // selected with MsiChk on before AllTypesChk becomes enable-able).
        Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = FixtureMsiPath;
        var allTypes = Win.FindFirstDescendant(cf => cf.ByAutomationId("AllTypesChk")).AsCheckBox();
        Assert.True(TestHelpers.WaitUntilEnabled(allTypes, TimeSpan.FromSeconds(2)),
            "AllTypes checkbox should be enabled after setting an .msi path with MsiScan on.");
        allTypes.Toggle();

        Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();
        TestHelpers.DismissFirstButton(TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(30)));

        // 1 outer MSI row + 9 inner rows (6 exe + 2 jpg + 1 txt) = 10 total.
        Assert.Equal(10, TestHelpers.GetResultsRowCount(Win));
    }

    // ── Sidecar suppression for inner files ──────────────────────────────────

    [Fact]
    public void MsiInnerScan_WithSidecarsOn_OnlyWritesSidecarForOuterMsi()
    {
        AssertFixturePresent();

        // Copy the fixture to a fresh, writable temp directory so the test doesn't
        // pollute the test-bin fixtures/ folder with .sha256 files across runs.
        var workDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(workDir);
        var msiCopy = Path.Combine(workDir, "msi-test.msi");
        File.Copy(FixtureMsiPath, msiCopy);
        var expectedSidecar = msiCopy + ".sha256";

        try
        {
            EnableMsiScan();
            // Turn on sidecar writes (uses default .sha256 extension, sha256sum format).
            var sidecarChk = Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarChk")).AsCheckBox();
            if (sidecarChk.IsChecked != true) sidecarChk.Toggle();

            TestHelpers.RunHashOnFile(Win, msiCopy);

            // The outer MSI must have its sidecar written next to it on disk.
            Assert.True(File.Exists(expectedSidecar),
                $"Expected outer-MSI sidecar at '{expectedSidecar}'.");

            // No phantom sidecars anywhere in the work dir — inner files live
            // in a now-deleted temp dir, so nothing else should exist here.
            var allSidecars = Directory.GetFiles(workDir, "*.sha256", SearchOption.AllDirectories);
            Assert.Single(allSidecars);

            // And no leaked FileHasher_msi_* temp dir under %TEMP%.
            var leakedTempDirs = Directory.GetDirectories(Path.GetTempPath(), "FileHasher_msi_*")
                .Where(d => Directory.GetCreationTime(d) > DateTime.Now.AddMinutes(-5))
                .ToList();
            Assert.Empty(leakedTempDirs);
        }
        finally
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }
    }

    // ── Multi-MSI folder scan ────────────────────────────────────────────────

    [Fact]
    public void MsiInnerScan_FolderWithMultipleMsis_EachExtractsAndCleansSeparately()
    {
        AssertFixturePresent();

        // Build a temp folder containing two copies of the fixture MSI under
        // distinct names. This exercises the fan-out behavior: each MSI gets
        // its own MsiExtractor instance, its own randomly-named temp dir, and
        // its own cleanup on disposal.
        var workDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(workDir);
        File.Copy(FixtureMsiPath, Path.Combine(workDir, "msi-test-1.msi"));
        File.Copy(FixtureMsiPath, Path.Combine(workDir, "msi-test-2.msi"));

        try
        {
            EnableMsiScan();
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = workDir;
            // AllTypes stays off: folder enumeration picks .exe/.msi only (the only files in
            // this folder are 2 .msi anyway), and inner files filter to .exe/.msi only.

            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();
            TestHelpers.DismissFirstButton(TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(60)));

            // 2 outer MSI rows + 6 inner .exe rows per MSI × 2 = 14 total.
            Assert.Equal(14, TestHelpers.GetResultsRowCount(Win));

            // Both MSIs' temp dirs must be cleaned up — no FileHasher_msi_* dir
            // freshly created in the last few minutes should remain.
            var leakedTempDirs = Directory.GetDirectories(Path.GetTempPath(), "FileHasher_msi_*")
                .Where(d => Directory.GetCreationTime(d) > DateTime.Now.AddMinutes(-5))
                .ToList();
            Assert.Empty(leakedTempDirs);
        }
        finally
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void EnableMsiScan()
    {
        var chk = Win.FindFirstDescendant(cf => cf.ByAutomationId("MsiChk")).AsCheckBox();
        if (chk.IsChecked != true) chk.Toggle();
    }

    private AutomationElement[] GetRowItems()
    {
        var list = Win.FindFirstDescendant(cf => cf.ByAutomationId("ResultsView"));
        return list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
    }

    private static void AssertFixturePresent()
    {
        Assert.True(File.Exists(FixtureMsiPath),
            $"Test fixture not found at '{FixtureMsiPath}'. Confirm FileHasherApp.Tests.csproj " +
            "ships fixtures/ via <Content Include=\"fixtures\\**\\*\" CopyToOutputDirectory=...>.");
    }
}
