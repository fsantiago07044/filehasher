# FileHasher CLI: design proposal

**Status: design agreed, nothing built.** Written 2026-09-10.

## Decisions

Taken by Fabian on 2026-09-10:

| # | Question | Decision |
| --- | --- | --- |
| 1 | Core cross-platform or Windows-only? | **Cross-platform** |
| 2 | `--json` streaming or one document? | **Streaming**, one object per line |
| 3 | Bundled with the app, or separate? | **Separate `dotnet tool` package** |
| 4 | Recursion control? | **Yes**, the CLI gets a recurse option |
| 5 | `verify --fail-on`? | **Yes** |

Decisions 4 and 5 are additions to the command surface below. Decision 1 turned
out cheaper than this document first assumed, and its consequences for version
pinning, reproducibility and signing are worked through at the end.

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

**Core targets plain `net10.0`. No multi-targeting is needed**, which is the
part this document originally got wrong.

`MsiExtractor` depends on `WixToolset.Dtf.WindowsInstaller`, and the assumption
was that this forced a Windows TFM. It does not: that package ships
`netstandard2.0`, so it restores and compiles against `net10.0` on any host. It
only fails at *runtime*, where it P/Invokes `msi.dll`.

So the shape is one cross-platform Core assembly, with the MSI path gated at
runtime:

- annotate `MsiExtractor` `[SupportedOSPlatform("windows")]`, which is the
  accurate annotation regardless and keeps the CA1416 platform analyzer quiet
- have the CLI reject `--msi-inner` on non-Windows with a clear message rather
  than failing inside a P/Invoke

That buys a CLI that runs on Linux and macOS CI runners, where most pipelines
actually are, and lets it verify sidecars written by the macOS app on the
machine that wrote them. The only capability missing off Windows is inner-MSI
hashing, which is meaningless there anyway.

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
  -r, --recurse, --recursive                 descend into subdirectories
                                             (default: top directory only)
      --max-depth <n>                        limit recursion depth; implies -r
      --follow-symlinks                      follow directory symlinks
                                             (default: off, see below)
      --fail-on <list>                       conditions that force exit 1
      --json                                 machine-readable output
  -q, --quiet                                errors and the summary only
      --version, --help
```

### --fail-on

Applies to both verbs (decided 2026-09-11). A comma-separated list of
conditions; anything listed forces exit code 1, anything not listed is still
reported but does not fail the run. The point is that a pipeline decides for
itself what counts as a failure, instead of inheriting our opinion.

| Token | `hash` | `verify` | Means |
| --- | --- | --- | --- |
| `unreadable` | yes | yes | a file could not be opened or read |
| `mismatch` | no | yes | the hash does not match the sidecar |
| `missing` | no | yes | a sidecar names a file that is not there |
| `no-sidecar` | no | yes | a file has no sidecar at all |
| `empty` | yes | yes | the run matched no files |
| `any` | yes | yes | every condition applicable to that verb |
| `none` | yes | yes | report only; never exit 1 |

Defaults are chosen to reproduce the exit-code table exactly, so adding the
option changes nothing for anyone who does not pass it:

```
hash     --fail-on unreadable
verify   --fail-on mismatch,missing,unreadable
```

Two of these are worth having even though the table above does not currently
produce them. `no-sidecar` turns `verify` into a coverage gate, which is the
natural way to assert "every artifact in this directory is accounted for".
`empty` catches the silent-success failure mode that bites hardest in CI: a
wrong path, or a glob that matched nothing, currently exits 0 and looks like a
clean run. `--fail-on none` is the inverse, for a reporting job that should
collect results without failing the build.

Usage errors (2) and run failures (3) are unaffected. `--fail-on none` does not
suppress them, because they mean the tool did not do its job, which is not a
finding the caller gets to ignore.

### Recursion

**Default: top directory only, recursion opt-in via flag** (decided
2026-09-11).

Note that this is a real behavioural fork, and it should be documented for
anyone migrating:

- **The GUI is already non-recursive.** `HashWorker` and `SidecarVerifier` both
  call `Directory.EnumerateFiles(dir)` with no `SearchOption`, which is
  `TopDirectoryOnly`.
- **`filehasher.ps1` recursed, always.** Its `Get-ChildItem -Recurse` had no
  opt-out. So the recursion was quietly dropped in the port to the GUI, and a
  script author moving from the `.ps1` to the CLI will hash fewer files than
  before unless they pass `-r`.

Because of that second point the CLI should say so rather than let it pass
silently: when a non-recursive run finishes and subdirectories were present but
skipped, print a hint to stderr (suppressed by `-q`, absent from `--json`):

```
note: 3 subdirectories were skipped. Use -r to include them.
```

#### Flag naming

| Candidate | Precedent | Verdict |
| --- | --- | --- |
| `-r` | universal on both sides | **short flag** |
| `--recurse` | PowerShell `Get-ChildItem -Recurse`, and the origin script | **canonical long flag** |
| `--recursive` | POSIX `grep -r`, `cp -R`, `chmod -R` | **alias**, same option |
| `--max-depth <n>` | `find -maxdepth`, `du --max-depth` | **add**, implies `-r` |
| `--depth <n>` | few precedents | reject: reads as "exactly this depth" |
| `--no-recurse` | needed only if the default were on | not needed |

The `--recurse` / `--recursive` split is the only genuinely contested one: the
PowerShell spelling matches this tool's heritage, the POSIX spelling matches
what a Linux or macOS user will type first, and the CLI is now cross-platform,
so both audiences are real. Defining both as aliases of one option costs a
single extra string and removes the question, which is better than being right
about it.

`--max-depth 1` means the starting directory plus one level down. Passing it
without `-r` implies recursion rather than erroring; nobody who typed
`--max-depth 2` meant "do not recurse".

#### Symlinks

Recursion plus cross-platform means symlink loops are now reachable, which they
never were in the Windows-only GUI. `--follow-symlinks` is therefore **off by
default**: directory symlinks are reported and not descended into. When it is
on, track visited directories by device and inode so a cycle terminates rather
than running until the path length blows up.

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

## Version pinning and reproducibility

Going cross-platform costs less here than expected, because cross-platform
*targets* do not require cross-platform *builds*.

**The SDK pin is untouched.** `global.json` pins 10.0.400 with
`rollForward: disable`, and every artifact is still cross-compiled from the one
Windows runner with `-r linux-x64`, `-r osx-arm64` and so on. One SDK produces
everything, which is a marginally stronger guarantee than today rather than a
weaker one. Only building *on* Linux would strain it, since that host would then
need exactly 10.0.400.

**`RuntimeFrameworkVersion` multiplies but stays one knob.** A self-contained
publish resolves a RID-specific runtime pack
(`Microsoft.NETCore.App.Runtime.win-x64`, `.linux-x64`, `.osx-arm64`), and the
single `10.0.11` property pins all of them, because those packs ship together at
matching versions. Two things get simpler: the CLI needs only
`Microsoft.NETCore.App`, not `Microsoft.WindowsDesktop.App`, so it pins half of
what the GUI does; and Core is a library rather than a self-contained app, so it
needs no `RuntimeFrameworkVersion` at all.

**Determinism is unaffected in mechanism, but per-RID in effect.**
`Deterministic=true` and `ContinuousIntegrationBuild=true` behave identically for
every RID. But a `linux-x64` binary will never hash the same as a `win-x64` one,
so the "Reproducible builds" recipe in the main README must name the RID it
applies to. On the other hand, a single-target Core means the *same* Core
assembly ships inside both the GUI and the CLI: one engine, one hash to verify.

**Do not add `PublishTrimmed` or AOT in the same change.** Trimming is RID- and
analyzer-sensitive and would weaken a reproducibility story that is currently
clean. Neither is used today; introducing one alongside the RID expansion would
make any regression hard to attribute.

## Signing, and what it means for claims already published

This is the real cost of decision 1, and it is not a build problem.

**Authenticode signs Windows PE files only.** The HSM pipeline signs the exe and
the MSI; it cannot sign a `linux-x64` or `osx-arm64` binary. macOS artifacts
would want `codesign` plus notarization under a Developer ID certificate, which
is a different certificate from the App Store one used for the Mac app.

That collides with wording that is live today. The support page says every
release is code-signed by FSP Productions, LLC, and the 0.4.0 announcement says
every channel installs the identical binary, code-signed by FSP Productions,
LLC. Both are true of the Windows app and stop being true the moment an unsigned
Linux binary ships under the same product name. **Whichever way the signing
question lands, those two sentences need revisiting before a cross-platform
artifact is published.**

### Packaging: self-contained, per RID

**Decided 2026-09-11: self-contained per-RID packages.** Fabian does not want to
depart from the self-contained model the GUI already uses, and the consistency
argument is strong: every FileHasher artifact today runs without installing a
runtime first, and a CLI that needs one would be the odd one out.

This is compatible with shipping as a `dotnet tool`, but only just, and only
because of the .NET version already pinned here. **RID-specific and
self-contained tools require SDK 10 or later**, and `global.json` pins 10.0.400.
Setting `RuntimeIdentifiers` alongside `PackAsTool` makes the SDK build a
self-contained package per RID, and `dotnet tool install` selects the right one
automatically, so users see no difference:

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>filehasher</ToolCommandName>
<RuntimeIdentifiers>win-x64;linux-x64;osx-arm64;osx-x64;any</RuntimeIdentifiers>
```

**`any` is in that list deliberately (decided 2026-09-11).** It emits a
framework-dependent package alongside the four self-contained ones, which the
SDK falls back to on any platform not enumerated: linux-arm64 CI runners,
Alpine/musl images, an arm64 Windows box, anything that appears later. It costs
one list entry, changes nothing for the four first-class RIDs, and means an
unanticipated platform degrades to "needs the .NET 10 runtime installed"
instead of "no package available".

Two consequences worth writing down now, since the fallback is easy to forget
once it exists:

- **It is a framework-dependent package**, so it needs a .NET 10 runtime on the
  target. That is the trade being made in exchange for the coverage; it is not
  a silent second-class self-contained build.
- **It is a `.dll` with no native apphost**, so there is no PE file to
  Authenticode-sign even when it lands on Windows. A Windows user who somehow
  resolves the fallback instead of `win-x64` gets an unsigned payload. Unlikely,
  since `win-x64` is enumerated and wins, but the signing matrix below should
  be read as describing the RID-specific packages.

**Do not enable `PublishAot`,** even though Microsoft's page presents RID-specific,
self-contained and AOT together and its second example turns AOT on. Self-contained
is not the same as trimmed or AOT-compiled. AOT would undermine the byte-for-byte
reproducibility story the README documents, and the MSI reader leans on a library
whose reflection behaviour under AOT has not been tested here. Self-contained
without trimming or AOT keeps the current guarantees intact.

One upside: the CLI's per-RID packages should be considerably smaller than the
GUI's 45.8 MB installer, because the CLI needs only the `Microsoft.NETCore.App`
runtime pack and not `Microsoft.WindowsDesktop.App`.

### Signing: the resulting matrix

Self-contained means a native host per platform, so the signature story differs
by artifact. This is the discrepancy to document rather than paper over:

| Artifact | Host format | Authenticode | Other coverage |
| --- | --- | --- | --- |
| GUI `.exe`, `.msi`, `.zip` | PE | **Yes**, FSP Productions, LLC via the HSM | SHA-256 sidecar |
| CLI `win-x64` | PE (apphost) | **Yes**, same pipeline | nuget.org repository signature |
| CLI `linux-x64` | ELF | **No.** Authenticode does not apply to ELF | nuget.org repository signature |
| CLI `osx-arm64` / `osx-x64` | Mach-O | **No.** Would need Apple `codesign` + Developer ID + notarization, a different certificate from the App Store one | nuget.org repository signature |
| Every NuGet package | n/a | n/a | nuget.org repository signature, applied on push |

So: Windows artifacts carry the company signature; Linux and macOS artifacts do
not and cannot without new certificates and tooling. Every package regardless of
platform carries nuget.org's repository signature, and every release keeps its
published SHA-256 checksums, which is the integrity mechanism that actually
travels across all three platforms.

### Documentation to write at release time, not before

The README as it stands is **already correct** and needs no pre-emptive edit: its
signing language is scoped to "the released `.exe`" and to the Authenticode
signature specifically, so it makes no blanket claim that the CLI would falsify.

At release, three places need wording:

1. **README**, a new CLI section carrying the matrix above in prose: Windows
   binaries Authenticode-signed, Linux and macOS binaries not, all packages
   repository-signed by nuget.org, all artifacts covered by SHA-256 checksums.
2. **Support page**, whose "Every release is code-signed by FSP Productions,
   LLC" becomes true of the Windows app and the Windows CLI, with the
   cross-platform CLI binaries described honestly.
3. **The 0.4.0 announcement**, which says every channel installs the identical
   binary code-signed by FSP Productions, LLC. That sentence is about the GUI
   and stays true of it; the cleanest fix is to scope it explicitly to the
   Windows app rather than edit history misleadingly.

Fabian's framing, agreed 2026-09-10: describe the CLI as carrying a different
signature and a different signing process, consistent with nuget.org's
repository-signing policy, rather than claiming one uniform signature across
everything.

### Can the NuGet package be author-signed?

In principle yes, in practice not with the current topology.

The certificate qualifies: NuGet requires a code-signing certificate with an RSA
key of 2048 bits or more, chaining to a root trusted by default on Windows, and
self-issued certificates are rejected. The FSP Productions OV certificate meets
all of that.

The obstacle is key access. `dotnet nuget sign` can reach a private key exactly
two ways: `--certificate-path`, meaning a file that contains the private key, or
`--certificate-store-*`, meaning the Windows certificate store. The key lives in
a USB HSM attached to the Linux signer, reached over PKCS#11 by `osslsigncode`.
Neither route reaches it:

- a PFX export is impossible, and would defeat the HSM even if it were not
- the Windows certificate store route needs the token attached to a Windows host
  with the vendor's CSP/KSP driver, and moving the token there would break the
  exe and MSI signing that depends on it being on the Linux box

Buying a second, file-based certificate is not an escape either: since 2023 the
CA/Browser Forum has required code-signing private keys to be held in certified
hardware, so a public CA will not issue a soft PFX.

Two workable directions, neither urgent:

1. **Do not author-sign.** nuget.org applies a **repository signature** to every
   package it accepts, which gives consumers an integrity guarantee and
   provenance from nuget.org. Combined with the published SHA-256 sidecars,
   which cover every artifact on every platform, that is a reasonable baseline.
2. **Move to a cloud signing service** (Azure Trusted Signing, DigiCert
   KeyLocker, SSL.com eSigner and similar). These hold the key in a cloud HSM and
   are drivable from CI without a physical token, which would cover Authenticode
   and NuGet from the same place. That is a procurement decision well beyond this
   CLI.

Worth knowing before choosing: registering a certificate with nuget.org is a
**commitment, not a per-package choice**. Once the fingerprint is registered,
nuget.org requires every subsequent package from that account to carry that
signature. Turning it on is easy; turning it off later is disruptive.

**Recommendation: ship unsigned to NuGet initially** and lean on the repository
signature plus the sidecars, then revisit if cloud signing is ever adopted for
Authenticode anyway.

## What this is not

Not a rewrite. The GUI keeps its current behaviour exactly; the refactor moves
code between projects without changing it, and the existing FlaUI suite is the
guard that proves so.

## Still open

The five questions this document opened with are answered above. What remains
before implementation:

1. ~~The two published sentences about signing.~~ Settled: they are revisited at
   release time, worded per artifact (see "Agreed wording approach"). Much
   easier now that a framework-dependent tool can carry the existing signature.
2. ~~Whether to publish self-contained per-RID binaries.~~ Settled 2026-09-11:
   yes, self-contained per RID, with the signing consequences documented rather
   than avoided.
3. ~~CLI versioning.~~ Settled 2026-09-11: the CLI ships off the same tag as
   the app and carries the same version number. One tag, one version, one
   changelog entry. A CLI release with no CLI-visible change is a cheaper
   problem than two version lines to explain.
4. ~~Whether `hash` also gains `--fail-on`.~~ Settled 2026-09-11: yes, and
   everywhere else applicable. See the `--fail-on` section.
5. ~~The recurse option's spelling and default.~~ Settled 2026-09-11: opt-in
   `-r` / `--recurse` / `--recursive`, plus `--max-depth`. (An earlier draft of
   this line claimed recursive-by-default "matches the GUI's Windows
   behaviour". It does not: the GUI is `TopDirectoryOnly`. The `.ps1` was the
   recursive one.)
