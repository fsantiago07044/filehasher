using FlaUI.Core.AutomationElements;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Read-only assertions about the form's initial/default state.
/// All tests share one app instance via IClassFixture — none of them modify UI state.
/// </summary>
[Collection("Serial")]
public sealed class MainFormStateTests : IClassFixture<AppFixture>
{
    private readonly Window _win;

    public MainFormStateTests(AppFixture fixture)
    {
        _win = fixture.MainWindow;
    }

    [Fact]
    public void Title_StartsWithFileHasher()
    {
        Assert.StartsWith("FileHasher", _win.Title);
    }

    [Fact]
    public void DefaultAlgorithm_IsSha256()
    {
        Assert.True(_win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoSha256")).AsRadioButton().IsChecked);
    }

    [Fact]
    public void OtherAlgorithms_NotSelectedByDefault()
    {
        Assert.False(_win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoMd5")).AsRadioButton().IsChecked);
        Assert.False(_win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoSha1")).AsRadioButton().IsChecked);
        Assert.False(_win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoSha512")).AsRadioButton().IsChecked);
    }

    [Fact]
    public void RunButton_EnabledAtStart()
    {
        Assert.True(_win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().IsEnabled);
    }

    [Fact]
    public void StopButton_DisabledAtStart()
    {
        Assert.False(_win.FindFirstDescendant(cf => cf.ByAutomationId("StopBtn")).AsButton().IsEnabled);
    }

    [Fact]
    public void StatusLabel_ShowsReadyAtStart()
    {
        // WinForms Label.Text surfaces as the UIAutomation element Name (accessible name).
        var lbl = _win.FindFirstDescendant(cf => cf.ByAutomationId("StatusLabel"));
        Assert.Equal("Ready.", lbl.Name);
    }

    [Fact]
    public void PathBox_EmptyAtStart()
    {
        var tb = _win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox();
        Assert.Equal(string.Empty, tb.Text);
    }

    [Fact]
    public void SidecarCheckbox_UncheckedByDefault_AndOptionsDisabled()
    {
        var chk = _win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarChk")).AsCheckBox();
        var ext = _win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarExtBox")).AsTextBox();

        Assert.False(chk.IsChecked);
        Assert.False(ext.IsEnabled);
    }

    [Fact]
    public void CsvCheckbox_UncheckedByDefault_AndOptionsDisabled()
    {
        var chk  = _win.FindFirstDescendant(cf => cf.ByAutomationId("CsvChk")).AsCheckBox();
        var path = _win.FindFirstDescendant(cf => cf.ByAutomationId("CsvPathBox")).AsTextBox();

        Assert.False(chk.IsChecked);
        Assert.False(path.IsEnabled);
    }

    [Fact]
    public void SidecarExtBox_DefaultExtension_IsSha256()
    {
        // Default value should be pre-populated regardless of enabled state.
        var ext = _win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarExtBox")).AsTextBox();
        Assert.Equal(".sha256", ext.Text);
    }
}
