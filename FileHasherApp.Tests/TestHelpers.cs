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
    {
        // Same UIA-tree-population race as ClickDialogButton below: the dialog
        // window can be returned by ModalWindows before its children are
        // enumerable, in which case FindFirstDescendant returns null and the
        // subsequent .AsButton().Click() throws NullReferenceException. Poll
        // until at least one button materializes, with a short upper bound so
        // a genuinely button-less dialog still fails in bounded time.
        AutomationElement? btn = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            btn = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button));
            if (btn is not null) break;
            Thread.Sleep(50);
        }

        if (btn is null)
            throw new InvalidOperationException(
                $"No button found in dialog '{dialog.Title}' after polling for 3 seconds.");

        btn.AsButton().Click();
    }

    /// <summary>Finds a button in a dialog by exact accessible name (& accelerator markers stripped).</summary>
    internal static void ClickDialogButton(Window dialog, string name)
    {
        // UIA timing: a dialog window can appear in the parent's ModalWindows
        // collection before all of its child buttons have materialized in the
        // UIAutomation tree. Buttons can also surface in stages — e.g. a
        // TaskDialog with four custom buttons might present #1 and #3 first,
        // then fill in #2 and #4 within tens of milliseconds. Polling once
        // and looking by name would miss the target if the target is in the
        // later batch. Poll for the SPECIFIC button by name (not just any
        // button); on each iteration re-enumerate so newly-materialized
        // buttons enter the candidate set. Fall through to a clear error
        // (showing what WAS observed at the last enumeration) if the named
        // button never appears.
        AutomationElement? btn = null;
        AutomationElement[] lastSeen = Array.Empty<AutomationElement>();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            lastSeen = dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
            btn = lastSeen.FirstOrDefault(b =>
                string.Equals(b.Name.Replace("&", ""), name, StringComparison.OrdinalIgnoreCase));
            if (btn is not null) break;
            Thread.Sleep(50);
        }

        if (btn is null)
            throw new InvalidOperationException(
                $"Button '{name}' not found in dialog '{dialog.Title}' after polling for 3 seconds. " +
                $"Last-observed buttons: [{string.Join(", ", lastSeen.Select(b => b.Name))}]");

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

    /// <summary>
    /// Clicks Run, waits for the completion modal, returns it WITHOUT dismissing
    /// so the caller can assert on Title or other contents before closing.
    /// Retries the click once if neither run-started signal (button disabled OR
    /// modal up) appears within 3s — the first click sometimes silently misses
    /// on a freshly-launched app (focus/timing race), and re-clicking is the
    /// only recovery.
    /// </summary>
    internal static Window ClickRunAndReturnModal(Window win, TimeSpan modalTimeout)
    {
        var runBtn = win.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton();
        runBtn.Click();

        bool runStarted   = false;
        var  startDeadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < startDeadline)
        {
            if (!runBtn.IsEnabled || win.ModalWindows.Length > 0)
            {
                runStarted = true;
                break;
            }
            Thread.Sleep(100);
        }
        if (!runStarted) runBtn.Click();

        return WaitForModal(win, modalTimeout);
    }

    /// <summary>
    /// Convenience for the common case: clicks Run, waits for the completion
    /// modal, and dismisses it. Internally calls <see cref="ClickRunAndReturnModal"/>
    /// so the retry-click race protection lives in one place.
    /// </summary>
    internal static void ClickRunAndWaitForModal(Window win, TimeSpan modalTimeout)
        => DismissFirstButton(ClickRunAndReturnModal(win, modalTimeout));

    /// <summary>Sets the path box, clicks Run, waits for the completion dialog, dismisses it.</summary>
    internal static void RunHashOnFile(Window win, string filePath)
    {
        win.FindFirstDescendant(cf => cf.ByAutomationId("PathBox")).AsTextBox().Text = filePath;
        ClickRunAndWaitForModal(win, TimeSpan.FromSeconds(30));
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
