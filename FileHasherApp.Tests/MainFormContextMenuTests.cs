using System.Security.Cryptography;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// The right-click context menu on result rows (item presence, enabled states,
/// clipboard copy actions) and double-click row activation. The three Open
/// items are deliberately never invoked — they would spawn real Explorer /
/// PowerShell / cmd processes on the test host; their presence and enabled
/// state are asserted instead. Each test gets its own app process.
/// </summary>
[Collection("Serial")]
public sealed class MainFormContextMenuTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormContextMenuTests() => _fixture = new AppFixture();
    public void Dispose()             => _fixture.Dispose();

    private static readonly (string Id, string Text)[] AllMenuItems =
    {
        ("MiOpenExplorer",   "Open in File Explorer"),
        ("MiOpenPowerShell", "Open PowerShell here"),
        ("MiOpenCmd",        "Open Command Prompt here"),
        ("MiCopyHash",       "Copy Hash"),
        ("MiCopyPath",       "Copy File Path"),
    };

    [Fact]
    public void ContextMenu_OnHashedRow_ShowsAllItemsEnabled()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 1, 2, 3 });
        try
        {
            var row = HashAndGetSingleRow(tmp);
            row.RightClick();

            var menu = TestHelpers.GetOpenContextMenu(Win, TimeSpan.FromSeconds(5));
            Assert.NotNull(menu);

            foreach (var (id, text) in AllMenuItems)
            {
                var item = TestHelpers.FindMenuItem(menu!, id, text);
                Assert.True(item is not null, $"Menu item '{id}' ('{text}') not found.");
                Assert.True(item!.IsEnabled,  $"Menu item '{id}' should be enabled for a hashed row.");
            }

            Keyboard.Type(VirtualKeyShort.ESCAPE);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ContextMenu_CopyHash_PutsHashOnClipboard()
    {
        var content = new byte[] { 4, 5, 6, 7 };
        var tmp     = TestHelpers.CreateTempFile(content);
        try
        {
            var expected = Convert.ToHexString(SHA256.HashData(content));
            TestHelpers.ClearClipboardSta();

            var row = HashAndGetSingleRow(tmp);
            row.RightClick();

            var menu = TestHelpers.GetOpenContextMenu(Win, TimeSpan.FromSeconds(5));
            Assert.NotNull(menu);
            TestHelpers.FindMenuItem(menu!, "MiCopyHash", "Copy Hash")!.AsMenuItem().Invoke();

            Assert.True(TestHelpers.WaitUntilClipboardText(expected, TimeSpan.FromSeconds(5)),
                $"Clipboard should hold the hash '{expected}', got '{TestHelpers.GetClipboardTextSta()}'.");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ContextMenu_CopyPath_PutsFullPathOnClipboard()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 8, 9 });
        try
        {
            TestHelpers.ClearClipboardSta();

            var row = HashAndGetSingleRow(tmp);
            row.RightClick();

            var menu = TestHelpers.GetOpenContextMenu(Win, TimeSpan.FromSeconds(5));
            Assert.NotNull(menu);
            TestHelpers.FindMenuItem(menu!, "MiCopyPath", "Copy File Path")!.AsMenuItem().Invoke();

            Assert.True(TestHelpers.WaitUntilClipboardText(tmp, TimeSpan.FromSeconds(5)),
                $"Clipboard should hold the path '{tmp}', got '{TestHelpers.GetClipboardTextSta()}'.");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ContextMenu_CopyHash_DisabledOnErrorRow_CopyPathStillEnabled()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 10, 11 });
        try
        {
            // Hold an exclusive lock so the app's read (FileShare.ReadWrite)
            // fails and the row lands as an error row with no hash.
            using (new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var row = HashAndGetSingleRow(tmp);
                row.RightClick();

                var menu = TestHelpers.GetOpenContextMenu(Win, TimeSpan.FromSeconds(5));
                Assert.NotNull(menu);

                Assert.False(TestHelpers.FindMenuItem(menu!, "MiCopyHash", "Copy Hash")!.IsEnabled,
                    "Copy Hash should be disabled on an error row.");
                Assert.True(TestHelpers.FindMenuItem(menu!, "MiCopyPath", "Copy File Path")!.IsEnabled,
                    "Copy File Path should stay enabled on an error row.");

                Keyboard.Type(VirtualKeyShort.ESCAPE);
            }
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RowDoubleClick_WithDeletedFolder_IsSafeNoOp()
    {
        // Deleting the row's folder before double-clicking exercises the
        // fallback chain in OpenInExplorer (file gone → folder gone → no-op)
        // without spawning a real Explorer window on the test host.
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "a.exe");
        File.WriteAllBytes(file, new byte[] { 12, 13 });

        var row = HashAndGetSingleRow(file);
        Directory.Delete(dir, true);

        row.DoubleClick();
        Thread.Sleep(500);

        // The app must still be alive and responsive.
        Assert.StartsWith("FileHasher", Win.Title);
        Assert.True(Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().IsEnabled);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private AutomationElement HashAndGetSingleRow(string filePath)
    {
        TestHelpers.RunHashOnFile(Win, filePath);
        var list = Win.FindFirstDescendant(cf => cf.ByAutomationId("ResultsView"));
        var rows = list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
        Assert.Single(rows);
        return rows[0];
    }
}
