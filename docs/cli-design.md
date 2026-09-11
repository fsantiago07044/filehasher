# FileHasher CLI: design proposal

**Status: proposal, nothing built.** Written 2026-09-10 to think through a
headless FileHasher. Decisions marked **OPEN** are Fabian's to make.

## Why

Every channel FileHasher now ships on (Microsoft Store, winget, Chocolatey,
Scoop) delivers the same thing: a GUI app. So the app cannot be used where
file-integrity checking is arguably most valuable, which is unattended:

- a CI pipeline hashing build output, or verifying that a dependency it just
  downloaded matches a published checksum
- a scheduled job re-verifying an archive and failing loudly when a file rots
- a server with no interactive desktop

`filehasher.ps1` used to fill that role and no longer does; it is now marked
historical in the README. A real CLI is the honest replacement.

## What already exists

Better than expected. Of the app's eleven source files, only three touch
WinForms:

| Portable | UI-bound |
| --- | --- |
| `HashWorker` (308), `SidecarVerifier` (285), `MsiExtractor` (439), `HelpContent` (228), `Logger` (80), `HashOptions` (20), `HashResult` (18), `VerifyResult` (33) | `MainForm` (1547), `HelpForm` (219), `Program` (13) |

`HashWorker` and `SidecarVerifier` already report progress through
`event Action<...>` and `IProgress<int>` rather than touching controls, so they
were written in a shape a CLI can drive. They are `internal`; the main work is
making the surface `public` in a shared project.

## Architecture

Extract **`FileHasher.Core`**, referenced by both `FileHasherApp` (WinForms) and
a new `FileHasher.Cli`. One hashing engine, one sidecar parser, one MSI reader,
so the two front ends cannot drift. That matters more here than in most apps:
the whole product promise is that a hash written by one FileHasher verifies in
another.

**OPEN, and the decision with the longest reach: does Core target
`net10.0-windows`, or multi-target `net10.0;net10.0-windows`?**

`MsiExtractor` depends on `WixToolset.Dtf.WindowsInstaller`, a managed wrapper
over the Windows Installer API, so inner-MSI hashing is Windows-only and always
will be. Everything else (hashing, sidecars, CSV, logs) is plain .NET.

- *Windows-only Core* is simpler: one target, no conditional compilation, MSI
  support everywhere.
- *Multi-targeted Core* makes the CLI run on Linux and macOS, with `--msi-inner`
  unavailable off Windows. That is what makes it usable on an ordinary Linux CI
  runner, which is where most pipelines actually run, and it would let the CLI
  verify sidecars written by the macOS app on the machine that wrote them.

The cross-platform option costs `#if`/partial-class work around one file. It is
the more valuable answer if the CLI is for automation, which is the premise.

## Command surface (mockup)

Two verbs, mirroring the two things the app does.

```
filehasher hash   <path> [options]
filehasher verify <path> [options]
```

### hash

```
  -a, --algorithm <md5|sha1|sha256|sha512>   default: sha256
      --all-types                            include every file type
                                             (default: .exe and .msi only)
      --sidecar                              write sidecar hash files
      --sidecar-ext <.ext>                   default follows the algorithm
      --sidecar-format <sum|hash|extended>   default: sum
      --csv <path>                           write results as CSV
      --metadata                             include size and modified time
      --msi-inner                            also hash files inside .msi
                                             (Windows only)
```

### verify

```
      --sidecar-ext <.ext>                   default: .sha256
      --all-types                            audit every file for a missing
                                             sidecar, not just .exe/.msi
      --csv <path>
```

### shared

```
      --json                                 machine-readable output
  -q, --quiet                                errors and the summary only
      --version, --help
```

## Sample sessions

```
$ filehasher hash ./dist --sidecar
FileHasher 0.5.0

  dist/Setup-1.2.0.msi      9f2a1c...  62.9 MB
  dist/Setup-1.2.0.exe      3b71de...  45.8 MB

2 files hashed, 2 sidecars written, 0 errors.  (1.4s)
```

```
$ filehasher verify ./archive --all-types
FileHasher 0.5.0

  OK            archive/2024-photos.zip
  MISMATCH      archive/ledger.db
                  expected 8c4f21...  actual 1d90ab...
  MISSING FILE  archive/old-report.pdf
  NO SIDECAR    archive/notes.txt

1 OK, 1 mismatch, 1 missing file, 1 without a sidecar.
$ echo $?
1
```

## Exit codes

The single most important CLI-specific decision, because it is the whole point
of running in a pipeline. A verification that finds corruption must fail the
build, and it must be distinguishable from the tool itself breaking.

| Code | Meaning |
| --- | --- |
| 0 | completed, nothing wrong |
| 1 | verification found mismatches, missing files, or unreadable files |
| 2 | usage error: bad arguments, path does not exist |
| 3 | the run failed: aborted, or an unexpected error |

`hash` returns 1 only if some file could not be read. It never returns 1 for
doing its job.

## Output

Human-readable by default. `--json` emits one object per run, with a `files`
array and a `summary`, so a pipeline can branch on the detail rather than
scraping the table. CSV stays as it is today, since it is already a documented
format shared with the GUI and the macOS app.

**OPEN:** should `--json` stream one object per line (easier on huge trees) or
emit a single document (easier to consume)? Streaming is the better fit for a
tool that can walk a hundred thousand files.

## Distribution

A .NET CLI has an obvious home the GUI does not: **NuGet as a `dotnet tool`**.

```
dotnet tool install -g FileHasher.Cli
```

That is the idiomatic way CI pipelines acquire .NET tooling, it needs no
installer, and it costs one extra pipeline step. The existing four channels
could also carry it later, but they are aimed at desktop users, who already
have the app.

**OPEN:** ship the CLI inside the existing package (one install, two
executables) or as a separate product? Separate is cleaner for the `dotnet
tool` route and keeps the GUI download small, at the cost of a second thing to
version. Versioning them together from the one repo and tag is probably the
least confusing answer.

## What this is not

Not a rewrite. The GUI keeps its current behaviour exactly; the refactor moves
code between projects without changing it, and the existing FlaUI suite is the
guard that proves so.

## Open questions, collected

1. Cross-platform Core, or Windows-only?
2. `--json` streaming or single document?
3. CLI bundled with the app, or a separate `dotnet tool` package?
4. Does the CLI need `--recurse`/`--no-recurse`, or does it inherit the app's
   always-recursive behaviour? The app is always recursive on Windows; a CLI
   used in scripts may want to limit depth.
5. Should `verify` gain a `--fail-on <mismatch|missing|no-sidecar>` switch, so a
   pipeline can decide whether a missing sidecar is an error or a warning?
