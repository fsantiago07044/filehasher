using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Basic interaction tests — each test method gets its own app instance so
/// there is no state leakage between tests.
/// </summary>
[Collection("Serial")]
public sealed class MainFormInteractionTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormInteractionTests() => _fixture = new AppFixture();
    public void Dispose()              => _fixture.Dispose();

    [Fact]
    public void RunWithNoPath_ShowsWarningDialog()
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();
        var dialog = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(5));
        Assert.NotNull(dialog);
        TestHelpers.DismissFirstButton(dialog);
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

        chk.Toggle();
        Assert.True(chk.IsChecked);
        Assert.True(ext.IsEnabled);

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
        Win.FindFirstDescendant(cf => cf.ByAutomationId("ClearBtn")).AsButton().Click();
        Assert.Equal("Ready.", TestHelpers.GetStatusText(Win));
    }

    [Fact]
    public void HashSingleFile_AppearsInResultsAndCompletionDialogShows()
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "filehasher automated test");
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = tmp;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            var dialog = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(15));
            Assert.NotNull(dialog);
            TestHelpers.DismissFirstButton(dialog);

            var listEl = Win.FindFirstDescendant(cf => cf.ByAutomationId("ResultsView"));
            var items  = listEl.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            Assert.True(items.Length >= 1, $"Expected at least one result row, got {items.Length}.");
        }
        finally { File.Delete(tmp); }
    }
}
