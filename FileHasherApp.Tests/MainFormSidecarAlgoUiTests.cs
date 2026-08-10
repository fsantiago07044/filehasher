using FlaUI.Core.AutomationElements;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// The sidecar suggested extension and the "{algo}sum format" radio label
/// follow the selected hash algorithm; a custom extension the user typed is
/// never clobbered. Each test gets its own app process.
/// </summary>
[Collection("Serial")]
public sealed class MainFormSidecarAlgoUiTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormSidecarAlgoUiTests() => _fixture = new AppFixture();
    public void Dispose()               => _fixture.Dispose();

    [Theory]
    [InlineData("AlgoMd5",    ".md5",    "md5sum format")]
    [InlineData("AlgoSha1",   ".sha1",   "sha1sum format")]
    [InlineData("AlgoSha512", ".sha512", "sha512sum format")]
    public void AlgorithmSwitch_UpdatesExtensionAndSumRadioLabel(
        string radioId, string expectedExt, string expectedLabelPrefix)
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId(radioId)).AsRadioButton().Click();
        Assert.True(TestHelpers.WaitUntilRadioChecked(Win, radioId, TimeSpan.FromSeconds(2)));

        Assert.True(TestHelpers.WaitUntilTextBoxText(Win, "SidecarExtBox", expectedExt, TimeSpan.FromSeconds(2)),
            $"Extension box should follow the algorithm to '{expectedExt}', got " +
            $"'{Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarExtBox")).AsTextBox().Text}'.");

        // WinForms RadioButton.Text surfaces as the UIA element Name.
        var sumRadio = Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarFmtSha256Sum"));
        Assert.Contains(expectedLabelPrefix, sumRadio.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlgorithmSwitch_RoundTrip_RestoresSha256Suggestion()
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoMd5")).AsRadioButton().Click();
        Assert.True(TestHelpers.WaitUntilTextBoxText(Win, "SidecarExtBox", ".md5", TimeSpan.FromSeconds(2)));

        Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoSha256")).AsRadioButton().Click();
        Assert.True(TestHelpers.WaitUntilTextBoxText(Win, "SidecarExtBox", ".sha256", TimeSpan.FromSeconds(2)),
            "Switching back to SHA256 should restore the .sha256 suggestion.");

        var sumRadio = Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarFmtSha256Sum"));
        Assert.Contains("sha256sum format", sumRadio.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlgorithmSwitch_CustomExtension_IsNeverClobbered()
    {
        // Enable the sidecar options so the extension box accepts input.
        var chk = Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarChk")).AsCheckBox();
        if (chk.IsChecked != true) chk.Toggle();

        var ext = Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarExtBox")).AsTextBox();
        ext.Text = ".custom";

        Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoMd5")).AsRadioButton().Click();
        Assert.True(TestHelpers.WaitUntilRadioChecked(Win, "AlgoMd5", TimeSpan.FromSeconds(2)));

        // The label follows the algorithm even while the custom extension stays.
        var sumRadio = Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarFmtSha256Sum"));
        Assert.Contains("md5sum format", sumRadio.Name, StringComparison.OrdinalIgnoreCase);

        // Give the (unwanted) update a moment to happen before asserting it didn't.
        Thread.Sleep(300);
        Assert.Equal(".custom",
            Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarExtBox")).AsTextBox().Text);
    }
}
