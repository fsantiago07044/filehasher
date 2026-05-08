using System.Security.Cryptography;
using FlaUI.Core.AutomationElements;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Algorithm selection and hash-value accuracy.  Hash output is verified by
/// enabling sidecar writing (hashonly format), reading the written sidecar file,
/// and comparing against the .NET crypto provider for the same input bytes.
/// </summary>
[Collection("Serial")]
public sealed class MainFormAlgorithmTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormAlgorithmTests() => _fixture = new AppFixture();
    public void Dispose()            => _fixture.Dispose();

    // ── Algorithm selection ──────────────────────────────────────────────────

    [Fact]
    public void AlgorithmSelection_CanSwitchToSha1()
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoSha1")).AsRadioButton().Click();
        // Poll: UIAutomation may lag behind the click before reflecting the new state.
        Assert.True(
            TestHelpers.WaitUntilRadioChecked(Win, "AlgoSha1", TimeSpan.FromSeconds(2)),
            "SHA1 radio button was not checked after click.");
        Assert.True(
            TestHelpers.WaitUntilRadioUnchecked(Win, "AlgoSha256", TimeSpan.FromSeconds(2)),
            "SHA256 radio button should be unchecked after selecting SHA1.");
    }

    [Fact]
    public void AlgorithmSelection_OnlyOneCanBeCheckedAtATime()
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoMd5")).AsRadioButton().Click();
        Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoSha512")).AsRadioButton().Click();

        // Poll each: UIAutomation state may not reflect clicks immediately.
        Assert.True(TestHelpers.WaitUntilRadioChecked(Win,   "AlgoSha512", TimeSpan.FromSeconds(2)), "SHA512 should be checked.");
        Assert.True(TestHelpers.WaitUntilRadioUnchecked(Win, "AlgoMd5",    TimeSpan.FromSeconds(2)), "MD5 should be unchecked.");
        Assert.True(TestHelpers.WaitUntilRadioUnchecked(Win, "AlgoSha1",   TimeSpan.FromSeconds(2)), "SHA1 should be unchecked.");
        Assert.True(TestHelpers.WaitUntilRadioUnchecked(Win, "AlgoSha256", TimeSpan.FromSeconds(2)), "SHA256 should be unchecked.");
    }

    // ── Hash accuracy (verified via sidecar output) ──────────────────────────

    [Theory]
    [InlineData("AlgoMd5",    "MD5")]
    [InlineData("AlgoSha1",   "SHA1")]
    [InlineData("AlgoSha256", "SHA256")]
    [InlineData("AlgoSha512", "SHA512")]
    public void HashAccuracy_MatchesDotNetCrypto(string radioId, string algorithm)
    {
        // Known fixed content so the expected hash is deterministic
        var content    = new byte[] { 0x46, 0x6C, 0x61, 0x55, 0x49, 0x74, 0x65, 0x73, 0x74 };
        var sidecarExt = ".testhash";
        var tmp        = TestHelpers.CreateTempFile(content);
        var sidecar    = tmp + sidecarExt;

        try
        {
            // Select algorithm
            Win.FindFirstDescendant(cf => cf.ByAutomationId(radioId)).AsRadioButton().Click();

            // Enable sidecar with hash-only format and a unique extension
            EnableSidecar(ext: sidecarExt, hashOnly: true);

            TestHelpers.RunHashOnFile(Win, tmp);

            Assert.True(File.Exists(sidecar), $"Sidecar not created for {algorithm}.");
            var appHash      = File.ReadAllText(sidecar).Trim();
            var expectedHash = ComputeExpected(algorithm, content);
            Assert.Equal(expectedHash, appHash, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tmp);
            if (File.Exists(sidecar)) File.Delete(sidecar);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void EnableSidecar(string ext, bool hashOnly)
    {
        var chk = Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarChk")).AsCheckBox();
        if (chk.IsChecked != true) chk.Toggle();
        Win.FindFirstDescendant(cf => cf.ByAutomationId("SidecarExtBox")).AsTextBox().Text = ext;
        var fmtId = hashOnly ? "SidecarFmtHashOnly" : "SidecarFmtSha256Sum";
        Win.FindFirstDescendant(cf => cf.ByAutomationId(fmtId)).AsRadioButton().Click();
        // Wait for the format selection to register before hashing begins.
        TestHelpers.WaitUntilRadioChecked(Win, fmtId, TimeSpan.FromSeconds(2));
    }

    private static string ComputeExpected(string algorithm, byte[] data)
    {
        using HashAlgorithm algo = algorithm switch
        {
            "MD5"    => MD5.Create(),
            "SHA1"   => SHA1.Create(),
            "SHA512" => SHA512.Create(),
            _        => SHA256.Create()
        };
        return Convert.ToHexString(algo.ComputeHash(data));
    }
}
