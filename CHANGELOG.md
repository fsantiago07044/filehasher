# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Release tags use the form `vMAJOR.MINOR.PATCH`. The release pipeline strips the
leading `v` when injecting the version into the .NET build and validates the
result against `<Version>` in `FileHasherApp/FileHasherApp.csproj`.

## [Unreleased]

## [0.2.1] - 2026-05-25

### Changed
- Re-publish of v0.2.0 under a fresh tag. v0.2.0's tag name was permanently reserved on the GitHub mirror by GitHub's Immutable Releases policy during a history-signing cleanup, blocking the GitLab → GitHub mirror push for that one ref. v0.2.1 has no functional changes from v0.2.0; the source tree differs only in `<Version>` (`0.2.0` → `0.2.1`) and this CHANGELOG entry.

## [0.2.0] - 2026-05-22

### Added
- Experimental **Hash files inside MSI installers** option. When enabled, every `.msi` file is opened (read-only) via the Windows Installer database API and the files contained inside it are hashed individually in addition to the MSI itself. Inner-file rows appear in the results list prefixed with `[parent.msi]`, colored steel-blue, with the MSI's internal install layout in the **File Path** column (e.g. `Program Files\msi-test\foo.exe` — well-known MSI Directory-table identifiers like `PFiles64` and `SystemFolder` are resolved to their Windows-friendly equivalents). The original raw identifier is preserved in a new **MSI Dir** column for audit. The CSV export gains optional `Container` and `MsiDirectoryId` columns when the option is on. The **Scan all file types** checkbox now also drives the inner-MSI filter — when unchecked, only `.exe`/`.msi` inner files are surfaced; when checked, every inner file regardless of extension. See [`README-msi-inner-scan.md`](README-msi-inner-scan.md) for the full security model, threat-model coverage, and known limitations.
- Signed binary releases via a tag-driven GitLab CI/CD pipeline (`build → test → sign → release`, plus a non-blocking `audit` stage that surfaces NuGet supply-chain CVEs against the GitHub Advisory Database). Each `vX.Y.Z` tag push produces a signed `FileHasher-X.Y.Z.exe`, a `.zip`, and matching `.sha256` sidecars, attached to a GitLab Release for the tag with the same files in the project's Generic Package Registry.
- Reproducible builds: the CI passes `-p:Deterministic=true -p:ContinuousIntegrationBuild=true` and the repo pins the .NET 8 SDK to `8.0.420` via `global.json`, so the unsigned executable bytes can be reproduced from a clean checkout. Details and verification recipe in the main `README.md` under "Reproducible builds."
- `CHANGELOG.md` (this file) tracking the project's release history.

### Changed
- The shipped `FileHasher.exe` is now truly single-file. Five native WPF libraries (`wpfgfx_cor3.dll`, `D3DCompiler_47_cor3.dll`, `PenImc_cor3.dll`, `PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`) that previously had to ship as sidecars alongside the `.exe` are now bundled inside it via `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>`. End users no longer need to extract or place those DLLs anywhere. The release `.zip` is correspondingly slimmer.
- `hash-icon.ico` is now embedded into the assembly as a managed resource (loaded via `Assembly.GetManifestResourceStream`) rather than being copied as a loose file next to the executable. End-user visual experience is unchanged; the publish output has one fewer file.

### Fixed
- Latent integer-overflow bug in the MSI inner-scan free-disk-space cap that would have silently disabled the cap for near-`int64` input values. Surfaced by the new `Cap_MinFreeDisk_Absurd_AbortsBeforeExtraction` unit test; replaced with saturated addition so the comparison fires correctly under all input ranges.

### Security
- The MSI inner-scan feature enforces, on every extraction: a per-file size cap (2 GiB default), a total declared-size cap (5 GiB), a file-count cap (10,000), a minimum free-disk-space headroom check (1 GiB), a post-extraction path-traversal guard that rejects any extracted file whose canonical path escapes the per-MSI temp directory, a reparse-point rejection guard, and a sibling-escape cleanup that snapshots the extract dir's parent before extraction and deletes any new sibling that appears during extraction (closes a real escape vector via malicious `Directory.DefaultDir` values that the original guard logic did not catch). The MSI is opened in read-only mode throughout; no installer code is ever executed. End-to-end test coverage includes programmatically-synthesized adversarial MSIs.

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

[Unreleased]: https://github.com/fsantiago07044/filehasher/compare/v0.2.1...HEAD
[0.2.1]: https://github.com/fsantiago07044/filehasher/compare/v0.1.1...v0.2.1
[0.1.1]: https://github.com/fsantiago07044/filehasher/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/fsantiago07044/filehasher/releases/tag/v0.1.0
