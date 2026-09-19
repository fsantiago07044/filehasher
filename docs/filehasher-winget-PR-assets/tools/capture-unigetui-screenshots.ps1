# capture-unigetui-screenshots.ps1
#
# Regenerates the six UI screenshots UniGetUI serves beside the winget package
# (see docs/unigetui-screenshots.md).
#
# Run make-demo-data.ps1 IMMEDIATELY before this, not just at some point: the
# Store capture leaves an "added-later.txt" behind (it needs a file without a
# sidecar for its verify shot), and that name sorts early enough to become the
# file named in the sidecar-conflict dialog. A test artifact's name has no place
# in a public listing image.
#
# Different set from the Microsoft Store shots: these include the two dialogs
# and a File Explorer window, and they are captured at the window's own size
# rather than forced to a fixed one.
#
# Filenames must not change: the UniGetUI database entry references each by URL,
# so a rename needs a PR to their repo, while replacing a file in place does not.
param(
  [string]$Exe    = 'C:\Program Files\FileHasher\FileHasher.exe',
  [string]$OwnMsi = 'C:\Users\fabian\Documents\FileHasher-0.4.0.msi',
  [string]$Demo   = 'C:\Users\fabian\Documents\Release Artifacts',
  [string]$Out    = 'C:\Windows\Temp\ugui-shots'
)

$ErrorActionPreference = 'Continue'
$log = 'C:\Users\Public\ugui-shots.log'
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
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,int a,out RECT r,int s);
}
'@

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
Remove-Item $Out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Out | Out-Null

if (-not (Test-Path $Exe))  { L "FATAL: exe not found: $Exe";   exit 1 }
if (-not (Test-Path $Demo)) { L "FATAL: demo folder missing: $Demo (run make-demo-data.ps1)"; exit 1 }

# Wake the desktop; it blanks after idle and captures come back black.
[void][W]::SetCursorPos(800,500); Start-Sleep -Milliseconds 300

Get-Process FileHasher -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
# Launch via explorer.exe so the app gets the ordinary user token: this task
# runs elevated, and a direct child would title the window "[Administrator]",
# wrongly implying the app needs elevation.
Start-Process explorer.exe -ArgumentList ('"{0}"' -f $Exe)
$p = $null
foreach ($i in 1..30) {
  Start-Sleep 1
  $p = Get-Process FileHasher -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($p -and $p.MainWindowHandle -ne 0) { break }
}
if (-not $p) { L 'FATAL: app did not start'; exit 1 }
$p.Refresh(); $h = $p.MainWindowHandle
[void][W]::ShowWindow($h,9)
[void][W]::MoveWindow($h,60,40,1100,860,$true)
Start-Sleep 1
[void][W]::SetForegroundWindow($h)
Start-Sleep 1

$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$win  = $AE::RootElement.FindFirst($TS::Children, $cond)
if (-not $win) { L 'FATAL: main window not in UIA tree'; exit 1 }

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
  if (($t.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) -ne $on) { $t.Toggle() }
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
  $bmp.Save("$Out\$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L "SHOT $name = ${w}x${ht}"
}
# Owner-owned windows hang off the main window in the UIA tree, not the desktop.
function WaitWindow($nameLike, $timeout=90) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $wc = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,[System.Windows.Automation.ControlType]::Window)
  while ($sw.Elapsed.TotalSeconds -lt $timeout) {
    foreach ($d in $win.FindAll($TS::Descendants, $wc)) {
      if (-not $nameLike -or $d.Current.Name -like $nameLike) { L "window: $($d.Current.Name)"; return $d }
    }
    Start-Sleep -Milliseconds 400
  }
  L "WaitWindow timed out waiting for '$nameLike'"; return $null
}
function DialogHwnd($d){ [IntPtr]$d.Current.NativeWindowHandle }
function PressButton($dlg, $name) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
  $b = $dlg.FindFirst($TS::Descendants, $c)
  if (-not $b) { L "button '$name' not found"; return $false }
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  L "pressed '$name'"; Start-Sleep -Milliseconds 700; return $true
}

# Sidecars from an earlier run would turn shot 2 into a conflict dialog.
Get-ChildItem $Demo -Filter *.sha256 -ErrorAction SilentlyContinue | Remove-Item -Force
L "cleared stale sidecars"

# ── 1: the idle window ───────────────────────────────────────────────────────
SetVal 'PathBox' $Demo
Check 'AllTypesChk' $true
Shot 'main-ui'

# ── 2: a completed plain hash run ────────────────────────────────────────────
Click 'RunBtn'
$d = WaitWindow 'Complete'; if ($d) { [void](PressButton $d 'OK') }
Shot 'main-ui-completion-state-simple'

# ── 3: a completed inner-MSI scan ────────────────────────────────────────────
Click 'ClearBtn'
Check 'MsiChk' $true
SetVal 'PathBox' $OwnMsi
Click 'RunBtn'
$d = WaitWindow 'Complete'; if ($d) { [void](PressButton $d 'OK') }
Shot 'main-ui-completion-inner-msi-scan'

# ── 4 and 5: the sidecar conflict and the completion that follows ────────────
Click 'ClearBtn'
Check 'MsiChk' $false
SetVal 'PathBox' $Demo
Check 'SidecarChk' $true
Click 'RunBtn'                      # first pass writes the sidecars
$d = WaitWindow 'Complete'; if ($d) { [void](PressButton $d 'OK') }
Click 'ClearBtn'
Click 'RunBtn'                      # second pass hits every existing sidecar
$dlg = WaitWindow 'Sidecar Already Exists'
if ($dlg) {
  Shot 'sidecar-file-overwrite-dialog' (DialogHwnd $dlg)
  [void](PressButton $dlg 'Overwrite All')
} else { L 'WARN: conflict dialog never appeared' }
$done = WaitWindow 'Complete'
if ($done) {
  Shot 'completion-dialog-sidecars-overwritten' (DialogHwnd $done)
  [void](PressButton $done 'OK')
}

# ── 6: Explorer showing the sidecars beside their files ──────────────────────
Start-Process explorer.exe -ArgumentList $Demo
Start-Sleep 3
$shell = New-Object -ComObject Shell.Application
$ehwnd = [IntPtr]::Zero
foreach ($w in $shell.Windows()) {
  try { if ($w.LocationURL -and ($w.Document.Folder.Self.Path -eq $Demo)) { $ehwnd = [IntPtr]$w.HWND } } catch {}
}
if ($ehwnd -ne [IntPtr]::Zero) {
  [void][W]::MoveWindow($ehwnd,80,60,1020,660,$true)
  Start-Sleep 1
  [void][W]::SetForegroundWindow($ehwnd)
  Shot 'sidecar-file-explorer-view' $ehwnd
} else { L 'WARN: Explorer window not found' }

Get-ChildItem "$Out\*.png" | ForEach-Object { L ("file {0} {1}" -f $_.Name, $_.Length) }
L 'done'
