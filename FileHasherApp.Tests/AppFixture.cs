using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;

using FlaUIApp = FlaUI.Core.Application;

namespace FileHasher.Tests;

/// <summary>
/// Launches a fresh FileHasher.exe instance and tears it down after the owning
/// test class completes.  Each test class that needs UI interaction should either
/// accept this as IClassFixture (shared instance, read-only tests) or instantiate
/// it directly in the constructor (one process per test method).
/// </summary>
public sealed class AppFixture : IDisposable
{
    /// <summary>
    /// Points every app instance this suite launches at a throwaway settings
    /// file instead of the real %APPDATA% one. Without this the UI tests read
    /// whatever the developer last left in the app: the assertions about
    /// default state (SHA256 selected, options unticked) would then pass or
    /// fail according to that person's saved preferences rather than the code.
    ///
    /// Set once per test process and inherited by the launched app. The path is
    /// never written in practice, because Dispose kills the app rather than
    /// closing it, so OnFormClosing never runs; the override guards the READ
    /// side, and the file is cleaned up if a save ever does happen.
    /// </summary>
    private static readonly string SettingsOverride = InitSettingsOverride();

    private static string InitSettingsOverride()
    {
        // Respect one already set, so a developer can point a debugging run at
        // a specific file.
        var existing = Environment.GetEnvironmentVariable("FILEHASHER_SETTINGS");
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        var path = Path.Combine(Path.GetTempPath(),
                                $"filehasher-tests-{Guid.NewGuid():N}", "settings.json");
        Environment.SetEnvironmentVariable("FILEHASHER_SETTINGS", path);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch { /* best-effort */ }
        };
        return path;
    }

    private readonly FlaUIApp       _app;
    private readonly UIA3Automation _automation;

    public Window MainWindow { get; }

    public AppFixture()
    {
        _ = SettingsOverride;   // force the static initialiser before launching
        _automation = new UIA3Automation();
        _app        = FlaUIApp.Launch(FindExe());
        MainWindow  = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Finds the app's secondary windows (e.g. the help window) by exact
    /// title or AutomationId. Two places must be scanned, mirroring the
    /// lesson baked into TestHelpers.GetOpenContextMenu: desktop children by
    /// process id, AND the main window's own UIA subtree, because UIA parents
    /// owner-owned windows (Form.Show(owner)) under the owner rather than the
    /// desktop. Results are de-duplicated by element identity.
    /// </summary>
    private List<AutomationElement> FindAppWindows(string title, string automationId)
    {
        var found = new List<AutomationElement>();

        void Add(AutomationElement? el)
        {
            if (el is null) return;
            if (!found.Any(existing => existing.Equals(el)))
                found.Add(el);
        }

        foreach (var el in _automation.GetDesktop()
                     .FindAllChildren(cf => cf.ByProcessId(_app.ProcessId)))
        {
            try
            {
                if (el.Name == title || el.AutomationId == automationId)
                    Add(el);
            }
            catch { /* window can vanish or refuse properties mid-scan */ }
        }

        try
        {
            Add(MainWindow.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window).And(cf.ByName(title))));
            Add(MainWindow.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window)
                  .And(cf.ByAutomationId(automationId))));
        }
        catch { /* subtree can churn while a window is opening */ }

        return found;
    }

    /// <summary>Waits for a secondary window to appear. Returns null on timeout.</summary>
    public Window? WaitForTopLevelWindow(string title, string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var found = FindAppWindows(title, automationId);
            if (found.Count > 0) return found[0].AsWindow();
            Thread.Sleep(100);
        }
        return null;
    }

    /// <summary>Counts the app's current secondary windows matching the given
    /// title or AutomationId.</summary>
    public int CountTopLevelWindows(string title, string automationId) =>
        FindAppWindows(title, automationId).Count;

    public void Dispose()
    {
        // Kill outright instead of FlaUI's Close() (which logs "Application
        // failed to exit" whenever its internal wait times out, even if we'd
        // kill the process anyway).
        //
        // Since the app gained persisted settings this also keeps tests
        // isolated from each other, because Kill skips OnFormClosing and so
        // skips the save: one test selecting SHA512 cannot change what the next
        // test's launch sees. If this is ever changed to a graceful close, the
        // FILEHASHER_SETTINGS override above becomes the only thing standing
        // between the suite and order-dependent failures.
        try
        {
            if (!_app.HasExited) _app.Kill();
        }
        catch { /* best-effort */ }
        _automation.Dispose();
    }

    internal static string FindExe()
    {
        var fromEnv = Environment.GetEnvironmentVariable("FILEHASHER_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        // Walk up from the test assembly output to locate the app build alongside it
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    dir.FullName, "FileHasherApp", "bin", config,
                    "net10.0-windows", "FileHasher.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "FileHasher.exe not found. Build FileHasherApp first, or set the " +
            "FILEHASHER_EXE environment variable to the full path of the executable.");
    }
}

/// <summary>
/// Marks test classes that must not run in parallel with each other, since they
/// share the Windows desktop and interact with real application windows.
/// </summary>
[CollectionDefinition("Serial", DisableParallelization = true)]
public sealed class SerialCollection { }
