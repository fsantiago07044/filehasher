# FileHasher

A simple utility to hash files and folders, write sidecar hash files, and export results to CSV. Available as a PowerShell script and a compiled Windows GUI application.

---

## Contents

- [Windows GUI Application](#windows-gui-application)
  - [Requirements](#requirements)
  - [Building](#building)
  - [Usage](#usage)
    - [Selecting a target](#selecting-a-target)
    - [Choosing a hash algorithm](#choosing-a-hash-algorithm)
    - [Options](#options)
    - [Running and stopping](#running-and-stopping)
    - [Results](#results)
    - [Logs](#logs)
    - [UAC elevation](#uac-elevation)
    - [About dialog](#about-dialog)
- [Automated Testing](#automated-testing)
  - [Test project overview](#test-project-overview)
  - [Running the tests](#running-the-tests)
  - [AutomationId reference](#automationid-reference)
  - [Test classes](#test-classes)
  - [CI / custom exe path](#ci--custom-exe-path)
- [PowerShell Script](#powershell-script)
  - [Parameters](#parameters)
  - [Examples](#examples)

---

## Windows GUI Application

A self-contained, portable 64-bit Windows executable built on .NET 8 and WinForms. No installer or runtime dependency required.

### Requirements

- Windows 10 or Windows 11 (64-bit)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — only needed to **build**; the published `.exe` is fully self-contained

### Building

From a Windows machine with the .NET 8 SDK installed, open a terminal in the `FileHasherApp` folder and run:

```powershell
# Self-contained single-file executable (recommended)
dotnet publish -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded
```

Output: `bin\Release\net8.0-windows\win-x64\publish\FileHasher.exe`

Copy `FileHasher.exe` anywhere you like — it has no external dependencies.

---

### Usage

#### Selecting a target

| Button | Behaviour |
| --- | --- |
| **Browse File…** | Opens a file picker. The selected file is hashed regardless of its extension. |
| **Browse Folder…** | Opens a folder picker. The folder is scanned **recursively**. |

You can also type or paste a path directly into the path box, or **drag and drop** a file or folder onto it. Dropping a folder automatically enables the **Scan all file types** checkbox; dropping a file disables it (matching the Browse buttons' behaviour).

**Scan all file types** — when scanning a folder, this checkbox controls which files are included:

- **Unchecked** (default) — only `.exe` and `.msi` files are hashed, matching the behaviour of the PowerShell script.
- **Checked** — every file in the folder tree is hashed.

This option is not applicable when a single file is selected.

---

#### Choosing a hash algorithm

Select one of the four algorithms using the radio buttons in the **Hash Algorithm** group. SHA256 is selected by default.

| Algorithm | Notes |
| --- | --- |
| MD5 | Fast; not collision-resistant — avoid for security-critical use |
| SHA1 | Legacy; deprecated for most security purposes |
| **SHA256** | **Default.** Recommended for general use. |
| SHA512 | Strongest option; produces a longer hash |

The column header in the results list and the CSV export header update automatically to reflect the selected algorithm.

---

#### Options

**Include file metadata**
When checked, two additional columns are populated in the results list and CSV export: **Size (bytes)** and **Modified (UTC)**.

---

**Write sidecar hash files**
Writes a small text file next to each hashed file. For example, hashing `setup.exe` produces `setup.exe.sha256` alongside it.

- **Extension** — the suffix appended to the original filename. Default: `.sha256`. Change this to match the algorithm you are using (e.g. `.md5`, `.sha512`) or any custom value.
- **Format** — controls the content of the sidecar file:
  - **sha256sum format** (default) — `HASH *filename`, compatible with standard `sha256sum`/`sha512sum` tools.
  - **Hash only** — the raw hash string with no filename, useful for simple verification scripts.

Writing sidecars to protected locations (e.g. `C:\Program Files`) requires Administrator rights — see [UAC elevation](#uac-elevation).

##### Sidecar conflict handling

When a sidecar file already exists for a target file, FileHasher pauses before hashing begins and shows a per-file conflict dialog. The dialog displays:

- **File** — full path of the source file
- **Size** — file size in bytes
- **Modified** — last-modified timestamp (UTC)
- **Existing sidecar** — name of the sidecar that is already on disk
- **Sidecar written** — timestamp when the existing sidecar was last written

Four buttons let you choose what to do:

| Button | Behaviour |
| --- | --- |
| **Overwrite** | Replace the existing sidecar for this file only. |
| **Overwrite All** | Replace existing sidecars for all remaining conflicts without further prompts. |
| **Skip** | Leave the existing sidecar for this file; the file is excluded from hashing. |
| **Skip All** | Leave existing sidecars for all remaining conflicts; those files are excluded from hashing. |

All conflict decisions are collected **before** any file is hashed, so no file is touched until every dialog has been answered.

The completion summary (see [Running and stopping](#running-and-stopping)) includes **Sidecars skipped** and **Sidecars overwritten** counts when at least one conflict was resolved.

Sidecar files are never themselves treated as hash targets — FileHasher automatically excludes any file whose path ends with the configured sidecar extension, preventing `.sha256.sha256` chains on repeated runs.

---

**Export results to CSV**
When checked, a CSV file is written after all files have been hashed. Click **Browse…** to choose the output path, or type one directly.

- Encoding: UTF-8 with BOM (opens correctly in Microsoft Excel without an import wizard).
- Only successfully hashed files are included; errors are omitted.
- Rows are sorted by file path.
- Columns: `Path`, `<Algorithm>`, and (if metadata is enabled) `LengthBytes`, `LastWriteUtc`.

---

#### Running and stopping

| Control | Behaviour |
| --- | --- |
| **▶ Run** | Starts enumeration then hashing. Most controls are disabled while a run is in progress. |
| **Stop** | Cancels the current run cleanly. Files already hashed are retained in the results list. |
| **Clear Results** | Clears all rows from the results list, resets the progress bar, and returns the status to "Ready." Available before and after a run. |

The progress bar shows indeterminate (marquee) progress during folder enumeration, then switches to a percentage bar during hashing. When hashing completes successfully the bar turns **blue**.

A completion dialog is shown at the end of every run summarising:

- **Files hashed** — count of successfully hashed files
- **Errors** — count of files that failed
- **Sidecars skipped** — shown only when at least one existing sidecar was left in place
- **Sidecars overwritten** — shown only when at least one existing sidecar was replaced
- **Log** — path to the current log file

---

#### Results

The results list shows one row per file:

| Column | Content |
| --- | --- |
| File Path | Full absolute path |
| `<Algorithm>` | Uppercase hex hash, or an error message in red if hashing failed |
| Size (bytes) | Populated when **Include file metadata** is checked |
| Modified (UTC) | Populated when **Include file metadata** is checked |

Warnings (e.g. inaccessible subdirectories) appear as orange rows.

---

#### Logs

Every run is automatically logged. Log files are written to:

```text
%APPDATA%\FileHasher\Logs\FileHasher_YYYY-MM-DD.log
```

Each log entry records the timestamp (UTC), algorithm, result (`OK` or `ERROR`), hash, file path, and (when metadata is enabled) file size and last modified date. A session header and footer are written at the start and end of each run.

Click the path shown in the status bar at the bottom of the window to open the log folder in Explorer.

---

#### UAC elevation

FileHasher starts as a standard user by default. Elevation is needed in two situations:

- **Reading files** in protected locations (e.g. `C:\Windows\System32`).
- **Writing sidecar files** next to installers in protected locations (e.g. `C:\Program Files`).

To elevate:

- Click **Run as Administrator…** before starting a run. The application will prompt for UAC confirmation and relaunch with Administrator privileges. The title bar and status bar will confirm elevated status.
- If a run encounters an access-denied error mid-way, a dialog will offer to relaunch as Administrator automatically.

When already running as Administrator, the button is disabled and the status bar shows **● Administrator**.

---

#### About dialog

Open **Help → About FileHasher…** from the menu bar to view the application version, author, and copyright information.

---

## Automated Testing

FileHasher's GUI is covered by a UI automation test suite built on [FlaUI](https://github.com/FlaUI/FlaUI) and [xUnit](https://xunit.net/). The tests launch the real `FileHasher.exe`, interact with it through the Windows UI Automation (UIA3) accessibility tree, and assert on observable behaviour — no mocking.

### Test project overview

| Item | Value |
| --- | --- |
| Project | `FileHasherApp.Tests\FileHasherApp.Tests.csproj` |
| Target framework | `net8.0-windows` |
| Test runner | xUnit 2.8 |
| Automation library | FlaUI.UIA3 4.0 |

Key packages:

```xml
<PackageReference Include="FlaUI.Core"                Version="4.0.0" />
<PackageReference Include="FlaUI.UIA3"                Version="4.0.0" />
<PackageReference Include="xunit"                     Version="2.8.1" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.1" />
```

---

### Running the tests

Build `FileHasherApp` first (the tests locate the compiled executable automatically):

```powershell
# From the solution root
dotnet build FileHasherApp\FileHasherApp.csproj -c Debug

dotnet test FileHasherApp.Tests\FileHasherApp.Tests.csproj
```

All tests run serially (via xUnit's `[Collection("Serial")]`) because they interact with real application windows on the Windows desktop.

---

### AutomationId reference

Every interactive control in `MainForm.cs` is assigned a `Name` property, which WinForms surfaces as the UIAutomation `AutomationId`. FlaUI locates controls with:

```csharp
window.FindFirstDescendant(cf => cf.ByAutomationId("RunBtn")).AsButton()
```

| AutomationId | Control type | Description |
| --- | --- | --- |
| `PathBox` | TextBox | Target file / folder path |
| `BrowseFileBtn` | Button | Opens file picker |
| `BrowseFolderBtn` | Button | Opens folder picker |
| `AllTypesChk` | CheckBox | Scan all file types |
| `AlgoMd5` | RadioButton | MD5 algorithm |
| `AlgoSha1` | RadioButton | SHA1 algorithm |
| `AlgoSha256` | RadioButton | SHA256 algorithm (default) |
| `AlgoSha512` | RadioButton | SHA512 algorithm |
| `MetadataChk` | CheckBox | Include file metadata |
| `SidecarChk` | CheckBox | Write sidecar hash files |
| `SidecarExtBox` | TextBox | Sidecar file extension |
| `SidecarFmtSha256Sum` | RadioButton | sha256sum sidecar format (default) |
| `SidecarFmtHashOnly` | RadioButton | Hash-only sidecar format |
| `CsvChk` | CheckBox | Export results to CSV |
| `CsvPathBox` | TextBox | CSV output path |
| `CsvBrowseBtn` | Button | Opens CSV save dialog |
| `RunAsAdminBtn` | Button | Relaunches as Administrator |
| `ClearBtn` | Button | Clears results list |
| `StopBtn` | Button | Cancels active run |
| `RunBtn` | Button | Starts hashing |
| `StatusLabel` | Label | Status text (accessible name = label text) |
| `ResultsView` | ListView | Results list |

---

### Test classes

**`MainFormStateTests`** — read-only assertions about the form's initial state. All nine tests share one app instance (`IClassFixture<AppFixture>`) so none of them modify UI state.

| Test | What it asserts |
| --- | --- |
| `Title_StartsWithFileHasher` | Window title begins with "FileHasher" |
| `DefaultAlgorithm_IsSha256` | SHA256 radio button is checked on launch |
| `OtherAlgorithms_NotSelectedByDefault` | MD5, SHA1, SHA512 are unchecked |
| `RunButton_EnabledAtStart` | Run button is enabled |
| `StopButton_DisabledAtStart` | Stop button is disabled |
| `StatusLabel_ShowsReadyAtStart` | Status label reads "Ready." |
| `PathBox_EmptyAtStart` | Path box is empty |
| `SidecarCheckbox_UncheckedByDefault_AndOptionsDisabled` | Sidecar checkbox is off; extension box is disabled |
| `CsvCheckbox_UncheckedByDefault_AndOptionsDisabled` | CSV checkbox is off; path box is disabled |
| `SidecarExtBox_DefaultExtension_IsSha256` | Default extension value is `.sha256` |

**`MainFormInteractionTests`** — tests that click controls and observe state changes. Each test method gets its own `AppFixture` (fresh process) so there is no state leakage between tests.

| Test | What it covers |
| --- | --- |
| `RunWithNoPath_ShowsWarningDialog` | Clicking Run with an empty path shows a warning dialog |
| `AlgorithmSelection_CanSwitchToMd5` | Selecting MD5 checks it and unchecks SHA256 |
| `AlgorithmSelection_CanSwitchToSha512` | Selecting SHA512 checks it |
| `SidecarCheckbox_TogglesOptionsPanel` | Toggling the sidecar checkbox enables/disables the extension box |
| `CsvCheckbox_TogglesOptionsPanel` | Toggling the CSV checkbox enables/disables the path box |
| `ClearButton_ResetsStatusLabel` | Clear button returns the status label to "Ready." |
| `HashSingleFile_AppearsInResultsAndCompletionDialogShows` | Hashing a temp file produces a result row and shows the completion dialog |

---

### CI / custom exe path

`AppFixture` locates the executable by walking up from the test assembly's output directory, trying `Debug` then `Release` configurations. To override this (e.g. in a CI pipeline that publishes to a specific location), set the `FILEHASHER_EXE` environment variable to the full path of `FileHasher.exe`:

```powershell
$env:FILEHASHER_EXE = "C:\build\output\FileHasher.exe"
dotnet test FileHasherApp.Tests\FileHasherApp.Tests.csproj
```

---

## PowerShell Script

`filehasher.ps1` is the original script that the GUI application is based on. It recursively scans a folder for `.exe` and `.msi` files, computes SHA256 hashes, and optionally writes sidecar files and a CSV report.

### Parameters

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `-RootPath` | `string` | *(required)* | Root folder to scan recursively |
| `-OutCsv` | `string` | `""` | Path to write a CSV results file; omit to skip |
| `-IncludeMetadata` | switch | off | Include file size and last-modified date in output |
| `-WriteSidecarHashes` | switch | off | Write a sidecar hash file next to each installer |
| `-SidecarExtension` | `string` | `.sha256` | File extension for sidecar files |
| `-SidecarFormat` | `string` | `sha256sum` | Sidecar content format: `sha256sum` or `hashonly` |

### Examples

```powershell
# Hash all .exe and .msi files under C:\Installers, print to console
.\filehasher.ps1 -RootPath "C:\Installers"

# Include metadata and export to CSV
.\filehasher.ps1 -RootPath "C:\Installers" -IncludeMetadata -OutCsv "C:\hashes.csv"

# Write sha256sum-compatible sidecar files next to each installer
.\filehasher.ps1 -RootPath "C:\Installers" -WriteSidecarHashes

# Write hash-only sidecar files with a custom extension
.\filehasher.ps1 -RootPath "C:\Installers" -WriteSidecarHashes -SidecarExtension ".hash" -SidecarFormat hashonly
```
