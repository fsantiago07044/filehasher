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

Note the split: the shipped app's Help menu points users at
fabianasantiago.com for support and privacy (`HelpContent.cs`), while the
winget and Chocolatey metadata point at fspproductions.com and GitHub issues.
The Store listing follows the app, on the grounds that a user who found the
support page from inside the app should not be sent somewhere else by the
Store. Worth reconciling across all three channels at some point.

**System requirements:** Windows 10 version 1809 (build 17763) or newer, 64-bit.
About 150 MB free disk space during install.

**Age rating:** run the questionnaire. Expect 3+ / Everyone. The honest answers
are no user-generated content, no data collection or transmission, no
advertising, no in-app purchases, no user-to-user communication.

## Product declarations

None of the four apply. Leave every box unchecked.

| Declaration | Answer | Why |
| --- | --- | --- |
| Depends on non-Microsoft drivers or NT services | No | Plain user-mode WinForms app. Checking this triggers a dependency approval review that adds time to certification and can fail it. |
| Tested to meet accessibility guidelines | No | See below. |
| Supports pen and ink input | No | Nothing in the UI handles pen or ink specifically. |
| Incorporates generative AI features | No | No AI models, local or remote. |

On accessibility: it is tempting to check, because the FlaUI test suite means
most controls already carry AutomationIds and every result verdict is conveyed
by text (`OK`, `MISMATCH`, `NO SIDECAR`) rather than colour alone. But
Microsoft's bar for that box is the full list, which includes verified keyboard
navigation and tab order, a 4.5:1 contrast ratio throughout, a clean run of
Inspect or AccChecker, and end-to-end verification with Narrator, Magnifier,
High Contrast, and High DPI. None of that has been done, the menu items do not
reliably surface AutomationIds, and the owner-drawn `ColorProgressBar` has no
accessibility implementation at all. The docs are blunt that declaring an app
accessible when it is not earns negative feedback. Leave it unchecked; it is a
reasonable future goal given how much of the groundwork the test suite already
laid.

## Notes for certification

Recommended, 2000 character limit, and worth writing here: the certification
docs state that "any instructions in the certification notes will be followed",
and one of the silent-install checks is that the app "can be successfully
installed when logged in with a standard user account", which a per-machine MSI
cannot do without elevation. Saying so up front is the difference between a
tester understanding the UAC prompt and filing it as a failure.

Update the date on every submission; the docs ask for it so testers can judge
whether a transient problem still applies.

```text
Submitted 2026-09-03.

FileHasher is a standalone desktop utility. No account, no sign-in, and no network connection is required or made. There is nothing to unlock, no hidden features, and no region-dependent behaviour.

To exercise it in under a minute: launch it, click Browse and pick any folder, leave SHA256 selected, and click Run. Per-file results appear in the list. Tick "Write sidecar hash files" and run again to see .sha256 files written next to the hashed files, then click "Verify Sidecars" to have them checked back.

Install:
- The MSI is a per-machine install to %ProgramFiles%\FileHasher, so Windows shows a UAC prompt. This is by design: the app is meant to be available to every user of the machine, and no per-user variant is published.
- It creates a Start Menu shortcut and an Add/Remove Programs entry carrying ProductName FileHasher, Publisher FSP Productions LLC, version, and icon. Uninstall removes the program folder and the shortcut.
- The MSI and the FileHasher.exe inside it are Authenticode-signed by FSP Productions, LLC with an RFC 3161 timestamp. The exe is a self-contained single-file .NET 10 build, so no runtime install is needed.

Two behaviours a scanner may notice. Both are intentional and both are user-initiated:
- The optional "Hash files inside MSI installers" checkbox opens .msi files read-only through the Windows Installer database API and extracts their contents to %TEMP%\FileHasher_msi_<random> so each inner file can be hashed individually. That directory is deleted when the run finishes.
- The app appends to a log at %AppData%\FileHasher\Logs\FileHasher_<date>.log, and writes sidecar hash files only beside files the user selected, and only when that option is ticked.

No non-Microsoft drivers or NT services, no bundled third-party software, no advertising, and no telemetry. The same signed MSI is distributed through winget (FSPProductions.FileHasher) and Chocolatey (filehasher).
```

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

See the Assets section of [`README.md`](README.md). They must be PNG at
1366 x 768 or larger and must not have logos or marketing text added on top.
Each can carry a caption of 200 characters or less; suggested captions:

1. Main window, ready to scan a folder.
2. A completed SHA256 run over a folder tree, with per-file results.
3. Verifying a tree against its existing sidecar files, showing mixed verdicts.
4. Right-click a result to open its folder, open a terminal there, or copy the hash.
5. Hashing the files contained inside an MSI package individually.
6. In-app help, covering every feature.
