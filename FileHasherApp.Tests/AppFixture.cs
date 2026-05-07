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

    public void Dispose()
    {
        try { _app.Close(); } catch { /* best-effort */ }
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
