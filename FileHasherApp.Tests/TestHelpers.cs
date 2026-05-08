using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FileHasher.Tests;

internal static class TestHelpers
{
    // ── Modal windows ────────────────────────────────────────────────────────

    internal static Window WaitForModal(Window parent, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var m = parent.ModalWindows.FirstOrDefault();
            if (m is not null) return m;
            Thread.Sleep(100);
        }
        throw new TimeoutException($"No modal dialog appeared within {timeout}.");
    }

    /// <summary>Waits for all current modals to close, then waits for the next one to open.</summary>
    internal static Window WaitForNextModal(Window parent, TimeSpan totalTimeout)
    {
        var deadline = DateTime.UtcNow + totalTimeout;
        while (DateTime.UtcNow < deadline && parent.ModalWindows.Length > 0)
            Thread.Sleep(50);
        while (DateTime.UtcNow < deadline)
        {
            var m = parent.ModalWindows.FirstOrDefault();
            if (m is not null) return m;
            Thread.Sleep(100);
        }
        throw new TimeoutException($"No subsequent modal appeared within {totalTimeout}.");
    }

    // ── Button helpers ───────────────────────────────────────────────────────

    internal static void DismissFirstButton(Window dialog)
        => dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button))
                 .AsButton().Click();

    /// <summary>Finds a button in a dialog by exact accessible name (& accelerator markers stripped).</summary>
    internal static void ClickDialogButton(Window dialog, string name)
    {
        var buttons = dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
        var btn = buttons.FirstOrDefault(b =>
                string.Equals(b.Name.Replace("&", ""), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Button '{name}' not found in dialog '{dialog.Title}'. " +
                $"Available: [{string.Join(", ", buttons.Select(b => b.Name))}]");
        // Invoke (not Click) for dialog buttons: mouse-simulation coords can miss native
        // Win32 TaskDialog custom buttons, while IInvokePattern is coordinate-independent.
        btn.AsButton().Invoke();
    }

    // ── Polling helpers ──────────────────────────────────────────────────────

    internal static bool WaitUntilEnabled(AutomationElement el, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (el.IsEnabled) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    internal static bool WaitUntilDisabled(AutomationElement el, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!el.IsEnabled) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    // Polls until the named radio button reports IsChecked == true.
    // Use after .Click() to avoid reading stale UIAutomation state.
    internal static bool WaitUntilRadioChecked(Window win, string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var el = win.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (el?.AsRadioButton()?.IsChecked == true) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    // Polls until the named radio button reports IsChecked != true (false or null).
    internal static bool WaitUntilRadioUnchecked(Window win, string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var el = win.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (el?.AsRadioButton()?.IsChecked != true) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    internal static string GetStatusText(Window win)
        => win.FindFirstDescendant(cf => cf.ByAutomationId("StatusLabel"))?.Name ?? string.Empty;

    internal static bool WaitUntilStatusContains(Window win, string text, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (GetStatusText(win).Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    // ── Results list ─────────────────────────────────────────────────────────

    internal static int GetResultsRowCount(Window win)
    {
        var list = win.FindFirstDescendant(cf => cf.ByAutomationId("ResultsView"));
        return list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)).Length;
    }

    // ── Common run sequence ──────────────────────────────────────────────────

    /// <summary>Sets the path box, clicks Run, waits for the completion dialog, dismisses it.</summary>
    internal static void RunHashOnFile(Window win, string filePath)
    {
        win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = filePath;
        win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton().Click();
        DismissFirstButton(WaitForModal(win, TimeSpan.FromSeconds(30)));
    }

    // ── Temp file / folder factories ─────────────────────────────────────────

    internal static string CreateTempFile(byte[] content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, content);
        return path;
    }

    internal static string CreateTempFolder(params string[] fileNames)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        foreach (var name in fileNames)
            File.WriteAllText(Path.Combine(dir, name), $"test content for {name}");
        return dir;
    }
}
