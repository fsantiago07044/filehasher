using System.Security.Cryptography;
using FlaUI.Core.AutomationElements;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Sidecar file creation, format verification, custom extensions, and the full
/// conflict-resolution dialog (Overwrite / Overwrite All / Skip / Skip All).
/// </summary>
[Collection("Serial")]
public sealed class MainFormSidecarTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormSidecarTests() => _fixture = new AppFixture();
    public void Dispose()         => _fixture.Dispose();

    // ── Sidecar creation ─────────────────────────────────────────────────────

    [Fact]
    public void SidecarWrite_CreatesFileNextToTarget()
    {
        var tmp     = TestHelpers.CreateTempFile(new byte[] { 1, 2, 3 });
        var sidecar = tmp + ".sha256";
        try
        {
            EnableSidecar();
            TestHelpers.RunHashOnFile(Win, tmp);
            Assert.True(File.Exists(sidecar), "Expected sidecar .sha256 file to exist.");
        }
        finally { Cleanup(tmp, sidecar); }
    }

    [Fact]
    public void SidecarWrite_Sha256SumFormat_ContainsHashAndFilename()
    {
        var content = new byte[] { 10, 20, 30, 40 };
        var tmp     = TestHelpers.CreateTempFile(content);
        var sidecar = tmp + ".sha256";
        try
        {
            EnableSidecar(hashOnly: false);
            TestHelpers.RunHashOnFile(Win, tmp);

            var text         = File.ReadAllText(sidecar).Trim();
            var expectedHash = Convert.ToHexString(SHA256.HashData(content));

            Assert.Contains(expectedHash,          text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.GetFileName(tmp), text, StringComparison.Ordinal);
            Assert.Contains("*",                   text, StringComparison.Ordinal);
        }
        finally { Cleanup(tmp, sidecar); }
    }

    [Fact]
    public void SidecarWrite_HashOnlyFormat_ContainsHashOnly()
    {
        var content = new byte[] { 5, 6, 7, 8 };
        var tmp     = TestHelpers.CreateTempFile(content);
        var sidecar = tmp + ".sha256";
        try
        {
            EnableSidecar(hashOnly: true);
            TestHelpers.RunHashOnFile(Win, tmp);

            var text         = File.ReadAllText(sidecar).Trim();
            var expectedHash = Convert.ToHexString(SHA256.HashData(content));

            Assert.Equal(expectedHash, text, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("*",                   text);
            Assert.DoesNotContain(Path.GetFileName(tmp), text);
        }
        finally { Cleanup(tmp, sidecar); }
    }

    [Fact]
    public void SidecarWrite_CustomExtension_UsesCorrectExtension()
    {
        var tmp     = TestHelpers.CreateTempFile(new byte[] { 99 });
        var sidecar = tmp + ".myhash";
        try
        {
            EnableSidecar(ext: ".myhash");
            TestHelpers.RunHashOnFile(Win, tmp);
            Assert.True(File.Exists(sidecar), "Sidecar with custom extension was not created.");
            Assert.False(File.Exists(tmp + ".sha256"), "Default .sha256 sidecar should not exist.");
        }
        finally { Cleanup(tmp, sidecar); }
    }

    [Fact]
    public void SidecarWrite_NeverCreatesSidecarOfSidecar()
    {
        // The sidecar file itself must never be treated as a target on repeat runs.
        var tmp     = TestHelpers.CreateTempFile(new byte[] { 1 });
        var sidecar = tmp + ".sha256";
        try
        {
            EnableSidecar();
            TestHelpers.RunHashOnFile(Win, tmp);
            Assert.True(File.Exists(sidecar));

            // Run again — sidecar.sha256.sha256 must not be created
            TestHelpers.RunHashOnFile(Win, tmp);
            Assert.False(File.Exists(sidecar + ".sha256"),
                "App must not create a sidecar-of-sidecar on a second run.");
        }
        finally { Cleanup(tmp, sidecar); }
    }

    // ── Conflict dialog ──────────────────────────────────────────────────────

    [Fact]
    public void SidecarConflict_Overwrite_UpdatesSidecarContent()
    {
        var tmp     = TestHelpers.CreateTempFile(new byte[] { 1, 2, 3 });
        var sidecar = tmp + ".sha256";
        File.WriteAllText(sidecar, "ORIGINAL_PLACEHOLDER *fakefile");
        try
        {
            EnableSidecar();
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = tmp;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            var conflict = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(5));
            TestHelpers.ClickDialogButton(conflict, "Overwrite");

            var completion = TestHelpers.WaitForNextModal(Win, TimeSpan.FromSeconds(15));
            TestHelpers.DismissFirstButton(completion);

            Assert.DoesNotContain("ORIGINAL_PLACEHOLDER", File.ReadAllText(sidecar));
        }
        finally { Cleanup(tmp, sidecar); }
    }

    [Fact]
    public void SidecarConflict_Skip_LeavesExistingSidecarUnchanged()
    {
        const string original = "ORIGINAL *fakefile";
        var tmp               = TestHelpers.CreateTempFile(new byte[] { 1, 2, 3 });
        var sidecar           = tmp + ".sha256";
        File.WriteAllText(sidecar, original);
        try
        {
            EnableSidecar();
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = tmp;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            var conflict = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(5));
            TestHelpers.ClickDialogButton(conflict, "Skip");

            var completion = TestHelpers.WaitForNextModal(Win, TimeSpan.FromSeconds(15));
            TestHelpers.DismissFirstButton(completion);

            Assert.Equal(original, File.ReadAllText(sidecar));
        }
        finally { Cleanup(tmp, sidecar); }
    }

    [Fact]
    public void SidecarConflict_OverwriteAll_UpdatesAllSidecarsWithOnlyOneDialog()
    {
        var dir  = BuildConflictFolder(out var sc1, out var sc2, "OLD1", "OLD2");
        try
        {
            EnableSidecar();
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = dir;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            // Only the first conflict dialog appears; clicking Overwrite All suppresses the rest
            var conflict = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(5));
            TestHelpers.ClickDialogButton(conflict, "Overwrite All");

            var completion = TestHelpers.WaitForNextModal(Win, TimeSpan.FromSeconds(20));
            TestHelpers.DismissFirstButton(completion);

            Assert.DoesNotContain("OLD1", File.ReadAllText(sc1));
            Assert.DoesNotContain("OLD2", File.ReadAllText(sc2));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SidecarConflict_SkipAll_PreservesAllSidecarsWithOnlyOneDialog()
    {
        var dir = BuildConflictFolder(out var sc1, out var sc2, "KEEP1", "KEEP2");
        try
        {
            EnableSidecar();
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = dir;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            var conflict = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(5));
            TestHelpers.ClickDialogButton(conflict, "Skip All");

            // Completion dialog still appears (0 files hashed, N skipped)
            var completion = TestHelpers.WaitForNextModal(Win, TimeSpan.FromSeconds(15));
            TestHelpers.DismissFirstButton(completion);

            Assert.Equal("KEEP1", File.ReadAllText(sc1));
            Assert.Equal("KEEP2", File.ReadAllText(sc2));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SidecarConflict_SkipAll_ResultsViewIsEmpty()
    {
        var dir = BuildConflictFolder(out _, out _, "X", "Y");
        try
        {
            EnableSidecar();
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = dir;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            TestHelpers.ClickDialogButton(TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(5)), "Skip All");
            TestHelpers.DismissFirstButton(TestHelpers.WaitForNextModal(Win, TimeSpan.FromSeconds(15)));

            Assert.Equal(0, TestHelpers.GetResultsRowCount(Win));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void EnableSidecar(string ext = ".sha256", bool hashOnly = false)
    {
        var chk = Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarChk")).AsCheckBox();
        if (chk.IsChecked != true) chk.Toggle();
        Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarExtBox")).AsTextBox().Text = ext;
        var fmtId = hashOnly ? "SidecarFmtHashOnly" : "SidecarFmtSha256Sum";
        Win.FindFirstDescendant(cf => cf.ByAutomationId(fmtId)).AsRadioButton().Click();
    }

    private static string BuildConflictFolder(out string sidecar1, out string sidecar2,
                                               string sc1Content, string sc2Content)
    {
        var dir   = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var file1 = Path.Combine(dir, "a.exe"); File.WriteAllBytes(file1, new byte[] { 1 });
        var file2 = Path.Combine(dir, "b.exe"); File.WriteAllBytes(file2, new byte[] { 2 });
        sidecar1  = file1 + ".sha256"; File.WriteAllText(sidecar1, sc1Content);
        sidecar2  = file2 + ".sha256"; File.WriteAllText(sidecar2, sc2Content);
        return dir;
    }

    private static void Cleanup(string file, string sidecar)
    {
        if (File.Exists(file))    File.Delete(file);
        if (File.Exists(sidecar)) File.Delete(sidecar);
    }
}
