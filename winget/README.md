# winget distribution

FileHasher is distributed through the [Windows Package Manager Community
Repository](https://github.com/microsoft/winget-pkgs) so end users can run:

```powershell
winget install FSPProductions.FileHasher
```

winget hosts nothing itself — each version is a set of three small YAML
manifests in `microsoft/winget-pkgs` that pin the public download URL and
SHA256 of that version's signed MSI on the GitHub mirror
(`fsantiago07044/filehasher` releases). The MSI is produced and signed by the
tag pipeline (see [`../ci/README.md`](../ci/README.md)); the authoring lives
in [`../installer/FileHasher.wxs`](../installer/FileHasher.wxs).

**Per-release submission is automated.** The `winget-update` pipeline job
(tag pipelines only, after `mirror-github`) runs `wingetcreate update` on the
Windows runner, patches the version-specific locale fields (fresh
`ReleaseNotesUrl`, drops carried-over `ReleaseNotes`), and opens the PR from
the `fsantiago07044` winget-pkgs fork. Prerequisites are one-time
(`wingetcreate.exe` on the runner, `WINGET_PAT` CI/CD variable — both in
[`../ci/README.md`](../ci/README.md)). After each release, just watch the PR
the job log links to. Everything below is the **manual fallback** for when
that job yellow-flags (its generated manifests are attached as job artifacts,
so `wingetcreate submit <folder>` on a fixed copy is usually all a recovery
takes) — or for a first-time submission of a brand-new package.

**Every manual step in this document runs on a Windows box** (build host or
any Windows 10/11 machine with winget). Nothing here touches the Mac.

## One-time setup

```powershell
winget install Microsoft.WingetCreate
# Enable installing from local manifest files (admin PowerShell, once per machine):
winget settings --enable LocalManifestFiles
```

You also need a GitHub fine-grained PAT from the `fsantiago07044` account for
`wingetcreate` to fork `microsoft/winget-pkgs` and open the PR (Public
repositories access is enough). Store it in Bitwarden; pass it with `-t` or
let `wingetcreate` prompt and cache it.

## MSI validation

ICE validation is automatic: the `package-msi` pipeline job runs
`wix msi validate` on the Windows runner and fails the pipeline on ICE
errors, so any MSI that reaches a release has already passed. No manual
validation step is needed; the functional smoke test below is the part that
still deserves human eyes.

## Manual fallback workflow (per release)

Run after the tag pipeline's `mirror-github` job has finished (the manifest
points at the GitHub Release asset, so it must exist and be final first —
GitHub releases on this repo are immutable).

1. **Fetch the MSI** from the GitHub Release (or from
   `/src/filehasher/signed-builds/` if that's handier) and confirm its hash
   matches the `.sha256` sidecar.

2. **Smoke-test the real install path** — ideally on a scratch VM, not the
   build host:

   ```powershell
   msiexec /i FileHasher-X.Y.Z.msi /qn    # silent, exactly how winget runs it
   ```

   Check: `FileHasher` appears in Start Menu and launches; **Settings → Apps**
   shows FileHasher X.Y.Z, publisher FSP Productions, LLC, with the app icon;
   upgrading over the previous version leaves exactly one entry; uninstall
   removes the Program Files folder and the shortcut.

3. **Generate + submit the manifests.** First version ever:

   ```powershell
   wingetcreate new https://github.com/fsantiago07044/filehasher/releases/download/vX.Y.Z/FileHasher-X.Y.Z.msi
   ```

   Interactive prompts — answer per the templates in
   [`manifest-templates/`](manifest-templates/) (identifier
   `FSPProductions.FileHasher`, license MIT, etc.). Subsequent versions:

   ```powershell
   wingetcreate update FSPProductions.FileHasher `
     -v X.Y.Z `
     -u https://github.com/fsantiago07044/filehasher/releases/download/vX.Y.Z/FileHasher-X.Y.Z.msi `
     --submit
   ```

   `wingetcreate` downloads the MSI, computes the SHA256, extracts the
   ProductCode, writes the three manifests, validates them, and opens the PR
   against `microsoft/winget-pkgs`.

   To inspect before submitting, drop `--submit` (manifests land in
   `.\manifests\`), then validate and test-install locally:

   ```powershell
   winget validate --manifest .\manifests\f\FSPProductions\FileHasher\X.Y.Z\
   winget install  --manifest .\manifests\f\FSPProductions\FileHasher\X.Y.Z\
   wingetcreate submit .\manifests\f\FSPProductions\FileHasher\X.Y.Z\
   ```

4. **Watch the PR.** winget-pkgs CI validates schema, URL, hash, and runs a
   Defender scan; moderators may ask questions on a first submission. Once
   merged (hours for updates, up to a few days for the first version), confirm:

   ```powershell
   winget source update
   winget search FSPProductions.FileHasher
   winget upgrade FileHasher    # on a machine with the old version
   ```

## Notes

- **PackageVersion must equal** the tag version, the csproj `<Version>`, and
  the MSI ProductVersion. The pipeline already enforces tag == csproj; the
  MSI stamps the same value. A mismatch is the most common winget-pkgs
  validation failure.
- **ProductCode changes every build** by design (`MajorUpgrade` authoring), so
  each version's installer manifest pins its own ProductCode. Never reuse a
  previous version's value.
- The `manifest-templates/` files are reference copies so the manifest content
  is reviewable in this repo; the authoritative manifests are whatever is
  merged into `microsoft/winget-pkgs`. If a static field changes (publisher
  URL, description, license), update the template here and let `wingetcreate`
  prompt/carry it into the next submission.
- wingetcreate's interactive prompts (first submission, 2026-07) had two
  gotchas worth remembering: the PackageIdentifier auto-derived from the
  publisher name came out as `FSPProductions,LLC.FileHasher` (comma baked in)
  and had to be corrected manually, and the Tags prompt never splits input
  into list items regardless of delimiter — enter a single placeholder tag,
  decline auto-submit, hand-edit the saved locale YAML, then
  `wingetcreate submit <folder>`. The automated `winget-update` job avoids
  both by using non-interactive `wingetcreate update`, which carries fields
  over from the previous merged manifests.
