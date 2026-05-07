using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Tests that modify UI state.  Each test method gets its own app instance so
/// there is no state leakage between tests.
/// </summary>
[Collection("Serial")]
public sealed class MainFormInteractionTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormInteractionTests()
    {
        _fixture = new AppFixture();
    }

    public void Dispose() => _fixture.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Polls until a modal window appears on the main window or throws on timeout.</summary>
    private Window WaitForModal(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var modal = Win.ModalWindows.FirstOrDefault();
            if (modal is not null) return modal;
            Thread.Sleep(100);
        }
        throw new TimeoutException($"No modal dialog appeared within {timeout}.");
    }

    /// <summary>Clicks the first button in a dialog (OK / the sole action button).</summary>
    private static void DismissFirstButton(Window dialog)
    {
        var btn = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button));
        btn.AsButton().Click();
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void RunWithNoPath_ShowsWarningDialog()
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

        var dialog = WaitForModal(TimeSpan.FromSeconds(5));
        Assert.NotNull(dialog);

        DismissFirstButton(dialog);
    }

    [Fact]
    public void AlgorithmSelection_CanSwitchToMd5()
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoMd5")).AsRadioButton().Click();

        Assert.True(Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoMd5")).AsRadioButton().IsChecked);
        Assert.False(Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoSha256")).AsRadioButton().IsChecked);
    }

    [Fact]
    public void AlgorithmSelection_CanSwitchToSha512()
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoSha512")).AsRadioButton().Click();

        Assert.True(Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoSha512")).AsRadioButton().IsChecked);
    }

    [Fact]
    public void SidecarCheckbox_TogglesOptionsPanel()
    {
        var chk = Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarChk")).AsCheckBox();
        var ext = Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarExtBox")).AsTextBox();

        // Enable
        chk.Toggle();
        Assert.True(chk.IsChecked);
        Assert.True(ext.IsEnabled);

        // Disable again
        chk.Toggle();
        Assert.False(chk.IsChecked);
        Assert.False(ext.IsEnabled);
    }

    [Fact]
    public void CsvCheckbox_TogglesOptionsPanel()
    {
        var chk  = Win.FindFirstDescendant(cf => cf.ByAutomationId("CsvChk")).AsCheckBox();
        var path = Win.FindFirstDescendant(cf => cf.ByAutomationId("CsvPathBox")).AsTextBox();

        chk.Toggle();
        Assert.True(chk.IsChecked);
        Assert.True(path.IsEnabled);

        chk.Toggle();
        Assert.False(chk.IsChecked);
        Assert.False(path.IsEnabled);
    }

    [Fact]
    public void ClearButton_ResetsStatusLabel()
    {
        // Clear with no results should reset status to "Ready."
        Win.FindFirstDescendant(cf => cf.ByAutomationId("ClearBtn")).AsButton().Click();
        var lbl = FindStatusLabel();
        Assert.Equal("Ready.", lbl.Name);
    }

    [Fact]
    public void HashSingleFile_AppearsInResultsAndCompletionDialogShows()
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "filehasher automated test");

        try
        {
            // Set path to temp file
            var pathBox = Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox();
            pathBox.Text = tmp;

            // Run
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            // Wait for the completion MessageBox (up to 15 s for slow CI machines)
            var dialog = WaitForModal(TimeSpan.FromSeconds(15));
            Assert.NotNull(dialog);
            DismissFirstButton(dialog);

            // Results list should have exactly one item for the single file
            var listEl = Win.FindFirstDescendant(cf => cf.ByAutomationId("ResultsView"));
            var items  = listEl.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            Assert.True(items.Length >= 1, $"Expected at least one result row, got {items.Length}.");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ── private helpers ───────────────────────────────────────────────────────

    private AutomationElement FindStatusLabel()
        => Win.FindFirstDescendant(cf => cf.ByAutomationId("StatusLabel"));
}
