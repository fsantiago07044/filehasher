using FlaUI.Core.AutomationElements;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Folder-scanning behaviour: default filter, all-types mode, empty folders,
/// recursive subdirectories, file counts, and the Stop button.
/// </summary>
[Collection("Serial")]
public sealed class MainFormFolderTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormFolderTests() => _fixture = new AppFixture();
    public void Dispose()        => _fixture.Dispose();

    [Fact]
    public void FolderScan_DefaultFilter_OnlyHashesExeAndMsi()
    {
        // 2 .exe + 1 .msi + 2 .txt → only 3 should appear in results
        var dir = TestHelpers.CreateTempFolder("a.exe", "b.exe", "c.msi", "d.txt", "e.txt");
        try
        {
            RunFolder(dir);
            Assert.Equal(3, TestHelpers.GetResultsRowCount(Win));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FolderScan_AllTypes_HashesEveryFile()
    {
        var dir = TestHelpers.CreateTempFolder("a.exe", "b.txt", "c.pdf");
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = dir;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("AllTypesChk")).AsCheckBox().Toggle();
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();
            TestHelpers.DismissFirstButton(TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(15)));

            Assert.Equal(3, TestHelpers.GetResultsRowCount(Win));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FolderScan_NoMatchingFiles_ShowsStatusMessage()
    {
        // Folder with only .txt files — default filter matches nothing
        var dir = TestHelpers.CreateTempFolder("readme.txt", "notes.txt");
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = dir;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            // No completion dialog — the app returns early and updates the status label
            Assert.True(
                TestHelpers.WaitUntilStatusContains(Win, "No matching files found", TimeSpan.FromSeconds(10)),
                $"Expected 'No matching files found' status, got: '{TestHelpers.GetStatusText(Win)}'");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FolderScan_MultipleExe_CorrectRowCount()
    {
        var dir = TestHelpers.CreateTempFolder("x.exe", "y.exe", "z.exe");
        try
        {
            RunFolder(dir);
            Assert.Equal(3, TestHelpers.GetResultsRowCount(Win));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FolderScan_RecursiveSubfolders_HashesAllMatchingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var sub = Path.Combine(dir, "subdir");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(dir, "root.exe"),   "root");
        File.WriteAllText(Path.Combine(sub, "nested.exe"), "nested");
        try
        {
            RunFolder(dir);
            Assert.Equal(2, TestHelpers.GetResultsRowCount(Win));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FolderScan_EmptyFolder_ShowsNoFilesStatus()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = dir;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            Assert.True(
                TestHelpers.WaitUntilStatusContains(Win, "No matching files found", TimeSpan.FromSeconds(10)),
                $"Expected 'No matching files found', got: '{TestHelpers.GetStatusText(Win)}'");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void StopDuringRun_ButtonStatesReset()
    {
        var dir = BuildLargeFolder(20);
        try
        {
            var runBtn  = Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton();
            var stopBtn = Win.FindFirstDescendant(cf => cf.ByAutomationId("StopBtn")).AsButton();

            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = dir;
            runBtn.Click();

            Assert.True(TestHelpers.WaitUntilEnabled(stopBtn, TimeSpan.FromSeconds(5)),
                "Stop button did not become enabled after Run was clicked.");
            stopBtn.Click();

            // The run may finish before Stop takes effect on a fast machine, in which case
            // the completion dialog blocks Run from re-enabling until we dismiss it.
            WaitForRunEnd(runBtn, TimeSpan.FromSeconds(20));

            Assert.True(runBtn.IsEnabled,   "Run button should be enabled after the run ends.");
            Assert.False(stopBtn.IsEnabled, "Stop button should be disabled after the run ends.");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void StopDuringRun_StatusChangesFromReady()
    {
        // We can reliably assert that the status changes during a run, but not that
        // cancellation specifically happened (the run may complete before Stop registers).
        var dir = BuildLargeFolder(20);
        try
        {
            var runBtn  = Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton();
            var stopBtn = Win.FindFirstDescendant(cf => cf.ByAutomationId("StopBtn")).AsButton();

            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = dir;
            runBtn.Click();

            TestHelpers.WaitUntilEnabled(stopBtn, TimeSpan.FromSeconds(5));
            stopBtn.Click();
            WaitForRunEnd(runBtn, TimeSpan.FromSeconds(20));

            var status = TestHelpers.GetStatusText(Win);
            Assert.False(string.IsNullOrEmpty(status), "Status label should not be empty after a run.");
            Assert.NotEqual("Ready.", status, StringComparer.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(dir, true); }
    }

    // Waits for RunBtn to re-enable, dismissing any completion dialog that blocks it.
    private void WaitForRunEnd(AutomationElement runBtn, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (runBtn.IsEnabled) return;
            var modal = Win.ModalWindows.FirstOrDefault();
            if (modal is not null) TestHelpers.DismissFirstButton(modal);
            Thread.Sleep(100);
        }
    }

    [Fact]
    public void RunButton_IsDisabledWhileRunning()
    {
        var dir = TestHelpers.CreateTempFolder("a.exe", "b.exe");
        try
        {
            var runBtn = Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton();
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = dir;
            runBtn.Click();

            Assert.True(TestHelpers.WaitUntilDisabled(runBtn, TimeSpan.FromSeconds(5)),
                "Run button should be disabled while a run is in progress.");

            TestHelpers.DismissFirstButton(TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(15)));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void RunFolder(string dir)
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = dir;
        TestHelpers.ClickRunAndWaitForModal(Win, TimeSpan.FromSeconds(25));
    }

    private static string BuildLargeFolder(int count)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        for (int i = 0; i < count; i++)
            File.WriteAllBytes(Path.Combine(dir, $"file{i:D3}.exe"), new byte[64 * 1024]);
        return dir;
    }
}
