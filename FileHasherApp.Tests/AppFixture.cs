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
    private readonly FlaUIApp       _app;
    private readonly UIA3Automation _automation;

    public Window MainWindow { get; }

    public AppFixture()
    {
        _automation = new UIA3Automation();
        _app        = FlaUIApp.Launch(FindExe());
        MainWindow  = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Waits for a non-modal top-level window of the app (e.g. the help
    /// window) to appear, matched by exact title. Returns null on timeout.
    /// </summary>
    public Window? WaitForTopLevelWindow(string title, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = _app.GetAllTopLevelWindows(_automation)
                            .FirstOrDefault(w => w.Title == title);
            if (match is not null) return match;
            Thread.Sleep(100);
        }
        return null;
    }

    /// <summary>Counts the app's current top-level windows with the given title.</summary>
    public int CountTopLevelWindows(string title) =>
        _app.GetAllTopLevelWindows(_automation).Count(w => w.Title == title);

    public void Dispose()
    {
        // Test fixture has no persistent state to flush — kill outright instead
        // of FlaUI's Close() (which logs "Application failed to exit" whenever
        // its internal wait times out, even if we'd kill the process anyway).
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
                    "net8.0-windows", "FileHasher.exe");
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
