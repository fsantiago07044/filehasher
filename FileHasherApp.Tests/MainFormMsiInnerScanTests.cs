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
