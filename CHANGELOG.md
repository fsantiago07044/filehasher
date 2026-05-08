# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Release tags use the form `vMAJOR.MINOR.PATCH`. The release pipeline strips the
leading `v` when injecting the version into the .NET build and validates the
result against `<Version>` in `FileHasherApp/FileHasherApp.csproj`.

## [Unreleased]

## [0.1.1] - 2026-05-08

### Added
- Drag-and-drop support on the main form for files and folders.
- Completion confirmation dialog and progress-bar color change when hashing finishes.
- Per-file sidecar-conflict dialog showing details of the existing file and sidecar, with Skip / Overwrite / Skip All / Overwrite All choices.
- `HashWorker.SidecarSkippedCount` and `HashWorker.SidecarOverwrittenCount` counters surfaced in the completion summary.
- FlaUI-based automated UI test suite (`FileHasherApp.Tests`) covering the algorithm, sidecar, CSV, and clear-after-hash flows.
- GitLab CI nightly Windows build pipeline (`.gitlab-ci.yml`) producing a self-contained single-file `FileHasher.exe` artifact.

### Changed
- Project version bumped to `0.1.1`.
- `LICENSE` updated so the named entity matches the application's copyright attribution.
- Minor copyright attribution correction.
- README rewritten to reflect the current UI, options, and behavior.

### Fixed
- The app no longer writes sidecars for files that are themselves sidecars (no more `.sha256.sha256` chains).
- "Skip all existing hashes" with zero remaining files to hash now still shows the final summary dialog.
- "Skip existing hashes" actually skips — previously the files were re-hashed anyway.
- The first file in a batch is no longer re-hashed before the user is offered the skip choice.
- Test stability: `StatusLabel` race fixed via `WaitUntilStatusContains` polling instead of an immediate read; FlaUI `.Click()` used in place of `IInvokePattern.Invoke()` to avoid blocking on `MessageBox.Show()`; completion-modal detection timing tightened; cleaner test-process exits to avoid orphaned processes during runs.

## [0.1.0] - 2026-03-23

### Added
- Initial release: self-contained, single-file .NET 8 WinForms application converted from the prior PowerShell `filehasher.ps1` script.
- Hash algorithm selection: MD5, SHA-1, SHA-256, SHA-512.
- Optional metadata column and sidecar hash files (`sha256sum`-format and hash-only formats, configurable extension).
- Custom application icon and project metadata.
- `.gitignore` covering standard .NET build output.

[Unreleased]: https://internal-host/root/filehasher/-/compare/v0.1.1...HEAD
[0.1.1]: https://internal-host/root/filehasher/-/compare/v0.1.0...v0.1.1
[0.1.0]: https://internal-host/root/filehasher/-/tags/v0.1.0
