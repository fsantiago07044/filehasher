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
| Reserved name | `FileHasher` preferred; `FileHasher - Checksum Utility` reserved as the fallback (see README) |
| Category | Utilities + tools |
| Subcategory | File managers |

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
