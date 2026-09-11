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

### Correction: a framework-dependent tool is signable with what already exists

The section above was written assuming cross-platform meant shipping native
binaries. Combined with decision 3, it mostly does not.

A **framework-dependent `dotnet tool` ships no native code**: the nupkg contains
IL assemblies and a manifest. Managed .NET assemblies are PE32+ files, verified
against this repo's own build output, which means `osslsigncode` on the existing
Linux signer can Authenticode-sign them with the existing HSM, exactly as it
signs `FileHasher.exe` today. So:

- the assemblies inside the package carry the FSP Productions signature, from
  the pipeline that already exists, with no new infrastructure
- the package itself carries nuget.org's repository signature, applied
  automatically on push
- nothing ships unsigned, and the published claims need clarifying rather than
  retracting

The gap only reopens for **self-contained per-RID executables** (ELF on Linux,
Mach-O on macOS), whether published beside the nupkg or via a RID-specific
self-contained tool package. Those cannot be Authenticode-signed at all.

**So: ship the CLI framework-dependent.** The cost is that the target machine
needs a .NET runtime, which on CI is either already present or one setup step
away. One detail for the eventual wording: `dotnet tool install` generates a
launcher shim on the user's machine at install time, and that shim is created
locally and is unsigned. That is inherent to the tool model.

### Agreed wording approach

Fabian's position, 2026-09-10: the support page and the 0.4.0 post will be
revisited when the tool is actually released, not before, and the framing will
be that the CLI carries a different signature and a different signing process,
consistent with nuget.org's repository-signing policy, rather than claiming one
uniform signature across everything. That is honest per artifact and avoids
rewriting live pages for something not yet shipped.

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
2. Whether to publish self-contained per-RID binaries at all, beside the nupkg.
   That is the one choice that would reintroduce genuinely unsigned artifacts,
   so it should be a deliberate decision rather than a default.
3. Whether the CLI is versioned with the app off the same tag (probably yes,
   least confusing) or gets its own version line.
4. Whether `hash` should also gain `--fail-on`, or whether exit code 1 for
   unreadable files is enough.
5. The recurse option's spelling and default: `--recurse`/`--no-recurse` with
   recursive as the default matches the GUI's Windows behaviour, but a
   depth-limited `--depth N` may serve scripts better.
