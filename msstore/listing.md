# Store listing copy

Every Partner Center field for the FileHasher listing, so the wording is
reviewable in-repo and a resubmission does not have to reinvent it. The
authoritative copy is whatever is live in Partner Center; this file is the
source it was typed from. Keep it in sync with the nuspec and winget
descriptions when the app's feature set changes.

Field limits shown are Partner Center's.

---

## Product setup

| Field | Value |
| --- | --- |
| Product type | EXE/MSI app |
| Reserved name | `FileHasher - Checksum Utility` (reserved 2026-09-03) |
| Category | Utilities + tools |
| Subcategory | File managers |

The bare `FileHasher` was not available; the descriptive form was accepted and
is what the Store listing displays. It matches the Mac App Store listing except
for the separator, which is a plain hyphen here because the Store's naming rules
reject unsupported special characters. The app itself, its winget package
(`FSPProductions.FileHasher`) and its Chocolatey package (`filehasher`) are all
still plain FileHasher, so keep the prose below using the short name; only the
Store's display title carries the descriptor.

Category rationale: the comparable Store apps (File Hasher, Files Hasher, Hash
Tool) all sit under Utilities + tools, so that is where someone browsing for
this lands. Developer tools > Utilities is the defensible second choice and a
narrower audience; Security > PC protection would be a stretch, since the app
verifies integrity rather than protecting the machine.

## Packages

| Field | Value |
| --- | --- |
| App type | MSI |
| Package URL | `https://github.com/fsantiago07044/filehasher/releases/download/vX.Y.Z/FileHasher-X.Y.Z.msi` |
| Architecture | x64 |
| Languages | en-us |
| Installer parameters | not required; the Store uses `/qn` for MSI |

## Properties

| Field | Value |
| --- | --- |
| Privacy policy URL | `https://fabianasantiago.com/privacy-policy/` |
| Website | `https://www.fspproductions.com/software-projects` |
| Support contact | `https://fabianasantiago.com/filehasher/support/` |
| Copyright and trademark | Copyright © 2026 FSP Productions, LLC |
| Additional license terms | MIT License: `https://github.com/fsantiago07044/filehasher/blob/main/LICENSE` |
| Developed by | FSP Productions, LLC |

**Privacy.** Use the URL. `https://fabianasantiago.com/privacy-policy/` carries a
dedicated "FileHasher (desktop application)" section covering both platforms: no
collection, no telemetry, no accounts, no network connections from the Windows
app at all, where each kind of output is written, and the macOS sandbox. It is
the same URL compiled into both shipped apps (`HelpContent.cs` on Windows,
`FileHasherApp.swift` on macOS) and the same one submitted to the Mac App Store,
so anything else would strand the in-app link in versions already installed.

**Support.** Use the URL. `https://fabianasantiago.com/filehasher/support/` was
macOS-only (it asked for your macOS version and explained the App Sandbox
prompt), which would have read as a broken listing to a Windows customer. It was
rewritten on 2026-09-04 to cover both platforms, with a Windows section on the
`.exe`/`.msi` scan filter, when Administrator is actually needed, the MSI inner
scan, and the right-click and F1 shortcuts. It is also what the app's own Help
menu links to.

The wider split is still worth reconciling some day: the app points at
fabianasantiago.com, winget and Chocolatey point at fspproductions.com and
GitHub issues.

## Store listing

### Short description (max 1000 characters)

Hash files and whole folder trees, write and verify sidecar hash files, and export the results to CSV.

### Description (max 10000 characters)

FileHasher computes cryptographic hashes for a single file or an entire folder tree, writes the sidecar hash files that go alongside them, verifies sidecars that already exist, and exports everything to CSV.

It is built for the moment you need to prove a file is the file you think it is: checking a download against a published checksum, recording hashes for a set of installers before they go out, or re-verifying an archive months later to confirm nothing on disk has rotted or been altered.

WHAT IT DOES

Choose MD5, SHA1, SHA256, or SHA512 per run. Point it at a file or a folder and it scans recursively, filtered to .exe and .msi by default, or every file type with one checkbox.

Write sidecar hash files in three formats: sha256sum style, hash only, or an extended format that also records the file's last-modified time in ISO 8601 UTC and its size in bytes. The suggested file extension follows the algorithm you picked, and a custom extension you type is never overwritten.

Verify a tree against the sidecars already on disk. FileHasher detects each sidecar's algorithm from its hash length, so a folder holding a mix of MD5 and SHA256 sidecars verifies in a single pass. Every file gets a verdict: OK, MISMATCH with the expected and computed hashes shown, MISSING FILE, NO SIDECAR for a file that should have one, PARSE ERROR, or READ ERROR.

Export any run to CSV, including file size and last-modified time.

Right-click a result to open its folder in File Explorer with the file selected, open PowerShell or Command Prompt there, or copy the hash or the full path.

Optionally open .msi packages read-only and hash the files inside them individually, in addition to the .msi itself, with each inner file's install-time path shown.

Every run is logged, and in-app help covers each feature with F1.

WHAT IT DOES NOT DO

FileHasher does not collect, transmit, or store any information about you or the files you hash. Everything happens on your machine. There is no account, no telemetry, and no network connection at all.

REQUIREMENTS

64-bit Windows 10 version 1809 or newer, or Windows 11. About 150 MB of free disk space. No .NET runtime installation is required; the app is self-contained.

LICENSING

Free and open source under the MIT license. Not trial software, nothing to activate, no license key.

### Product features (up to 20, 200 characters each)

- MD5, SHA1, SHA256, and SHA512, selected per run
- Recursive folder scans with an extension filter, or every file type with one checkbox
- Sidecar hash files in sha256sum, hash-only, or extended format with timestamp and size
- Verify a whole tree against existing sidecars, auto-detecting each one's algorithm
- Clear per-file verdicts: OK, mismatch with both hashes shown, missing file, or no sidecar
- CSV export including file size and last-modified time
- Right-click a result to open its folder, open a terminal there, or copy the hash or path
- Optionally hash the files contained inside .msi packages, individually
- Per-run logs and in-app help on F1
- No telemetry, no account, no network access; everything stays on your machine
- Free and open source under the MIT license

### Search terms (up to 7 terms, 30 characters each, 21 words total)

1. file hash
2. checksum
3. sha256
4. md5 sha1 sha512
5. sidecar hash file
6. integrity verification
7. hash folder

### What's new in this version (max 1500 characters)

Per release, from the CHANGELOG entry for that version. Keep it user-facing:
describe what changed for someone using the app, not the build system. For a
release whose changes are all internal, say so plainly rather than padding.

## Screenshots

Six PNGs in [`screenshots/`](screenshots/), captured on the Win10 build/test VM
at 1486 x 893 (1466 x 893 for the help window), comfortably over the Store's
1366 x 768 minimum. No logos or marketing text are overlaid, per the guidance.

Upload in this order; the first is the one most people see. Partner Center
reorders by drag and drop.

| # | File | Caption (200 char limit) |
| --- | --- | --- |
| 1 | `02-hash-run.png` | A completed SHA256 run over a folder of release artifacts, with every file's hash. |
| 2 | `03-verify-sidecars.png` | Verifying a folder against the sidecars already on disk: 17 files OK, one mismatch, one missing file, one never hashed. |
| 3 | `01-main-window.png` | Pick a file or a folder, choose an algorithm, and run. Sidecar writing and CSV export are one checkbox each. |
| 4 | `04-context-menu.png` | Right-click any result to open its folder, open PowerShell or Command Prompt there, or copy the hash or path. |
| 5 | `05-inner-msi-scan.png` | Optionally open an MSI read-only and hash the files inside it individually, alongside the MSI itself. |
| 6 | `06-help-window.png` | In-app help, on F1, covering every feature. |

Regenerating them for a later release: run
[`tools/make-demo-data.ps1`](tools/make-demo-data.ps1) then
[`tools/capture-screenshots.ps1`](tools/capture-screenshots.ps1) on the VM. The
capture script drives the app through UI Automation using the AutomationIds in
the main README, so it does not depend on where controls happen to sit. Four
things it has to work around, all learned the hard way:

- The app must be launched **through `explorer.exe`**. Started directly from an
  elevated context it inherits elevation and the title bar reads
  "FileHasher [Administrator]", which wrongly implies the app needs elevation.
- The completion dialog is **owner-owned**, so in the UIA tree it hangs off the
  main window, not the desktop. Searching the desktop's children never finds
  it, and an undismissed dialog sits in the middle of the next capture.
- Its title varies by run type ("Complete" after hashing, "Verification
  complete" after a verify), so the script matches on control type, not name.
- The MessageBox OK button does not reliably expose InvokePattern; Enter on the
  focused dialog is the fallback.
