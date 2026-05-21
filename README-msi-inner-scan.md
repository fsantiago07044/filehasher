# MSI Inner-File Scan (experimental)

> **Status:** experimental feature on branch `feature/msi-inner-scan`. Not present in `main`. This file lives only on the feature branch; its content will be folded into the main `README.md`'s "Options" section when (and if) the feature merges back.

## What it does

When the **Hash files inside MSI installers** checkbox in the Options group is enabled, every `.msi` file the worker encounters is:

1. Hashed normally as a single binary blob, exactly as before — one results row per MSI.
2. Opened (read-only) via the Windows Installer COM API.
3. Inspected against safety caps (file count, declared sizes, free disk space — see [Security model](#security-model) below).
4. Extracted to a per-MSI temp directory, with the cabinet streams and embedded streams unpacked into the install-layout directory tree the MSI declares.
5. Each extracted inner file is hashed and produces an additional results row tagged with the parent MSI in the `Container` field. The displayed path is the MSI-internal layout (e.g. `Program Files/MyApp/foo.exe`), not the temp directory.
6. The temp directory is deleted (best-effort, including read-only flag clearing) when the inner-file pass finishes — whether it completed successfully, was cancelled, or threw.

The MSI is treated as **data**: the Windows Installer database is opened in `DatabaseOpenMode.ReadOnly` and queried for its `File` table contents. The installer is **not executed**; no system state is changed.

## How to enable

In the main window's **Options** group, tick **Hash files inside MSI installers (experimental)**. The checkbox is unchecked by default. With it on:

- All `.msi` files in the target produce inner-file rows in addition to the MSI's own row.
- Inner-file rows are colored steel-blue in the results list to distinguish them from top-level files.
- Inner-file rows are prefixed in the display with `[parent.msi] `.
- The displayed file path resolves common MSI Directory-table identifiers to their Windows-friendly equivalents — `PFiles64\msi-test\foo.exe` becomes `Program Files\msi-test\foo.exe`, `ProgramFilesFolder\…` becomes `Program Files (x86)\…`, etc. (see `MsiExtractor.WellKnownMsiDirectoryNames` for the full table). Custom identifiers an MSI author invented (e.g. `INSTALLDIR`) aren't in the table and stay in the displayed path verbatim.
- A new **MSI Dir** column on the right of the results list shows the original (unresolved) MSI Directory-table identifier — `PFiles64`, `INSTALLDIR`, etc. — so the audit-friendly raw value is visible alongside the human-readable path. Empty for top-level files.
- CSV export gains a `Container` column that's empty for top-level files and populated with the parent MSI's full path for extracted ones, plus an `MsiDirectoryId` column that mirrors the **MSI Dir** ListView column.
- The log file under `%APPDATA%\FileHasher\Logs\` appends ` | container: <path>` and ` | msi-dir: <id>` fields to each inner-file row.
- The existing **Scan all file types** checkbox in the Target group also controls which inner MSI files get hashed. When unchecked (the default), only `.exe` and `.msi` files inside the MSI are hashed — same rule that applies to folder scans. When checked, every inner file is hashed regardless of extension. The checkbox is enabled whenever the rule is meaningful: a folder is selected, OR a single `.msi` file is selected AND **Hash files inside MSI installers** is on. Otherwise it stays disabled (one-file scans of non-MSI files don't need a filter).
- Sidecar hash files are **not** written for inner files, even when **Write sidecar hash files** is on — the inner files live in a temp directory that's about to be deleted, and an orphan sidecar there would be meaningless. The CSV is the durable record.

## Security model

This feature handles potentially-untrusted MSI files. Every input is treated as adversarial. `MsiExtractor` enforces the following invariants (defaults shown; tunable via constructor parameters):

| Guard                        | Default                | Purpose                                                                                  |
|------------------------------|------------------------|------------------------------------------------------------------------------------------|
| Per-file size cap            | 2 GB                   | Refuses an MSI whose `File` table declares any single file larger than the cap.          |
| Total extracted size cap     | 5 GB                   | Sum of declared file sizes; defends against decompression-bomb MSIs.                     |
| Max file count               | 10,000                 | Caps the number of inner files; defends against inode-exhaustion / loop-blowup attacks.  |
| Minimum free disk            | 1 GB headroom          | Pre-extraction check that the temp drive can hold the projected payload plus a margin.   |
| Path-traversal guard         | always on              | Every extracted file's canonical path must remain strictly under the extract directory. Any escape is deleted and excluded. |
| Sibling-escape guard         | always on              | The contents of the extract dir's parent are snapshotted immediately before extraction; any new sibling that appears during the extraction and isn't the extract dir itself is deleted afterward. Catches escapes via `Directory.DefaultDir` payloads like `..\..\evil_dir` that land *outside* the extract dir, where the path-traversal guard above (which only enumerates *inside* the extract dir) cannot see them. |
| Reparse-point rejection      | always on              | Symlinks, junctions, and mount points found in the extracted tree are deleted before hashing. |
| Cryptographically random temp dir | always on         | `Path.GetRandomFileName()` produces 11-char random names; unpredictable to other processes. |
| Read-only MSI open           | always on              | The MSI database is opened `ReadOnly`; the underlying Windows Installer code does not run any installer logic. |
| Deferred cleanup             | `Dispose`-time         | Temp dir tree is removed (read-only flags first cleared) when the extractor is disposed. |

The user is **never** prompted to elevate solely because of MSI extraction; everything happens under the current process's privileges and against the per-user `%TEMP%` directory by default.

### What this protects against

- Decompression bombs (a 200 KB MSI claiming to expand to 5 TB).
- Path-traversal payloads (`../../../Windows/System32/evil.dll` in the `File` table).
- Sentinel reparse points dropped into the extracted tree (escape via junction/symlink redirection).
- Inode / loop exhaustion via huge file counts.
- Predictable temp-path probing by other processes.
- Stale sensitive content left in `%TEMP%` after a crash (best-effort cleanup minimizes this; OS temp-cleaning catches the rest).

### What this does **not** protect against

- **Vulnerabilities in `msi.dll` or cabinet.dll itself.** This feature relies on the security of the Windows Installer parser. A bug in the OS-shipped parser would be exploitable through this code path. We accept that trust boundary — the OS parser is the only practical way to read MSI databases on Windows.
- **Hash collisions / weak algorithms.** Hashes are MD5/SHA-1/SHA-256/SHA-512 as picked by the user, same as for non-MSI files. MD5/SHA-1 are not collision-resistant; that's a property of the algorithm, not of this feature.
- **Trust verification of inner files.** Authenticode signatures cover the outer MSI's PE-like wrapper. Inner files typically have no separate signature. Hashes here are useful for inventory and SBOM tracking, not for trust verification.

## Known limitations (v1, intentional)

- **MSI only.** EXE installers (NSIS, Inno Setup, InstallShield, WiX Burn bundles, self-extracting CABs, vendor-proprietary wrappers) are not handled. The branch could grow to cover them; that's a much larger scope and not part of this first cut.
- **No recursion.** If an inner file is itself an MSI, it is hashed as a regular file; its own contents are not decomposed. Same for EXE installers nested inside.
- **External cabinets.** If the MSI's `Media` table points at an external `.cab` file that isn't alongside the MSI, extraction will fail with the underlying WiX DTF error surfaced in the results as a `[WARN]` row.
- **Sort order in CSV.** Rows are sorted by `Container` then by `FilePath`, so all top-level files appear first (empty Container) and each MSI's inner files cluster together after. This is intentional but may not match every analyst's preference.

## Implementation map

Code added on this branch:

| File                                                | Change |
|-----------------------------------------------------|--------|
| `FileHasherApp/FileHasherApp.csproj`                | NuGet refs for `WixToolset.Dtf.WindowsInstaller` and `…Package`. |
| `FileHasherApp/HashOptions.cs`                      | New `DescendIntoMsi` bool. |
| `FileHasherApp/HashResult.cs`                       | New `Container` string? — populated for inner-file rows. |
| `FileHasherApp/MsiExtractor.cs`                     | **NEW**. Sandboxed extraction with the security guards listed above. |
| `FileHasherApp/HashWorker.cs`                       | After hashing an MSI, optionally call `MsiExtractor.ExtractAsync` and hash each inner file with `Container=msiPath`. |
| `FileHasherApp/MainForm.cs`                         | Checkbox in Options, options-snapshot wiring, results-row prefix + color, CSV `Container` column. |

## Testing notes

A FlaUI test class `MainFormMsiInnerScanTests` lives in `FileHasherApp.Tests/`, exercising the option's default state, on/off effect on the results count, the `[parent.msi]` prefix on inner-file rows, the dynamic enable-state of the AllTypes checkbox when an MSI is the target, the CSV's new `Container` / `MsiDirectoryId` columns, and the per-extraction temp-directory cleanup. Tests share the fixture MSI at `FileHasherApp.Tests/fixtures/msi-test.msi` (~7 MB) and assert on shape rather than exact contents, so the fixture can be regenerated without churning the test code.

Covered:

- `MsiChk_UncheckedByDefault` — option unchecked at app launch.
- `MsiInnerScan_OffByDefault_HashesMsiAsSingleFile` — MSI input + option off → exactly one row.
- `MsiInnerScan_OnProducesInnerRows` — MSI input + option on → more than one row.
- `MsiInnerScan_OnInnerRowsArePrefixedWithContainerName` — at least one row begins with `[msi-test.msi]`.
- `AllTypesChk_EnabledWhenMsiFileSelectedAndDescendOn` — AllTypes flips enabled when both conditions hold.
- `AllTypesChk_DisabledWhenMsiFileSelectedAndDescendOff` — AllTypes stays disabled when MSI scan is off.
- `MsiInnerScan_TempDirCleanedUpAfterRun` — no `FileHasher_msi_*` directories leak under `%TEMP%` after a normal run.
- `MsiInnerScan_CsvHasContainerAndMsiDirectoryIdColumns` — CSV header contains both new columns and at least one row populates Container.
- `MsiInnerScan_AllTypesOff_OnlyHashesExeAndMsiInnerFiles` — with AllTypes off, only the 6 `.exe` files inside the fixture surface as inner rows (7 total rows including the outer MSI).
- `MsiInnerScan_AllTypesOn_HashesEveryInnerFile` — with AllTypes on, all 9 inner files of the fixture surface (10 total rows including the outer MSI).
- `MsiInnerScan_WithSidecarsOn_OnlyWritesSidecarForOuterMsi` — sidecar writes for top-level files happen as usual; inner files generate no sidecars (they live in a temp dir that is deleted right after the inner-file pass).
- `MsiInnerScan_FolderWithMultipleMsis_EachExtractsAndCleansSeparately` — scanning a folder containing two MSIs produces 2 outer + 12 inner rows = 14 total (AllTypes off), and both per-MSI temp directories are cleaned up.

A second test class `MsiExtractorTests` exercises `MsiExtractor` directly as a unit (via `InternalsVisibleTo`), covering the security guards without needing the UI or adversarial MSI fixtures:

- `Cap_PerFileBytes_Tiny_AbortsBeforeExtraction` / `Cap_TotalBytes_Tiny_AbortsBeforeExtraction` / `Cap_FileCount_Zero_AbortsBeforeExtraction` / `Cap_MinFreeDisk_Absurd_AbortsBeforeExtraction` — instantiate `MsiExtractor` with absurdly tight caps, run against the benign fixture, assert each cap path throws the right exception before the temp directory is created. No malicious fixture needed.
- `IsPathOutsideDirectory_Cases` (theory) — drives the path-traversal guard with synthetic absolute paths covering inside/outside/sibling-with-shared-prefix/`..` traversal scenarios.
- `IsPathOutsideDirectory_TrailingSeparatorRequired_DistinguishesPrefixSiblings` — captures the calling convention that the canonicalized directory must carry a trailing separator (the production code does this; the test documents why).
- `IsReparsePoint_RegularFile_ReturnsFalse` — sanity check for the negative case.
- `IsReparsePoint_Symlink_ReturnsTrue` — creates an actual symlink in the test temp directory and asserts the reparse-point bit is detected. Requires the test process to have `SeCreateSymbolicLinkPrivilege` (admin or Developer Mode); skip-by-early-return with a stderr message when the privilege is missing.
- `Dispose_RemovesExtractDirectory_AfterSuccessfulExtraction` / `ExtractAsync_NonExistentMsi_ThrowsFileNotFound` — lifecycle sanity.
- `ResolveDisplayPath_Cases` — Theory covering 13 inputs against the MSI Directory-table identifier resolver: standard `PFiles64`/`ProgramFiles64Folder` → `Program Files`, `PFiles`/`ProgramFilesFolder` → `Program Files (x86)`, `WindowsFolder`/`SystemFolder`/`FontsFolder`/`CommonFiles64Folder` → their expected Windows shell paths, `TARGETDIR` → stripped entirely (empty identifier), custom non-well-known identifiers (e.g. `INSTALLDIR`) → passed through verbatim with the identifier still surfaced for audit display, plus bare-filename and empty-input edge cases.

These required a small refactor on `MsiExtractor.cs`: the reparse-point and path-traversal inline guards in `ExtractAsync`'s post-extraction loop were extracted into `internal static` helpers (`IsReparsePoint` and `IsPathOutsideDirectory`). Production behavior is unchanged — the loop calls the same predicates, in the same order, against the same data — they're just addressable from tests now.

Tier 3 — end-to-end adversarial MSI synthesis — is also now covered. The `MaliciousMsiBuilder` test helper copies the benign template fixture to a temp file and mutates specific table rows in place (via WiX DTF's `Database` in `Direct` open mode), producing a structurally well-formed but content-hostile MSI. Four tests exercise it against `MsiExtractor.ExtractAsync` directly:

- `Adversarial_PathTraversalInFileName_DoesNotEscapeSandbox` — flip the first File row's `FileName` to `..\..\evil.exe`, run the full extraction, assert every returned path is canonically under the extract dir, and confirm directly on disk that no `evil.exe` was created in the extract dir's parent within the last 30 seconds.
- `Adversarial_PathTraversalInDirectoryDefaultDir_DoesNotEscapeSandbox` — flip a child Directory row's `DefaultDir` to `..\..\evil_dir`. Same pair of assertions, walking the extract dir's parent and grandparent for any `evil_dir` directory created recently.
- `Adversarial_OversizeDeclaredFileSize_TripsPerFileCap` — flip the first File row's `FileSize` to `int.MaxValue` (~2 GB, just above the per-file cap). Assert `InvalidDataException` and that the temp dir was never created.
- `Adversarial_TooManyFiles_TripsFileCountCap` — insert 10,000 synthetic File rows so the total exceeds the count cap. Same exception/no-temp-dir assertions.

The path-traversal tests are the high-value ones: they additionally cover "feeding adversarial input to the real WiX DTF call doesn't slip past our guards" on top of the Tier-2 helper unit tests' coverage of the guard logic itself. The two cap tests are mostly insurance — they verify the cap-enforcement path works end-to-end from an actual-MSI input rather than from artificially-tight constructor parameters.

Still NOT covered — known limitations, not bugs:

- `MsiInnerScan_TempDirCleanedUpOnCancel` — Stop button mid-extraction cleanup. `InstallPackage.ExtractFiles` is synchronous and not cancellation-aware, so the cancel signal only takes effect between MSIs, never within one. The Dispose-time cleanup still runs (the `using (extractor)` ensures it), so the cleanup itself is correct; there's just no reliable way to test the mid-extraction trigger.

## Roadmap to merge back

The intended path for closing out this branch:

1. Add the test suite enumerated above.
2. Run a full local pipeline (audit + build + test) to confirm the WiX DTF dependency doesn't introduce vulnerable transitives (the audit job will surface any CVEs).
3. Capture the binary-size delta on the shipped self-contained exe (expected ≤ ~2 MB total over main).
4. Decide on default behavior: stay opt-in (recommended), or change to opt-out for `.exe`/`.msi` scans only.
5. Merge into `main` behind the opt-in checkbox.
6. Fold this file's content into the main `README.md`'s **Options** subsection (replacing the "experimental" framing with regular-feature framing).
7. Delete this file as the same commit that merges to main.

Until then: keep `main` clean of these changes, develop here, and tag from this branch only with pre-release-style tags (e.g., `v0.2.0-msi.1`) that do **not** match the CI's release-tag regex `/^v[0-9]+\.[0-9]+\.[0-9]+$/`. That keeps the feature out of the production release path while it's stabilizing.
