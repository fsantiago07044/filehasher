# FileHasher

A utility to hash files and folders, write sidecar hash files, and export results to CSV. The original implementation is a PowerShell script; the compiled Windows GUI application started from that script's design but has been iterated on extensively since and now offers significantly more functionality than the script.

---

## Contents

- [Windows GUI Application](#windows-gui-application)
  - [Requirements](#requirements)
  - [Building](#building)
  - [Reproducible builds](#reproducible-builds)
  - [Usage](#usage)
    - [Selecting a target](#selecting-a-target)
    - [Choosing a hash algorithm](#choosing-a-hash-algorithm)
    - [Options](#options)
    - [Running and stopping](#running-and-stopping)
    - [Results](#results)
    - [Logs](#logs)
    - [UAC elevation](#uac-elevation)
    - [Help menu](#help-menu)
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
- [Acknowledgements](#acknowledgements)

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

### Reproducible builds

This section is for anyone who wants to independently verify that the published binaries match the source in this repository — useful for security review, downstream packaging, or peace of mind.

The CI pipeline passes `-p:Deterministic=true -p:ContinuousIntegrationBuild=true` to `dotnet publish` so the unsigned executable content can be reproduced byte-for-byte from a clean checkout with matching tools. The trailing **Authenticode signature** on the released `.exe` is appended by the maintainer's code-signing step using a hardware token, so the signed region of the file will always differ between a third-party rebuild and an official release; what you can verify byte-for-byte is the underlying executable.

#### How to reproduce a release locally

1. Check out the tag you want to verify:
   ```powershell
   git fetch --tags
   git checkout vX.Y.Z
   ```
2. Install the .NET SDK version pinned by the repo's `global.json` at the root, for example:
   ```json
   {
     "sdk": {
       "version": "8.0.420",
       "rollForward": "disable"
     }
   }
   ```
   `rollForward: "disable"` means `dotnet` will refuse to build if that exact patch isn't installed, so you can't accidentally use a different SDK. Each release tag carries the `global.json` content the CI used at that time; checking out the tag and installing the SDK it names is all the version-matching you need.
3. Build from `FileHasherApp\`:
   ```powershell
   dotnet publish -c Release -r win-x64 `
       --self-contained true `
       -p:PublishSingleFile=true `
       -p:EnableCompressionInSingleFile=true `
       -p:DebugType=embedded `
       -p:Version=X.Y.Z `
       -p:Deterministic=true `
       -p:ContinuousIntegrationBuild=true
   ```
   The two flags beyond the regular [Building](#building) command are what make the output stable across machines:
   - `Deterministic=true` — replaces build timestamps and random GUIDs in PE/PDB metadata with content-derived values.
   - `ContinuousIntegrationBuild=true` — normalizes embedded source paths so the PDB content doesn't depend on your build machine's directory layout.
4. The output at `FileHasherApp\bin\Release\net8.0-windows\win-x64\publish\FileHasher.exe` should be byte-identical to:
   - the **unsigned** `FileHasher.exe` artifact uploaded by the CI's `build` stage (available on the pipeline's job page for 30 days after the release), or
   - the signed release `.exe` with its trailing Authenticode certificate table stripped — for example via `osslsigncode remove-signature signed.exe unsigned.exe`.

Compare with any SHA-256 utility. A match means the published bytes came from this source at that tag.

#### Caveats

- **Match the SDK first.** Patch-level SDK drift is the single biggest source of size and byte differences; even one patch apart will diverge by tens or hundreds of KB after compression. The SDK version the CI used to produce a given release is recorded in that pipeline's `build` job log.
- **Applies only to releases built after the CI adopted deterministic flags.** Binaries from tags cut before `.gitlab-ci.yml` started passing `-p:Deterministic=true -p:ContinuousIntegrationBuild=true` (initial release `v0.1.1` predates the change) cannot be reproduced byte-for-byte regardless of the local flags used.
- **The signature itself can't be reproduced** without access to the same code-signing certificate and HSM token. Verification of the signature, separate from binary reproduction, is done with standard tools like `signtool verify` or `osslsigncode verify` against the signed release `.exe`.

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

- **Extension** — the suffix appended to the original filename. The suggested value follows the selected hash algorithm automatically (`.md5`, `.sha1`, `.sha256`, `.sha512`); switching algorithms updates the box only while it still holds one of those four standard values, so a custom extension you typed is never overwritten.
- **Format** — controls the content of the sidecar file:
  - **`{algo}sum` format** (default) — `HASH *filename`, compatible with the standard `md5sum`/`sha1sum`/`sha256sum`/`sha512sum` tools. The radio button's label follows the selected algorithm (e.g. **sha256sum format** when SHA256 is selected).
  - **Hash only** — the raw hash string with no filename, useful for simple verification scripts.
  - **Extended** — `HASH *filename *lastModified *sizeBytes`, where the last-modified timestamp is ISO-8601 UTC (e.g. `2026-08-09T14:33:05Z`), matching the CSV export's `LastWriteUtc` format.

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

**Hash files inside MSI installers (experimental)**
When checked, every `.msi` file the worker encounters is also opened read-only via the Windows Installer database API, and the files contained inside it are hashed individually in addition to the MSI itself. Each inner file appears as its own results row prefixed with `[parent.msi]`, colored steel-blue, with the MSI's internal install path (e.g. `Program Files\msi-test\foo.exe`) in the **File Path** column and the raw MSI Directory-table identifier (e.g. `PFiles64`, `INSTALLDIR`) in a new **MSI Dir** column on the right. The CSV export gains optional `Container` and `MsiDirectoryId` columns when this option is on.

The **Scan all file types** checkbox above also drives the inner-MSI filter: when unchecked, only `.exe` and `.msi` inner files are surfaced; when checked, every inner file regardless of extension. The checkbox stays enabled whenever the target is a folder OR an `.msi` file with this option on.

The MSI is treated as data — the database is opened read-only and the contents extracted to a sandboxed per-MSI temp directory that's deleted as soon as hashing finishes. The installer is **not** executed.

For the full security model (file-size / total-size / file-count / disk-headroom caps, path-traversal and reparse-point rejection, sibling-escape cleanup), the test-coverage layout across all three tiers, and the known limitations, see [`README-msi-inner-scan.md`](README-msi-inner-scan.md).

---

#### Running and stopping

| Control | Behaviour |
| --- | --- |
| **▶ Run** | Starts enumeration then hashing. Most controls are disabled while a run is in progress. |
| **Verify Sidecars** | Verifies previously written sidecar hash files against the current state of the files — see [Verifying sidecars](#verifying-sidecars). |
| **Stop** | Cancels the current run cleanly. Files already hashed (or verified) are retained in the results list. |
| **Clear Results** | Clears all rows from the results list, resets the progress bar, and returns the status to "Ready." Available before and after a run. |

The progress bar shows indeterminate (marquee) progress during folder enumeration, then switches to a percentage bar during hashing. When hashing completes successfully the bar turns **blue**.

A completion dialog is shown at the end of every run summarising:

- **Files hashed** — count of successfully hashed files
- **Errors** — count of files that failed
- **Sidecars skipped** — shown only when at least one existing sidecar was left in place
- **Sidecars overwritten** — shown only when at least one existing sidecar was replaced
- **Log** — path to the current log file

---

#### Verifying sidecars

**Verify Sidecars** re-hashes files and compares the result against their existing sidecar files, using the current **Target** path and the **Extension** configured under the sidecar options (the *Write sidecar hash files* checkbox does not need to be checked). For a folder target the scan is recursive; targeting a single file verifies that file's sidecar, and targeting a sidecar file directly verifies it against its base file.

The hash algorithm is **auto-detected per sidecar** from the length of the stored hash (32 hex characters = MD5, 40 = SHA1, 64 = SHA256, 128 = SHA512), so the algorithm radio selection is ignored during verification and a folder with mixed-algorithm sidecars verifies correctly in one pass. All three sidecar formats are recognized: bare hash, `HASH *filename`, and the extended `HASH *filename *lastModified *sizeBytes`.

Each verified item appears as one results row (the hash column header becomes **Verification**):

| Verdict | Color | Meaning |
| --- | --- | --- |
| `OK (ALGO)` | green | Re-computed hash matches the sidecar. |
| `MISMATCH (ALGO)` | red | Hash differs — the row shows both the expected and the computed value. |
| `MISSING FILE` | red | A sidecar exists but the file it attests to is gone. |
| `NO SIDECAR` | orange | The file matches the current scan filter (`.exe`/`.msi`, or everything when **Scan all file types** is checked) but has no sidecar — a completeness audit. |
| `PARSE ERROR` | red | The sidecar's content is not recognized as any supported format. |
| `READ ERROR` | red | The file or its sidecar could not be read. |

**The hash alone decides pass/fail.** For extended-format sidecars, a differing embedded filename, modified date, or size on an otherwise-matching row is appended as an informational note — a file's modified date often changes legitimately on copy or restore.

Verification runs are logged like hashing runs and end with the same style of summary dialog (per-verdict counts). The **Export results to CSV** option does not apply to verification runs.

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

Right-clicking a result row opens a context menu:

- **Open in File Explorer** — opens the containing folder with the file pre-selected (`explorer /select`). If the file has been deleted since it was hashed, the folder is opened plainly instead.
- **Open PowerShell here** — opens a Windows PowerShell window in the containing folder.
- **Open Command Prompt here** — opens a `cmd` window in the containing folder.
- **Copy Hash** — copies the row's hash to the clipboard (disabled on error rows and on verification rows where no hash was computed).
- **Copy File Path** — copies the row's full on-disk path to the clipboard.

**Double-clicking a row** (or pressing Enter on it) performs the Explorer action directly.

For inner-MSI rows (experimental MSI scan), the location-based actions target the containing `.msi` file — the extracted temp copies are already deleted by the time results are browsable — and **Copy File Path** copies that `.msi` path. Warning rows have no payload, so no menu appears for them. The three *Open* items are greyed out when the row's folder no longer exists; the copy items keep working.

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

#### Help menu

**Help → FileHasher Help…** (or **F1**) opens the in-app help window: a topic
list on the left and rendered content on the right, covering every feature
(getting started, targets, scan filtering, algorithms, sidecar formats and
conflicts, verification verdicts, the experimental MSI scan, CSV export, the
results list, logs, and Administrator mode). A link bar at the bottom offers
**Email Support** (pre-addressed to support@fabianasantiago.com with the
subject pre-filled as `FileHasher-Windows-<version>`, where the version is
read from the assembly at runtime so it tracks every release automatically),
**Support Website**, and **Privacy Policy**. The same website and privacy
links also live directly in the Help menu.

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
| `SidecarFmtSha256Sum` | RadioButton | `{algo}sum` sidecar format (default; label follows the selected algorithm) |
| `SidecarFmtHashOnly` | RadioButton | Hash-only sidecar format |
| `SidecarFmtExtended` | RadioButton | Extended sidecar format (hash, filename, modified, size) |
| `CsvChk` | CheckBox | Export results to CSV |
| `CsvPathBox` | TextBox | CSV output path |
| `CsvBrowseBtn` | Button | Opens CSV save dialog |
| `RunAsAdminBtn` | Button | Relaunches as Administrator |
| `ClearBtn` | Button | Clears results list |
| `VerifyBtn` | Button | Starts sidecar verification |
| `StopBtn` | Button | Cancels active run |
| `RunBtn` | Button | Starts hashing |
| `StatusLabel` | Label | Status text (accessible name = label text) |
| `ResultsView` | ListView | Results list |
| `ResultsMenu` | ContextMenuStrip | Right-click menu on result rows |
| `MiOpenExplorer` | MenuItem | Open in File Explorer (select the file) |
| `MiOpenPowerShell` | MenuItem | Open PowerShell at the row's location |
| `MiOpenCmd` | MenuItem | Open Command Prompt at the row's location |
| `MiCopyHash` | MenuItem | Copy the row's hash to the clipboard |
| `MiCopyPath` | MenuItem | Copy the row's file path to the clipboard |
| `MiHelpContents` | MenuItem | Help menu: opens the help window (F1) |
| `MiSupportWebsite` | MenuItem | Help menu: opens the support website |
| `MiPrivacyPolicy` | MenuItem | Help menu: opens the privacy policy |
| `HelpForm` | Window | The help window ("FileHasher Help") |
| `HelpTopicsList` | ListBox | Help window topic list |
| `HelpContentBox` | RichTextBox | Help window rendered topic content |
| `HelpEmailLink` | LinkLabel | Email Support (version-stamped subject) |
| `HelpSupportSiteLink` | LinkLabel | Opens the support website |
| `HelpPrivacyLink` | LinkLabel | Opens the privacy policy |

---

### Test classes

**`MainFormStateTests`** — read-only assertions about the form's initial state. All tests share one app instance (`IClassFixture<AppFixture>`) so none of them modify UI state.

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

**`MainFormInteractionTests`** — basic interaction tests. Each test gets its own app process.

| Test | What it covers |
| --- | --- |
| `RunWithNoPath_ShowsWarningDialog` | Clicking Run with an empty path shows a warning dialog |
| `AlgorithmSelection_CanSwitchToMd5` | Selecting MD5 checks it and unchecks SHA256 |
| `AlgorithmSelection_CanSwitchToSha512` | Selecting SHA512 checks it |
| `SidecarCheckbox_TogglesOptionsPanel` | Toggling the sidecar checkbox enables/disables the extension box |
| `CsvCheckbox_TogglesOptionsPanel` | Toggling the CSV checkbox enables/disables the path box |
| `ClearButton_ResetsStatusLabel` | Clear button returns the status label to "Ready." |
| `HashSingleFile_AppearsInResultsAndCompletionDialogShows` | Hashing a temp file produces a result row and shows the completion dialog |

**`MainFormAlgorithmTests`** — algorithm selection completeness and hash-value accuracy. Hash output is verified by writing a sidecar in hash-only format and comparing it against the .NET `HashAlgorithm` provider for the same bytes.

| Test | What it covers |
| --- | --- |
| `AlgorithmSelection_CanSwitchToSha1` | SHA1 radio button can be selected |
| `AlgorithmSelection_OnlyOneCanBeCheckedAtATime` | Switching algorithms unchecks the previous one |
| `HashAccuracy_MatchesDotNetCrypto (MD5)` | MD5 hash produced by the app matches `MD5.HashData` |
| `HashAccuracy_MatchesDotNetCrypto (SHA1)` | SHA1 hash matches `SHA1.HashData` |
| `HashAccuracy_MatchesDotNetCrypto (SHA256)` | SHA256 hash matches `SHA256.HashData` |
| `HashAccuracy_MatchesDotNetCrypto (SHA512)` | SHA512 hash matches `SHA512.HashData` |

**`MainFormFolderTests`** — folder scanning, file filters, recursive subdirectories, and the Stop button.

| Test | What it covers |
| --- | --- |
| `FolderScan_DefaultFilter_OnlyHashesExeAndMsi` | Mixed folder: only .exe and .msi files counted in results |
| `FolderScan_AllTypes_HashesEveryFile` | "Scan all file types" hashes every file regardless of extension |
| `FolderScan_NoMatchingFiles_ShowsStatusMessage` | Folder with no .exe/.msi shows "No matching files found" status |
| `FolderScan_MultipleExe_CorrectRowCount` | Three .exe files produce three result rows |
| `FolderScan_RecursiveSubfolders_HashesAllMatchingFiles` | Files in nested subfolders are found and hashed |
| `FolderScan_EmptyFolder_ShowsNoFilesStatus` | Completely empty folder shows "No matching files found" |
| `StopDuringRun_ButtonStatesReset` | Stop re-enables Run and disables itself |
| `StopDuringRun_StatusChangesFromReady` | Status label changes from "Ready." after Stop is clicked (run may complete on fast machines, so cancellation isn't strictly asserted) |
| `RunButton_IsDisabledWhileRunning` | Run button is disabled for the duration of a run |

**`MainFormSidecarTests`** — sidecar file creation, content formats, custom extensions, and the full conflict-resolution dialog.

| Test | What it covers |
| --- | --- |
| `SidecarWrite_CreatesFileNextToTarget` | `.sha256` sidecar is created alongside the source file |
| `SidecarWrite_Sha256SumFormat_ContainsHashAndFilename` | sha256sum format: `HASH *filename` |
| `SidecarWrite_HashOnlyFormat_ContainsHashOnly` | Hash-only format: bare hash string, no filename or `*` |
| `SidecarWrite_ExtendedFormat_ContainsHashFilenameDateAndSize` | Extended format: `HASH *filename *ISO-8601-UTC-date *sizeBytes`, each field verified against the real file |
| `SidecarWrite_CustomExtension_UsesCorrectExtension` | Custom extension is respected; default `.sha256` is not created |
| `SidecarWrite_NeverCreatesSidecarOfSidecar` | Second run does not create `.sha256.sha256` |
| `SidecarConflict_Overwrite_UpdatesSidecarContent` | Overwrite: existing sidecar is replaced |
| `SidecarConflict_Skip_LeavesExistingSidecarUnchanged` | Skip: existing sidecar is preserved |
| `SidecarConflict_OverwriteAll_UpdatesAllSidecarsWithOnlyOneDialog` | Overwrite All: all sidecars replaced after a single dialog |
| `SidecarConflict_SkipAll_PreservesAllSidecarsWithOnlyOneDialog` | Skip All: all sidecars preserved after a single dialog |
| `SidecarConflict_SkipAll_ResultsViewIsEmpty` | When all files are skipped, zero rows appear in the results list |

**`MainFormCsvTests`** — CSV export file creation, content verification, metadata columns, and missing-path validation.

| Test | What it covers |
| --- | --- |
| `CsvMissingPath_ShowsValidationWarning` | Run with CSV enabled but no path shows a warning |
| `CsvExport_CreatesFile` | CSV file is written to the specified path |
| `CsvExport_ContainsHeaderRow` | First line is `Path,SHA256` (or selected algorithm) |
| `CsvExport_DataRowContainsCorrectHashAndPath` | Data row contains the correct hash and file name |
| `CsvExport_WithMetadata_HeaderContainsMetadataColumns` | Metadata mode adds `LengthBytes` and `LastWriteUtc` columns |
| `CsvExport_WithMetadata_DataRowContainsSizeAndDate` | Metadata data row contains file size and modification date |
| `CsvExport_Algorithm_HeaderReflectsSelectedAlgorithm` | Column header updates when algorithm changes (e.g. MD5) |
| `CsvExport_OnlySuccessfulRowsIncluded` | Only hashed files appear in CSV; errors are excluded |

**`MainFormHelpMenuTests`**: Help menu and in-app help window. Each test gets its own app process.

| Test | What it covers |
| --- | --- |
| `HelpMenu_ContainsContentsSupportAndPrivacyItems` | Help menu lists Help contents, Support Website, Privacy Policy, and About |
| `HelpWindow_OpensAndListsEveryTopic` | Help window opens via the menu; topic count matches `HelpContent.Topics` |
| `HelpWindow_TopicSelectionUpdatesContent` | Selecting a topic renders its content (verification verdicts shown) |
| `HelpWindow_SupportTopicShowsVersionStampedSubject` | Support topic and mailto carry `FileHasher-Windows-<version>` |
| `HelpWindow_ReopeningActivatesExistingInstance` | Re-invoking Help activates the existing window instead of duplicating it |

**`MainFormEdgeCaseTests`** — edge cases, error paths, and miscellaneous UI invariants.

| Test | What it covers |
| --- | --- |
| `InvalidPath_ShowsWarningDialog` | Non-existent path shows a warning dialog |
| `ClearAfterHash_EmptiesResultsList` | Clear removes all rows from the results list |
| `ClearAfterHash_ResetsStatusToReady` | Clear resets the status label to "Ready." |
| `SecondRun_ClearsAndReplacesPreviousResults` | A second run replaces (not appends to) previous results |
| `AboutDialog_OpensAndCanBeDismissed` | Help → About opens a dialog that can be dismissed |
| `HashWithMetadata_RunCompletesAndShowsResult` | Metadata mode still produces a result row |
| `PathBox_AcceptsTypedPath` | Typing a path into the path box sets it correctly |
| `CompletionDialog_ShowsAfterSuccessfulRun` | Completion dialog title contains "Complete" |
| `StatusLabel_ShowsDoneAfterSuccessfulRun` | Status label contains "Done" after the completion dialog is dismissed |
| `AllTypesCheckbox_EnabledAfterFolderDropped_DisabledAfterFileSelected` | AllTypesChk is unchecked at launch |

**`MainFormSidecarAlgoUiTests`** — the sidecar suggested extension and the `{algo}sum format` radio label follow the selected hash algorithm. Each test gets its own app process.

| Test | What it covers |
| --- | --- |
| `AlgorithmSwitch_UpdatesExtensionAndSumRadioLabel (MD5/SHA1/SHA512)` | Selecting an algorithm updates the extension to `.md5`/`.sha1`/`.sha512` and the radio label to `md5sum format` etc. |
| `AlgorithmSwitch_RoundTrip_RestoresSha256Suggestion` | Switching away and back to SHA256 restores `.sha256` and `sha256sum format` |
| `AlgorithmSwitch_CustomExtension_IsNeverClobbered` | A hand-typed extension survives algorithm switches while the label still follows |

**`MainFormContextMenuTests`** — the right-click context menu on result rows and double-click activation. The three *Open* items are deliberately never invoked (they would spawn real Explorer/PowerShell/cmd processes on the test host); their presence and enabled state are asserted instead. Each test gets its own app process.

| Test | What it covers |
| --- | --- |
| `ContextMenu_OnHashedRow_ShowsAllItemsEnabled` | All five menu items exist and are enabled on a successfully hashed row |
| `ContextMenu_CopyHash_PutsHashOnClipboard` | Copy Hash places the row's exact hash on the clipboard |
| `ContextMenu_CopyPath_PutsFullPathOnClipboard` | Copy File Path places the row's full on-disk path on the clipboard |
| `ContextMenu_CopyHash_DisabledOnErrorRow_CopyPathStillEnabled` | On a failed-hash row (file locked exclusively), Copy Hash is disabled while Copy File Path stays enabled |
| `RowDoubleClick_WithDeletedFolder_IsSafeNoOp` | Double-clicking a row whose file and folder are gone is a safe no-op (exercises the Explorer fallback chain without spawning a window) |

**`MainFormVerifyTests`** — end-to-end tests for the **Verify Sidecars** button through the UI: button flow, completion dialog, status-line counts, and row counts. Verdict-level precision lives in `SidecarVerifierTests`. Each test gets its own app process.

| Test | What it covers |
| --- | --- |
| `VerifyButton_EnabledAtStart` | The Verify Sidecars button is present and enabled |
| `VerifyWithNoPath_ShowsWarningDialog` | Clicking Verify with an empty path shows a warning dialog |
| `Verify_FolderWithMixedStates_ReportsCountsInStatusAndRows` | Good + bad + sidecar-less files → "1 OK, 1 problem(s), 1 without sidecar" and three rows |
| `Verify_MixedAlgorithmSidecars_AutoDetectedInOnePass` | MD5 and SHA512 sidecars (both `.sha256`) verify OK together — algorithm auto-detection through the UI |
| `Verify_SidecarFileTargetedDirectly_VerifiesItsBaseFile` | Targeting a sidecar file verifies the file it attests to |
| `Verify_OrphanSidecar_ReportsProblem` | A sidecar whose base file is gone counts as a problem |
| `Verify_ThenHashRun_BothCompleteCleanly` | A hash run immediately after a verify run still works end-to-end |

**`SidecarVerifierTests`** — direct unit tests against the internal `SidecarVerifier` class (via `InternalsVisibleTo`, no UI, parallelizable): sidecar parsing across all three formats, hash-length algorithm auto-detection, status classification, informational metadata notes, and folder enumeration/filtering.

| Test | What it covers |
| --- | --- |
| `Ok_BareHashFormat` / `Ok_SumFormat_WithMatchingFilename` / `Ok_ExtendedFormat_AllMetadataMatching_NoNotes` | All three formats verify OK with no notes when everything matches |
| `Ok_LowercaseHash_ComparedCaseInsensitively` | Hash comparison is case-insensitive |
| `Ok_LeadingBlankLines_AreSkipped` | The first non-empty sidecar line is used |
| `Ok_ExtendedFormat_DifferingDateAndSize_NotedButStillOk` | Metadata differences produce notes, never failures |
| `Ok_SumFormat_DifferentEmbeddedFilename_Noted` | Embedded-filename difference is noted, row stays OK |
| `Mismatch_ReportsExpectedAndComputedHashes` | Mismatch detail carries both hashes |
| `MissingFile_WhenSidecarHasNoBaseFile` | Orphan sidecar → MISSING FILE |
| `NoSidecar_ForSingleFileTargetWithoutSidecar` | Sidecar-less file target → NO SIDECAR |
| `ParseError_NonHexContent` / `ParseError_UnsupportedHashLength` | Unrecognizable sidecar content → PARSE ERROR |
| `AlgorithmAutoDetected_FromHashLength (MD5/SHA1/SHA256/SHA512)` | 32/40/64/128 hex chars map to the right algorithm |
| `SidecarTargetedDirectly_VerifiesItsBaseFile` | Sidecar-file target resolves to its base file |
| `Folder_MixedStatuses_ClassifiedAndCounted` | Mixed folder classifies every row and the summary counts match |
| `Folder_AllTypes_AuditsNonExeFilesToo` | The scan-all-types flag widens the NO SIDECAR audit |
| `Folder_ResultsAreSortedByPath` | Folder results are emitted in path order |

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

---

## Acknowledgements

This project was developed with assistance from Anthropic's Claude AI. Per-commit attribution is recorded in the git history via `Co-Authored-By` lines.
