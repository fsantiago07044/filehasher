using System.Security.Cryptography;
using FlaUI.Core.AutomationElements;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// End-to-end tests for the Verify Sidecars button through the WinForms UI.
/// Verdict-level precision (parse errors, metadata notes, every status) lives
/// in <c>SidecarVerifierTests</c>; these tests assert the UI wiring — button
/// flow, completion dialog, status-line counts, and result-row counts.
/// Each test gets its own app process.
/// </summary>
[Collection("Serial")]
public sealed class MainFormVerifyTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormVerifyTests() => _fixture = new AppFixture();
    public void Dispose()        => _fixture.Dispose();

    [Fact]
    public void VerifyButton_EnabledAtStart()
    {
        Assert.True(Win.FindFirstDescendant(cf => cf.ByAutomationId("VerifyBtn")).AsButton().IsEnabled);
    }

    [Fact]
    public void VerifyWithNoPath_ShowsWarningDialog()
    {
        var dialog = TestHelpers.ClickButtonAndReturnModal(Win, "VerifyBtn", TimeSpan.FromSeconds(5));
        Assert.NotNull(dialog);
        TestHelpers.DismissFirstButton(dialog);
    }

    [Fact]
    public void Verify_FolderWithMixedStates_ReportsCountsInStatusAndRows()
    {
        var dir = CreateTempDir();
        try
        {
            var goodContent = new byte[] { 1, 2, 3 };
            var good        = WriteFile(dir, "good.exe", goodContent);
            File.WriteAllText(good + ".sha256", Convert.ToHexString(SHA256.HashData(goodContent)));

            var bad = WriteFile(dir, "bad.exe", new byte[] { 4, 5, 6 });
            File.WriteAllText(bad + ".sha256", new string('A', 64));

            WriteFile(dir, "naked.exe", new byte[] { 7 });

            RunVerifyOnPath(dir);

            Assert.True(TestHelpers.WaitUntilStatusContains(Win, "1 OK", TimeSpan.FromSeconds(5)),
                $"Status should report 1 OK; got '{TestHelpers.GetStatusText(Win)}'.");
            Assert.Contains("1 problem(s)",     TestHelpers.GetStatusText(Win));
            Assert.Contains("1 without sidecar", TestHelpers.GetStatusText(Win));
            Assert.Equal(3, TestHelpers.GetResultsRowCount(Win));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Verify_MixedAlgorithmSidecars_AutoDetectedInOnePass()
    {
        // Both sidecars use the .sha256 extension but hold MD5 and SHA512
        // hashes — the verifier must detect each from its hash length.
        var dir = CreateTempDir();
        try
        {
            var contentA = new byte[] { 10, 11 };
            var a        = WriteFile(dir, "a.exe", contentA);
            File.WriteAllText(a + ".sha256", Convert.ToHexString(MD5.HashData(contentA)));

            var contentB = new byte[] { 12, 13 };
            var b        = WriteFile(dir, "b.exe", contentB);
            File.WriteAllText(b + ".sha256", Convert.ToHexString(SHA512.HashData(contentB)));

            RunVerifyOnPath(dir);

            Assert.True(TestHelpers.WaitUntilStatusContains(Win, "2 OK", TimeSpan.FromSeconds(5)),
                $"Status should report 2 OK; got '{TestHelpers.GetStatusText(Win)}'.");
            Assert.Contains("0 problem(s)", TestHelpers.GetStatusText(Win));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Verify_SidecarFileTargetedDirectly_VerifiesItsBaseFile()
    {
        var dir = CreateTempDir();
        try
        {
            var content = new byte[] { 20, 21, 22 };
            var file    = WriteFile(dir, "target.exe", content);
            var sidecar = file + ".sha256";
            File.WriteAllText(sidecar, Convert.ToHexString(SHA256.HashData(content)));

            RunVerifyOnPath(sidecar);

            Assert.True(TestHelpers.WaitUntilStatusContains(Win, "1 OK", TimeSpan.FromSeconds(5)),
                $"Status should report 1 OK; got '{TestHelpers.GetStatusText(Win)}'.");
            Assert.Contains("0 problem(s)", TestHelpers.GetStatusText(Win));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Verify_OrphanSidecar_ReportsProblem()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ghost.exe.sha256"), new string('B', 64));

            RunVerifyOnPath(dir);

            Assert.True(TestHelpers.WaitUntilStatusContains(Win, "1 problem(s)", TimeSpan.FromSeconds(5)),
                $"Status should report 1 problem; got '{TestHelpers.GetStatusText(Win)}'.");
            Assert.Contains("0 OK", TestHelpers.GetStatusText(Win));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Verify_ThenHashRun_BothCompleteCleanly()
    {
        // The two modes share the results list, progress bar, and logger — a
        // hash run immediately after a verify run must still work end-to-end.
        var dir = CreateTempDir();
        try
        {
            var content = new byte[] { 30, 31 };
            var file    = WriteFile(dir, "a.exe", content);
            File.WriteAllText(file + ".sha256", Convert.ToHexString(SHA256.HashData(content)));

            RunVerifyOnPath(dir);
            Assert.True(TestHelpers.WaitUntilStatusContains(Win, "1 OK", TimeSpan.FromSeconds(5)));

            // Hash run afterwards still works and repopulates normal results.
            TestHelpers.RunHashOnFile(Win, file);
            Assert.True(TestHelpers.WaitUntilStatusContains(Win, "1 hashed", TimeSpan.FromSeconds(5)),
                $"Hash run after a verify run should complete normally; got '{TestHelpers.GetStatusText(Win)}'.");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void RunVerifyOnPath(string path)
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = path;
        var modal = TestHelpers.ClickButtonAndReturnModal(Win, "VerifyBtn", TimeSpan.FromSeconds(30));
        TestHelpers.DismissFirstButton(modal);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteFile(string dir, string name, byte[] content)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }
}
