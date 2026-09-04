$ErrorActionPreference = 'Continue'
$log = 'C:\Windows\Temp\shots.log'
function L($m){ Add-Content $log ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) }
Set-Content $log "=== run $(Get-Date -Format o) ==="

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public class W {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int t,bool r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,IntPtr e);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,int a,out RECT r,int s);
}
'@

$AE   = [System.Windows.Automation.AutomationElement]
$TS   = [System.Windows.Automation.TreeScope]
$OUT  = 'C:\Windows\Temp\shots'
$demo = 'C:\Users\fabian\Documents\Release Artifacts'
Remove-Item $OUT -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $OUT | Out-Null

# Wake the desktop; the screen blanks after idle and captures come back black.
[void][W]::SetCursorPos(800,500); Start-Sleep -Milliseconds 300

Get-Process FileHasher -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
# This task runs elevated, and a child process would inherit that: the title bar
# would read "FileHasher [Administrator]", wrongly implying the app needs
# elevation. Launching via explorer.exe hands the request to the shell, which
# starts it with the ordinary user token.
Start-Process explorer.exe -ArgumentList '"C:\Program Files\FileHasher\FileHasher.exe"'
$p = $null
foreach ($i in 1..30) {
  Start-Sleep 1
  $p = Get-Process FileHasher -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($p -and $p.MainWindowHandle -ne 0) { break }
}
if (-not $p) { L 'FATAL: app did not start'; exit 1 }
$p.Refresh(); $h = $p.MainWindowHandle
L "pid=$($p.Id) hwnd=$h"
[void][W]::ShowWindow($h,9)
[void][W]::MoveWindow($h,40,20,1500,900,$true)
Start-Sleep 1
[void][W]::SetForegroundWindow($h)
Start-Sleep 1

$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$win  = $AE::RootElement.FindFirst($TS::Children, $cond)
if (-not $win) { L 'FATAL: main window not found in UIA tree'; exit 1 }
L "window: $($win.Current.Name)"

function E($id, $scope) {
  if (-not $scope) { $scope = $win }
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  $scope.FindFirst($TS::Descendants, $c)
}
function SetVal($id,$v){ (E $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v); L "set $id" }
function Click($id){ (E $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); L "click $id" }
function Check($id,[bool]$on){
  $e = E $id
  $t = $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
  $is = $t.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
  if ($is -ne $on) { $t.Toggle() }
  L "check $id = $on"
}
function Shot($name, $hwnd) {
  if (-not $hwnd) { $hwnd = $h }
  Start-Sleep -Milliseconds 900
  $r = New-Object W+RECT
  if ([W]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16) -ne 0) { [void][W]::GetWindowRect($hwnd,[ref]$r) }
  $w = $r.R - $r.L; $ht = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap($w, $ht)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w,$ht)))
  $bmp.Save("$OUT\$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L "SHOT $name = ${w}x${ht}"
}
function WaitDone($timeout=90) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $winCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::Window)
  while ($sw.Elapsed.TotalSeconds -lt $timeout) {
    # The completion dialog is owner-owned, so it hangs off the main window in
    # the UIA tree rather than off the desktop. Its title depends on the run
    # type ("Complete" after hashing, "Verification complete" after a verify),
    # so match on it being a window at all, not on its name.
    $dlg = $win.FindFirst($TS::Descendants, $winCond)
    if ($dlg) { L "dialog appeared: $($dlg.Current.Name)"; return $dlg }
    Start-Sleep -Milliseconds 400
  }
  L 'WaitDone timed out'; return $null
}

function DismissDialog($dlg){
  if (-not $dlg) { L 'no dialog found to dismiss'; return }
  $hw = [IntPtr]$dlg.Current.NativeWindowHandle
  try {
    $b = $dlg.FindFirst($TS::Descendants,
          (New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)),
            (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty,'OK')))))
    $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    L 'dialog dismissed via OK'
  } catch {
    # The MessageBox OK button does not always expose InvokePattern; Enter on
    # the focused dialog is the same thing a user would do.
    if ($hw -ne [IntPtr]::Zero) { [void][W]::SetForegroundWindow($hw); Start-Sleep -Milliseconds 250 }
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    L 'dialog dismissed via ENTER'
  }
  Start-Sleep -Milliseconds 800
  [void][W]::SetForegroundWindow($h)
  Start-Sleep -Milliseconds 500
}

try {
  # 1: idle, target chosen
  SetVal 'PathBox' $demo
  Check 'AllTypesChk' $true
  Shot '01-main-window'

  # 2: a completed SHA256 run, writing sidecars
  Check 'SidecarChk' $true
  Click 'RunBtn'
  DismissDialog (WaitDone)
  Shot '02-hash-run'
} catch { L "ERR phase1: $_" }

try {
  # set up a verification with mixed verdicts
  Add-Content (Join-Path $demo 'release-notes.txt') 'edited after hashing, so this file now mismatches'
  Remove-Item (Join-Path $demo 'payload.bin') -Force              # sidecar remains -> MISSING FILE
  Set-Content (Join-Path $demo 'added-later.txt') 'created after the sidecars were written' -Encoding UTF8
  L 'verify fixtures staged'

  Click 'ClearBtn'
  Click 'VerifyBtn'
  DismissDialog (WaitDone)
  Shot '03-verify-sidecars'
} catch { L "ERR phase2: $_" }

try {
  # 4: results right-click menu
  $rv = E 'ResultsView'
  $rows = $rv.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)))
  # Pick a row with a hash: on a NO SIDECAR row "Copy Hash" is disabled, which
  # reads like a broken menu in a Store screenshot.
  $item = $rows.Item([Math]::Min(4, $rows.Count - 1))
  $b = $item.Current.BoundingRectangle
  $x = [int]($b.X + 120); $y = [int]($b.Y + $b.Height/2)
  [void][W]::SetForegroundWindow($h); Start-Sleep -Milliseconds 600
  [void][W]::SetCursorPos($x,$y); Start-Sleep -Milliseconds 400
  [void][W]::mouse_event(0x08,0,0,0,[IntPtr]::Zero)   # RIGHTDOWN
  [void][W]::mouse_event(0x10,0,0,0,[IntPtr]::Zero)   # RIGHTUP
  L "right-click at $x,$y"
  Shot '04-context-menu'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  Start-Sleep -Milliseconds 400
} catch { L "ERR phase3: $_" }

try {
  # 5: hashing the files inside an MSI
  Click 'ClearBtn'
  Check 'SidecarChk' $false
  Check 'MsiChk' $true
  SetVal 'PathBox' 'C:\Users\fabian\Documents\FileHasher-0.3.1.msi'
  Click 'RunBtn'
  DismissDialog (WaitDone)
  Shot '05-inner-msi-scan'
} catch { L "ERR phase4: $_" }

try {
  # 6: the help window
  [void][W]::SetForegroundWindow($h); Start-Sleep -Milliseconds 400
  [System.Windows.Forms.SendKeys]::SendWait('{F1}')
  Start-Sleep 2
  $hf = E 'HelpForm'
  if (-not $hf) {
    L 'F1 did not open help; driving the Help menu instead'
    try {
      $menu = $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty,'Help')))
      $menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
      Start-Sleep 1
      $mi = E 'MiHelpContents'
      $mi.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      Start-Sleep 2
      $hf = E 'HelpForm'
    } catch { L "menu route failed: $_" }
  }
  if ($hf) {
    $hh = [IntPtr]$hf.Current.NativeWindowHandle
    L "helpform hwnd=$hh"
    [void][W]::MoveWindow($hh,60,30,1480,900,$true)
    [void][W]::SetForegroundWindow($hh)
    try {
      $topic = $hf.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty,'Verifying Sidecars')))
      $topic.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
      L 'selected help topic: Verifying Sidecars'
      Start-Sleep 1
    } catch { L "topic select failed: $_" }
    Shot '06-help-window' $hh
  } else { L 'help window not found' }
} catch { L "ERR phase5: $_" }

L 'done'
Get-ChildItem $OUT | ForEach-Object { L ("file " + $_.Name + " " + $_.Length) }
