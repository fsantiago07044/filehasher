using System.Security.Cryptography;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Direct unit tests against the internal <see cref="SidecarVerifier"/> class:
/// sidecar parsing across all three formats, hash-length algorithm auto-detection,
/// status classification (OK / MISMATCH / MISSING FILE / NO SIDECAR / PARSE ERROR),
/// informational metadata notes, and folder enumeration/filtering. Complementary
/// to <c>MainFormVerifyTests</c>, which exercises the Verify Sidecars button
/// end-to-end through the WinForms UI.
///
/// These tests do NOT drive the UI and do NOT require an interactive desktop
/// session — they instantiate <see cref="SidecarVerifier"/> directly from
/// FileHasher.Core, so xUnit can parallelize them across the standard test
/// runner.
/// </summary>
public sealed class SidecarVerifierTests : IDisposable
{
    private readonly string _dir;

    public SidecarVerifierTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
    }

    // ── OK paths across all three formats ────────────────────────────────────

    [Fact]
    public async Task Ok_BareHashFormat()
    {
        var file = WriteFile("a.exe", new byte[] { 1, 2, 3 });
        File.WriteAllText(file + ".sha256", Hash("SHA256", new byte[] { 1, 2, 3 }));

        var (results, summary) = await RunAsync(file, isFile: true);

        var r = Assert.Single(results);
        Assert.Equal(VerifyStatus.Ok, r.Status);
        Assert.Equal("SHA256", r.Algorithm);
        Assert.NotNull(r.ComputedHash);
        Assert.Null(r.Detail);
        Assert.Equal(1, summary.Ok);
    }

    [Fact]
    public async Task Ok_SumFormat_WithMatchingFilename()
    {
        var content = new byte[] { 4, 5, 6 };
        var file    = WriteFile("a.exe", content);
        File.WriteAllText(file + ".sha256", $"{Hash("SHA256", content)} *a.exe");

        var (results, _) = await RunAsync(file, isFile: true);

        var r = Assert.Single(results);
        Assert.Equal(VerifyStatus.Ok, r.Status);
        Assert.Null(r.Detail);
    }

    [Fact]
    public async Task Ok_ExtendedFormat_AllMetadataMatching_NoNotes()
    {
        var content = new byte[] { 7, 8, 9, 10 };
        var file    = WriteFile("a.exe", content);
        var fi      = new FileInfo(file);
        File.WriteAllText(file + ".sha256",
            $"{Hash("SHA256", content)} *a.exe *{fi.LastWriteTimeUtc:yyyy-MM-ddTHH:mm:ssZ} *{fi.Length}");

        var (results, _) = await RunAsync(file, isFile: true);

        var r = Assert.Single(results);
        Assert.Equal(VerifyStatus.Ok, r.Status);
        Assert.Null(r.Detail);
    }

    [Fact]
    public async Task Ok_LowercaseHash_ComparedCaseInsensitively()
    {
        var content = new byte[] { 11, 12 };
        var file    = WriteFile("a.exe", content);
        File.WriteAllText(file + ".sha256", Hash("SHA256", content).ToLowerInvariant());

        var (results, _) = await RunAsync(file, isFile: true);

        Assert.Equal(VerifyStatus.Ok, Assert.Single(results).Status);
    }

    [Fact]
    public async Task Ok_LeadingBlankLines_AreSkipped()
    {
        var content = new byte[] { 13, 14 };
        var file    = WriteFile("a.exe", content);
        File.WriteAllText(file + ".sha256", $"\r\n\r\n{Hash("SHA256", content)} *a.exe\r\n");

        var (results, _) = await RunAsync(file, isFile: true);

        Assert.Equal(VerifyStatus.Ok, Assert.Single(results).Status);
    }

    // ── Informational notes never demote an OK row ───────────────────────────

    [Fact]
    public async Task Ok_ExtendedFormat_DifferingDateAndSize_NotedButStillOk()
    {
        var content = new byte[] { 15, 16, 17 };
        var file    = WriteFile("a.exe", content);
        File.WriteAllText(file + ".sha256",
            $"{Hash("SHA256", content)} *a.exe *2000-01-01T00:00:00Z *999999");

        var (results, summary) = await RunAsync(file, isFile: true);

        var r = Assert.Single(results);
        Assert.Equal(VerifyStatus.Ok, r.Status);
        Assert.NotNull(r.Detail);
        Assert.Contains("modified date differs", r.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("size differs",          r.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, summary.Ok);
    }

    [Fact]
    public async Task Ok_SumFormat_DifferentEmbeddedFilename_Noted()
    {
        var content = new byte[] { 18, 19 };
        var file    = WriteFile("a.exe", content);
        File.WriteAllText(file + ".sha256", $"{Hash("SHA256", content)} *renamed.exe");

        var (results, _) = await RunAsync(file, isFile: true);

        var r = Assert.Single(results);
        Assert.Equal(VerifyStatus.Ok, r.Status);
        Assert.NotNull(r.Detail);
        Assert.Contains("renamed.exe", r.Detail);
    }

    // ── Failure classifications ──────────────────────────────────────────────

    [Fact]
    public async Task Mismatch_ReportsExpectedAndComputedHashes()
    {
        var content = new byte[] { 20, 21, 22 };
        var file    = WriteFile("a.exe", content);
        var wrong   = new string('A', 64);
        File.WriteAllText(file + ".sha256", wrong);

        var (results, summary) = await RunAsync(file, isFile: true);

        var r = Assert.Single(results);
        Assert.Equal(VerifyStatus.Mismatch, r.Status);
        Assert.Equal("SHA256", r.Algorithm);
        Assert.Contains(wrong,                    r.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Hash("SHA256", content),  r.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, summary.Mismatch);
    }

    [Fact]
    public async Task MissingFile_WhenSidecarHasNoBaseFile()
    {
        File.WriteAllText(Path.Combine(_dir, "ghost.exe.sha256"), new string('B', 64));

        var (results, summary) = await RunAsync(_dir, isFile: false);

        var r = Assert.Single(results);
        Assert.Equal(VerifyStatus.MissingFile, r.Status);
        Assert.EndsWith("ghost.exe", r.FilePath);
        Assert.Equal(1, summary.MissingFile);
    }

    [Fact]
    public async Task NoSidecar_ForSingleFileTargetWithoutSidecar()
    {
        var file = WriteFile("a.exe", new byte[] { 23 });

        var (results, summary) = await RunAsync(file, isFile: true);

        Assert.Equal(VerifyStatus.NoSidecar, Assert.Single(results).Status);
        Assert.Equal(1, summary.NoSidecar);
    }

    [Fact]
    public async Task ParseError_NonHexContent()
    {
        var file = WriteFile("a.exe", new byte[] { 24 });
        File.WriteAllText(file + ".sha256", "this is not a hash at all");

        var (results, summary) = await RunAsync(file, isFile: true);

        Assert.Equal(VerifyStatus.ParseError, Assert.Single(results).Status);
        Assert.Equal(1, summary.ParseError);
    }

    [Fact]
    public async Task ParseError_UnsupportedHashLength()
    {
        var file = WriteFile("a.exe", new byte[] { 25 });
        File.WriteAllText(file + ".sha256", new string('C', 50));   // no algorithm has 50 hex chars

        var (results, _) = await RunAsync(file, isFile: true);

        Assert.Equal(VerifyStatus.ParseError, Assert.Single(results).Status);
    }

    // ── Algorithm auto-detection from hash length ────────────────────────────

    [Theory]
    [InlineData("MD5")]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("SHA512")]
    public async Task AlgorithmAutoDetected_FromHashLength(string algorithm)
    {
        var content = new byte[] { 26, 27, 28, 29 };
        var file    = WriteFile("a.exe", content);
        File.WriteAllText(file + ".sha256", Hash(algorithm, content));

        var (results, _) = await RunAsync(file, isFile: true);

        var r = Assert.Single(results);
        Assert.Equal(VerifyStatus.Ok, r.Status);
        Assert.Equal(algorithm,       r.Algorithm);
    }

    // ── Target-shape variants ────────────────────────────────────────────────

    [Fact]
    public async Task SidecarTargetedDirectly_VerifiesItsBaseFile()
    {
        var content = new byte[] { 30, 31 };
        var file    = WriteFile("a.exe", content);
        var sidecar = file + ".sha256";
        File.WriteAllText(sidecar, Hash("SHA256", content));

        var (results, _) = await RunAsync(sidecar, isFile: true);

        var r = Assert.Single(results);
        Assert.Equal(VerifyStatus.Ok, r.Status);
        Assert.Equal(file,            r.FilePath);
    }

    // ── Folder enumeration, filtering, and summary counts ────────────────────

    [Fact]
    public async Task Folder_MixedStatuses_ClassifiedAndCounted()
    {
        var goodContent = new byte[] { 32, 33 };
        var good        = WriteFile("good.exe", goodContent);
        File.WriteAllText(good + ".sha256", Hash("SHA256", goodContent));

        var bad = WriteFile("bad.exe", new byte[] { 34, 35 });
        File.WriteAllText(bad + ".sha256", new string('D', 64));

        WriteFile("naked.exe", new byte[] { 36 });                      // filter match, no sidecar
        WriteFile("readme.txt", new byte[] { 37 });                     // filtered out (AllTypes off)
        File.WriteAllText(Path.Combine(_dir, "ghost.exe.sha256"), new string('E', 64));

        var (results, summary) = await RunAsync(_dir, isFile: false);

        Assert.Equal(4, results.Count);
        Assert.Equal(1, summary.Ok);
        Assert.Equal(1, summary.Mismatch);
        Assert.Equal(1, summary.MissingFile);
        Assert.Equal(1, summary.NoSidecar);
        Assert.Equal(0, summary.ParseError);
        Assert.Equal(0, summary.ReadError);

        Assert.Equal(VerifyStatus.NoSidecar,
            Assert.Single(results, r => r.FilePath.EndsWith("naked.exe")).Status);
    }

    [Fact]
    public async Task Folder_AllTypes_AuditsNonExeFilesToo()
    {
        WriteFile("readme.txt", new byte[] { 38 });

        var (offResults, _) = await RunAsync(_dir, isFile: false, allTypes: false);
        Assert.Empty(offResults);

        var (onResults, onSummary) = await RunAsync(_dir, isFile: false, allTypes: true);
        Assert.Equal(VerifyStatus.NoSidecar, Assert.Single(onResults).Status);
        Assert.Equal(1, onSummary.NoSidecar);
    }

    [Fact]
    public async Task Folder_ResultsAreSortedByPath()
    {
        WriteFile("bbb.exe", new byte[] { 39 });
        WriteFile("aaa.exe", new byte[] { 40 });
        WriteFile("ccc.exe", new byte[] { 41 });

        var (results, _) = await RunAsync(_dir, isFile: false);

        var paths = results.Select(r => r.FilePath).ToList();
        Assert.Equal(paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(), paths);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    // ── Depth-bounded enumeration ────────────────────────────────────────────
    //
    // Every Windows release has walked folders without limit, so unlimited
    // stays the default and the first test here is a regression guard on that:
    // if it ever fails, existing users silently stopped seeing files they used
    // to see. The bounded cases pin the semantics the UI depth control and the
    // CLI's --max-depth both expose: 0 is the target folder alone, N is N
    // levels below it.

    [Fact]
    public async Task Depth_DefaultIsUnlimited()
    {
        WriteTree();

        var (results, _) = await RunAsync(_dir, isFile: false);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(VerifyStatus.Ok, r.Status));
    }

    [Fact]
    public async Task Depth_Zero_ScansTargetFolderOnly()
    {
        WriteTree();

        var (results, _) = await RunAsync(_dir, isFile: false, maxDepth: 0);

        var r = Assert.Single(results);
        Assert.Equal("root.exe", Path.GetFileName(r.FilePath));
    }

    [Fact]
    public async Task Depth_One_ScansTargetFolderAndOneLevelBelow()
    {
        WriteTree();

        var (results, _) = await RunAsync(_dir, isFile: false, maxDepth: 1);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => Path.GetFileName(r.FilePath) == "root.exe");
        Assert.Contains(results, r => Path.GetFileName(r.FilePath) == "one.exe");
        Assert.DoesNotContain(results, r => Path.GetFileName(r.FilePath) == "two.exe");
    }

    [Fact]
    public async Task Depth_Negative_IsClampedToTargetFolderOnly()
    {
        WriteTree();

        var (results, _) = await RunAsync(_dir, isFile: false, maxDepth: -5);

        var r = Assert.Single(results);
        Assert.Equal("root.exe", Path.GetFileName(r.FilePath));
    }

    /// <summary>root.exe, sub/one.exe, sub/deeper/two.exe, each with a valid
    /// sidecar so every enumerated file lands as Ok and the count is purely a
    /// statement about how far the walk reached.</summary>
    private void WriteTree()
    {
        WriteWithSidecar("root.exe");
        WriteWithSidecar(Path.Combine("sub", "one.exe"));
        WriteWithSidecar(Path.Combine("sub", "deeper", "two.exe"));
    }

    private void WriteWithSidecar(string relativePath)
    {
        var path    = Path.Combine(_dir, relativePath);
        var dir     = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var content = System.Text.Encoding.UTF8.GetBytes(relativePath);
        File.WriteAllBytes(path, content);
        File.WriteAllText(path + ".sha256", Hash("SHA256", content));
    }

    private static async Task<(List<VerifyResult> Results, VerifySummary Summary)> RunAsync(
        string target, bool isFile, string ext = ".sha256", bool allTypes = false,
        int? maxDepth = null)
    {
        using var logger = new Logger();
        var verifier = new SidecarVerifier(target, isFile, ext, allTypes, logger, maxDepth);

        var results = new List<VerifyResult>();
        verifier.SidecarVerified += r => results.Add(r);

        var work    = await verifier.EnumerateAsync(CancellationToken.None);
        var summary = await verifier.VerifyAllAsync(work, new Progress<int>(_ => { }), CancellationToken.None);
        return (results, summary);
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static string Hash(string algorithm, byte[] data) => algorithm switch
    {
        "MD5"    => Convert.ToHexString(MD5.HashData(data)),
        "SHA1"   => Convert.ToHexString(SHA1.HashData(data)),
        "SHA512" => Convert.ToHexString(SHA512.HashData(data)),
        _        => Convert.ToHexString(SHA256.HashData(data))
    };
}
