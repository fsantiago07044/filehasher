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

The existing FlaUI test suite under `FileHasherApp.Tests/` does not yet cover this feature. Reasonable additions before a merge-back to `main`:

- `MsiInnerScan_OffByDefault_NoExtraRows` — sanity check that the option is unchecked at launch and that an MSI input produces exactly one row.
- `MsiInnerScan_OnProducesInnerRowsWithContainer` — given a small test-fixture MSI checked into `FileHasherApp.Tests/fixtures/`, the results contain N+1 rows (1 outer + N inner) with the inner rows showing the parent MSI in the Container field.
- `MsiInnerScan_CapEnforced` — given an MSI that declares a file size over the cap, the run completes, the MSI's own row is present, and a `[WARN]` row reports the cap violation.
- `MsiInnerScan_PathTraversalEntry_NotWritten` — given a deliberately-malicious test fixture, verify no file lands outside the extract dir.
- `MsiInnerScan_TempDirCleanedUp_OnCancelAndOnException` — assert the `FileHasher_msi_*` directory under `%TEMP%` is gone after a cancelled run and after a forced-throw run.

These tests need a small test-fixture MSI checked into the repo. Building one with WiX or `msitools` is the simplest route.

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
