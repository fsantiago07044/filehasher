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

You can also type or paste a path directly into the path box.

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

The progress bar shows indeterminate (marquee) progress during folder enumeration, then switches to a standard percentage bar during hashing.

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
