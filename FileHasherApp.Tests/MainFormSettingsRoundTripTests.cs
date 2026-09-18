using System.Reflection;
using System.Runtime.ExceptionServices;
using Xunit;

namespace FileHasher.Tests;

/// <summary>
/// Proves the controls actually round-trip through <see cref="AppSettings"/>.
///
/// <see cref="SettingsStoreTests"/> covers the file: serialising, validating and
/// surviving corruption. What it cannot see is the wiring, whether
/// MainForm.ApplySettings pushes each value into the right control and
/// CaptureSettings reads it back. A typo swapping two fields there would leave
/// every store test green while silently restoring the wrong thing.
///
/// These construct MainForm in-process rather than driving the UI, so they need
/// an STA thread but no visible window and no FlaUI.
/// </summary>
public sealed class MainFormSettingsRoundTripTests
{
    /// <summary>WinForms controls require STA; xUnit threads are MTA.</summary>
    private static void OnStaThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static void WithForm(Action<MainForm> body) =>
        OnStaThread(() =>
        {
            using var form = new MainForm();
            body(form);
        });

    [Theory]
    [InlineData("MD5")]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("SHA512")]
    public void Algorithm_SurvivesApplyThenCapture(string algorithm)
    {
        WithForm(form =>
        {
            form.ApplySettings(new AppSettings { Algorithm = algorithm });

            Assert.Equal(algorithm, form.CaptureSettings().Algorithm);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true,  false)]
    [InlineData(false, true)]
    [InlineData(true,  true)]
    public void Checkboxes_SurviveApplyThenCapture(bool metadata, bool descendIntoMsi)
    {
        WithForm(form =>
        {
            form.ApplySettings(new AppSettings
            {
                IncludeMetadata = metadata,
                DescendIntoMsi  = descendIntoMsi
            });

            var captured = form.CaptureSettings();

            Assert.Equal(metadata,       captured.IncludeMetadata);
            Assert.Equal(descendIntoMsi, captured.DescendIntoMsi);
        });
    }

    [Fact]
    public void UnknownAlgorithm_LandsOnTheDefaultRatherThanNoSelection()
    {
        // Radio buttons have no "none" state to fall into, so a bad stored
        // value must select SHA256 rather than leaving the group empty.
        WithForm(form =>
        {
            form.ApplySettings(new AppSettings { Algorithm = "ROT13" }.Sanitised());

            Assert.Equal("SHA256", form.CaptureSettings().Algorithm);
        });
    }

    [Fact]
    public void FullCycle_ThroughTheFileOnDisk()
    {
        var dir  = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var file = Path.Combine(dir, "settings.json");
        try
        {
            var original = new AppSettings
            {
                Algorithm       = "SHA512",
                IncludeMetadata = true,
                DescendIntoMsi  = true
            };

            WithForm(form =>
            {
                form.ApplySettings(original);
                Assert.True(SettingsStore.Save(form.CaptureSettings(), file));
            });

            WithForm(form =>
            {
                form.ApplySettings(SettingsStore.Load(file));
                var captured = form.CaptureSettings();

                Assert.Equal(original.Algorithm,       captured.Algorithm);
                Assert.Equal(original.IncludeMetadata, captured.IncludeMetadata);
                Assert.Equal(original.DescendIntoMsi,  captured.DescendIntoMsi);
            });
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void PersistedFieldSet_IsExactlyTheControlsTheUiNeverDisables()
    {
        // The rule this encodes (settled 2026-09-18 after three rounds of
        // getting it wrong): if a control's enabled state depends on the
        // target, its value is not a standing preference and must not be
        // persisted. Everything named here is a control MainForm never
        // disables. Adding a field for the target path, scan-all-file-types,
        // the Subfolders depth, or anything gated behind the sidecar or CSV
        // checkboxes should fail this test rather than ship.
        var expected = new[]
        {
            nameof(AppSettings.SchemaVersion),
            nameof(AppSettings.Algorithm),
            nameof(AppSettings.IncludeMetadata),
            nameof(AppSettings.DescendIntoMsi)
        };

        var actual = typeof(AppSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(expected.OrderBy(n => n).ToArray(), actual);
    }
}
