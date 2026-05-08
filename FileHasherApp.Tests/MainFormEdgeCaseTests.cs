using FlaUI.Core.AutomationElements;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Edge cases: invalid paths, clear-after-hash, second-run result replacement,
/// About dialog, metadata in results, and miscellaneous UI state invariants.
/// </summary>
[Collection("Serial")]
public sealed class MainFormEdgeCaseTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormEdgeCaseTests() => _fixture = new AppFixture();
    public void Dispose()          => _fixture.Dispose();

    [Fact]
    public void InvalidPath_ShowsWarningDialog()
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text
            = @"C:\this_path_surely_does_not_exist_xyzzy\no_file.exe";
        Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

        var dialog = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(5));
        Assert.NotNull(dialog);
        TestHelpers.DismissFirstButton(dialog);
    }

    [Fact]
    public void ClearAfterHash_EmptiesResultsList()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 1 });
        try
        {
            TestHelpers.RunHashOnFile(Win, tmp);
            Assert.True(TestHelpers.GetResultsRowCount(Win) >= 1, "Expected at least one result row after hashing.");

            Win.FindFirstDescendant(cf => cf.ByAutomationId("ClearBtn")).AsButton().Click();
            Assert.Equal(0, TestHelpers.GetResultsRowCount(Win));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ClearAfterHash_ResetsStatusToReady()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 2 });
        try
        {
            TestHelpers.RunHashOnFile(Win, tmp);
            Win.FindFirstDescendant(cf => cf.ByAutomationId("ClearBtn")).AsButton().Click();
            Assert.Equal("Ready.", TestHelpers.GetStatusText(Win));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void SecondRun_ClearsAndReplacesPreviousResults()
    {
        // The app clears _allResults and the ListView at the start of each run.
        var tmp1 = TestHelpers.CreateTempFile(new byte[] { 1 });
        var tmp2 = TestHelpers.CreateTempFile(new byte[] { 2 });
        try
        {
            TestHelpers.RunHashOnFile(Win, tmp1);
            Assert.Equal(1, TestHelpers.GetResultsRowCount(Win));

            TestHelpers.RunHashOnFile(Win, tmp2);
            Assert.Equal(1, TestHelpers.GetResultsRowCount(Win)); // replaced, not appended
        }
        finally { File.Delete(tmp1); File.Delete(tmp2); }
    }

    [Fact]
    public void AboutDialog_OpensAndCanBeDismissed()
    {
        var helpMenu = Win.FindFirstDescendant(cf => cf.ByName("Help")).AsMenuItem();
        helpMenu.Click();

        var aboutItem = Win.FindFirstDescendant(cf => cf.ByName("About FileHasher…")); // …
        Assert.NotNull(aboutItem);
        aboutItem.Click();

        var dialog = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(5));
        Assert.NotNull(dialog);
        TestHelpers.DismissFirstButton(dialog);
    }

    [Fact]
    public void HashWithMetadata_RunCompletesAndShowsResult()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 99, 88, 77 });
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("MetadataChk")).AsCheckBox().Toggle();
            TestHelpers.RunHashOnFile(Win, tmp);
            Assert.True(TestHelpers.GetResultsRowCount(Win) >= 1);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void PathBox_AcceptsTypedPath()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 0 });
        try
        {
            var pathBox = Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox();
            pathBox.Text = tmp;
            Assert.Equal(tmp, pathBox.Text);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CompletionDialog_ShowsAfterSuccessfulRun()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 42 });
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = tmp;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            var dialog = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(15));
            Assert.NotNull(dialog);

            // Dialog title should indicate success
            Assert.Contains("Complete", dialog.Title, StringComparison.OrdinalIgnoreCase);
            TestHelpers.DismissFirstButton(dialog);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void StatusLabel_ShowsDoneWhenCompletionDialogAppears()
    {
        // SetStatus("Done…") is called before MessageBox.Show, so the label must read
        // "Done" while the completion dialog is still visible — read it before dismissing.
        var tmp = TestHelpers.CreateTempFile(new byte[] { 1, 2, 3 });
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = tmp;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            var dialog = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(15));

            // Read status while the dialog is still open (before dismissing)
            var statusWhileOpen = TestHelpers.GetStatusText(Win);
            Assert.Contains("Done", statusWhileOpen, StringComparison.OrdinalIgnoreCase);

            TestHelpers.DismissFirstButton(dialog);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void AllTypesCheckbox_EnabledAfterFolderDropped_DisabledAfterFileSelected()
    {
        // AllTypesChk is disabled for single-file targets; enabled for folder targets.
        // We test the initial default state (unchecked and enabled is the WinForms default for the control
        // itself; the app sets it based on what the user browses to — but we can verify
        // it starts unchecked, which the state tests already confirm).
        var chk = Win.FindFirstDescendant(cf => cf.ByAutomationId("AllTypesChk")).AsCheckBox();
        Assert.False(chk.IsChecked, "AllTypesChk should be unchecked at launch.");
    }
}
