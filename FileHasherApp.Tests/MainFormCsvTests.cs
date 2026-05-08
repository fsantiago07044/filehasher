using System.Security.Cryptography;
using FlaUI.Core.AutomationElements;
using Xunit;

namespace FileHasher.Tests;

/// <summary>CSV export: file creation, header/data content, metadata columns, missing-path validation.</summary>
[Collection("Serial")]
public sealed class MainFormCsvTests : IDisposable
{
    private readonly AppFixture _fixture;
    private Window Win => _fixture.MainWindow;

    public MainFormCsvTests() => _fixture = new AppFixture();
    public void Dispose()     => _fixture.Dispose();

    [Fact]
    public void CsvMissingPath_ShowsValidationWarning()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 1 });
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = tmp;
            Win.FindFirstDescendant(cf => cf.ByAutomationId("CsvChk")).AsCheckBox().Toggle();
            // Intentionally leave CsvPathBox empty
            Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();

            var dialog = TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(5));
            Assert.NotNull(dialog);
            TestHelpers.DismissFirstButton(dialog);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CsvExport_CreatesFile()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 10, 20 });
        var csv = TempCsvPath();
        try
        {
            SetupAndRun(tmp, csv);
            Assert.True(File.Exists(csv), "CSV file was not created.");
        }
        finally { Cleanup(tmp, csv); }
    }

    [Fact]
    public void CsvExport_ContainsHeaderRow()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 1, 2 });
        var csv = TempCsvPath();
        try
        {
            SetupAndRun(tmp, csv);
            var header = File.ReadLines(csv).First();
            Assert.StartsWith("Path,SHA256", header);
        }
        finally { Cleanup(tmp, csv); }
    }

    [Fact]
    public void CsvExport_DataRowContainsCorrectHashAndPath()
    {
        var content = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var tmp     = TestHelpers.CreateTempFile(content);
        var csv     = TempCsvPath();
        var expectedHash = Convert.ToHexString(SHA256.HashData(content));
        try
        {
            SetupAndRun(tmp, csv);
            var lines = File.ReadAllLines(csv);
            Assert.True(lines.Length >= 2, "Expected header + at least one data row.");

            var dataRow = lines[1];
            Assert.Contains(expectedHash,  dataRow, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.GetFileName(tmp), dataRow, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(tmp, csv); }
    }

    [Fact]
    public void CsvExport_WithMetadata_HeaderContainsMetadataColumns()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 5, 6 });
        var csv = TempCsvPath();
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("MetadataChk")).AsCheckBox().Toggle();
            SetupAndRun(tmp, csv);

            var header = File.ReadLines(csv).First();
            Assert.Contains("LengthBytes",  header, StringComparison.Ordinal);
            Assert.Contains("LastWriteUtc", header, StringComparison.Ordinal);
        }
        finally { Cleanup(tmp, csv); }
    }

    [Fact]
    public void CsvExport_WithMetadata_DataRowContainsSizeAndDate()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 1, 2, 3, 4, 5 });
        var csv = TempCsvPath();
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("MetadataChk")).AsCheckBox().Toggle();
            SetupAndRun(tmp, csv);

            var lines   = File.ReadAllLines(csv);
            var dataRow = lines[1];
            // Size should be "5" (5 bytes), date should contain a year
            Assert.Contains("5",    dataRow, StringComparison.Ordinal);
            Assert.Contains("202",  dataRow, StringComparison.Ordinal); // year 202x
        }
        finally { Cleanup(tmp, csv); }
    }

    [Fact]
    public void CsvExport_Algorithm_HeaderReflectsSelectedAlgorithm()
    {
        var tmp = TestHelpers.CreateTempFile(new byte[] { 7 });
        var csv = TempCsvPath();
        try
        {
            Win.FindFirstDescendant(cf => cf.ByAutomationId("AlgoMd5")).AsRadioButton().Click();
            SetupAndRun(tmp, csv);

            var header = File.ReadLines(csv).First();
            Assert.Contains("MD5", header, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(tmp, csv); }
    }

    [Fact]
    public void CsvExport_OnlySuccessfulRowsIncluded()
    {
        // Hash one valid file — errors are excluded from CSV; at least the valid row should be present
        var tmp = TestHelpers.CreateTempFile(new byte[] { 99 });
        var csv = TempCsvPath();
        try
        {
            SetupAndRun(tmp, csv);
            var lines = File.ReadAllLines(csv);
            // header + 1 data row
            Assert.Equal(2, lines.Length);
        }
        finally { Cleanup(tmp, csv); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void SetupAndRun(string filePath, string csvPath)
    {
        Win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = filePath;
        var chk = Win.FindFirstDescendant(cf => cf.ByAutomationId("CsvChk")).AsCheckBox();
        if (chk.IsChecked != true) chk.Toggle();
        Win.FindFirstDescendant(cf => cf.ByAutomationId("CsvPathBox")).AsTextBox().Text = csvPath;
        Win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();
        TestHelpers.DismissFirstButton(TestHelpers.WaitForModal(Win, TimeSpan.FromSeconds(25)));
    }

    private static string TempCsvPath()
        => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");

    private static void Cleanup(string file, string csv)
    {
        if (File.Exists(file)) File.Delete(file);
        if (File.Exists(csv))  File.Delete(csv);
    }
}
