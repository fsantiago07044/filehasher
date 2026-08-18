# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Release tags use the form `vMAJOR.MINOR.PATCH`. The release pipeline strips the
leading `v` when injecting the version into the .NET build and validates the
result against `<Version>` in `FileHasherApp/FileHasherApp.csproj`.

## Unreleased

- In-app help: Help menu gains "FileHasher Help…" (F1), opening a help window
  with a topic list covering every feature, plus Support Website and Privacy
  Policy links. The Email Support link pre-fills its subject with the
  installed version (FileHasher-Windows-x.y.z), read from the assembly so it
  updates itself on every release. About dialog now shares the same version
  helper. New UIA test class MainFormHelpMenuTests (5 tests).

## [Unreleased]

### Added
- **Verify Sidecars** button in the actions row: re-hashes files and compares against their existing sidecars, using the current Target path and the configured sidecar extension (the write-sidecars checkbox need not be checked). The algorithm is auto-detected per sidecar from its hash length (32/40/64/128 hex chars → MD5/SHA1/SHA256/SHA512), so mixed-algorithm trees verify in one pass and the algorithm radios are ignored; all three sidecar formats parse. Per-row verdicts in the results list (hash column header becomes **Verification**): `OK` (green), `MISMATCH` (red, expected vs computed shown), `MISSING FILE` (red — sidecar present, file gone), `NO SIDECAR` (orange completeness-audit row for files matching the current scan filter), `PARSE ERROR`, `READ ERROR`. Hash alone decides pass/fail; extended-format metadata differences (filename/date/size) are appended as informational notes on OK rows. Logged like hashing runs, with a per-verdict summary dialog; CSV export does not apply to verification runs.
- Right-click context menu on results-list rows: **Open in File Explorer** (containing folder with the file pre-selected via `explorer /select`, falling back to the plain folder if the file has since been deleted), **Open PowerShell here**, and **Open Command Prompt here**, each targeting the row's location. Inner-MSI rows (experimental MSI scan) target the containing `.msi`'s location, since the extracted temp copies are deleted before results are browsable; warning rows have no location and show no menu.
- Results context menu additionally offers **Copy Hash** (disabled on rows without a computed hash) and **Copy File Path** below a separator, and **double-click / Enter on a row** triggers the Open-in-Explorer action. The three *Open* items are greyed out (rather than the whole menu hidden) when the row's folder no longer exists, so the copy items keep working.
- Third sidecar format, **Extended** — `HASH *filename *lastModified *sizeBytes`, with the last-modified timestamp in ISO-8601 UTC matching the CSV export's `LastWriteUtc` (e.g. `2026-08-09T14:33:05Z`). Placed after **Hash only** in the sidecar options row.

- Test coverage for all of the above: `SidecarVerifierTests` (20 direct unit-test cases against the internal `SidecarVerifier` — parsing of all three formats, hash-length algorithm auto-detection, every status classification, metadata notes, folder filtering/sorting), plus three new FlaUI classes — `MainFormVerifyTests` (Verify button end-to-end), `MainFormContextMenuTests` (menu item presence/enabled states, clipboard copy actions via an STA helper, double-click fallback safety; the Open items are asserted but never invoked to avoid spawning real processes on the test host), and `MainFormSidecarAlgoUiTests` (extension/label follow the algorithm, custom extensions survive). `MainFormSidecarTests` gains the Extended-format content test. New `TestHelpers`: generic `ClickButtonAndReturnModal`, context-menu polling, STA clipboard access, text-box polling.

### Fixed
- `package-msi` failed on prerelease working versions (`0.3.1-beta`): Windows Installer requires a purely numeric `ProductVersion` and rejects semver labels (WIX1148 / ICE24). The job now maps a prerelease suffix to a 4th (revision) field — `0.3.1-beta` → `0.3.1.1` — while the full semver string still flows into artifact filenames and the MSI's summary description (new `DisplayVersion` preprocessor define, defaulting to `Version` for ad-hoc local builds). Windows Installer ignores the revision field in version comparisons, so the extra field is purely cosmetic identification; consequently `MajorUpgrade` treats beta `0.3.1.1` and release `0.3.1` as the same version — uninstall a locally installed beta MSI before installing the same-numbered release MSI.

### Changed
- The About dialog now displays the assembly's informational version instead of the numeric assembly version, so a prerelease suffix in the csproj `<Version>` (e.g. `0.3.1-beta`, the current working version for the test cycle) is visible during testing. Release builds carry a plain `X.Y.Z` informational version, which renders identically to the previous design — nothing to revert at release time. Any `+metadata` portion is stripped before display.
- The sidecar suggested extension and the first format radio's label now follow the selected hash algorithm (`.md5`/`md5sum format`, `.sha1`/`sha1sum format`, `.sha256`/`sha256sum format`, `.sha512`/`sha512sum format`). The extension box is only auto-updated while it still holds one of the four standard values — a custom extension the user typed is never overwritten.
- Automated winget submission: new `winget` pipeline stage (`winget-update` job, Windows runner, tag pipelines only, after `mirror-github`) runs `wingetcreate update` against the GitHub Release MSI, patches version-specific locale fields (fresh `ReleaseNotesUrl`, drops carried-over `ReleaseNotes`), and opens the `microsoft/winget-pkgs` PR from the `fsantiago07044` fork. `allow_failure: true` like the mirror stage; generated manifests are kept as job artifacts for audit/manual resubmission. One-time prerequisites: standalone `wingetcreate.exe` on the Windows runner and a `WINGET_PAT` Protected CI/CD variable (classic PAT, `public_repo`) — see `ci/README.md`. `winget/README.md`'s manual workflow is reframed as the fallback path. First exercised by the next `vX.Y.Z` tag.

## [0.3.0] - 2026-07-21

### Added
- Signed MSI installer deliverable, built for distribution via **winget** (`winget install FSPProductions.FileHasher`) and usable standalone. New WiX authoring in `installer/FileHasher.wxs`: per-machine install to `Program Files\FileHasher`, Start Menu shortcut, Add/Remove Programs entry with icon, clean major-upgrade between versions. Two new pipeline stages implement the required signing order across the two runners — `package-msi` (Windows) wraps the already-signed exe in the MSI with the WiX 5.x toolset and runs full ICE validation, then `sign-msi` (Linux signer) Authenticode-signs the MSI itself (WiX only runs on Windows; the HSM only lives on the signer). The MSI and its `.sha256` sidecar join the existing four release assets on both the GitLab Release and the GitHub mirror (six assets total). New `winget/` folder documents the Windows-side submission workflow to `microsoft/winget-pkgs` (wingetcreate) with reviewable manifest templates. Windows runner gains a one-time prerequisite: the `wix` dotnet global tool, pinned to 5.x to avoid the v6+ OSMF EULA gate (see `ci/README.md`).

### Changed
- Linux signer host: retired the pre-2.2 custom osslsigncode build (couldn't sign MSI — built without libgsf) in favor of the distro's 2.8, symlinked at the pipeline's stable `/usr/local/bin/osslsigncode` path, and replaced the stock noble libp11 0.4.12 PKCS#11 engine (segfaults osslsigncode HSM signing with OpenSSL 3.0.13 — Ubuntu bug 2119094) with 0.4.20 built from source, with the distro package apt-mark held. Setup steps 4/4b/4c in `ci/README.md` document the new stack for a from-scratch rebuild.

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

[Unreleased]: https://github.com/fsantiago07044/filehasher/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/fsantiago07044/filehasher/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/fsantiago07044/filehasher/compare/v0.1.1...v0.2.1
[0.1.1]: https://github.com/fsantiago07044/filehasher/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/fsantiago07044/filehasher/releases/tag/v0.1.0
