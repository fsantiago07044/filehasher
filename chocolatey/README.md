# Chocolatey distribution

FileHasher is distributed through the [Chocolatey Community
Repository](https://community.chocolatey.org/packages/filehasher) so end users
can run:

```powershell
choco install filehasher
```

Like winget, Chocolatey hosts none of the software. The published `.nupkg` is a
few KB: this folder's `filehasher.nuspec` plus
[`tools/chocolateyinstall.ps1`](tools/chocolateyinstall.ps1), which at install
time downloads that version's signed MSI from the GitHub mirror
(`fsantiago07044/filehasher` releases) and refuses to install it unless the
SHA256 matches the checksum baked into the script. The MSI is produced and
signed by the tag pipeline (see [`../ci/README.md`](../ci/README.md)); the
installer authoring lives in
[`../installer/FileHasher.wxs`](../installer/FileHasher.wxs).

**Per-release submission is automated.** The `chocolatey-push` pipeline job
(tag pipelines only, after `mirror-github`) stages this folder into
`chocolatey-out/`, substitutes the version-specific values, packs, and pushes
to `https://push.chocolatey.org/`. Prerequisites are one-time (the Chocolatey
CLI on the Windows runner, a community.chocolatey.org account, and the
`CHOCO_API_KEY` CI/CD variable, all in
[`../ci/README.md`](../ci/README.md) section 1d). After each release, watch the
review at `https://community.chocolatey.org/packages/filehasher/X.Y.Z`.

Everything below is background plus the **manual fallback** for when that job
yellow-flags. Its `chocolatey-out/` is kept as a job artifact, so a recovery is
usually just `choco push` on the already-packed nupkg.

## The templates in this folder

Three values are version-specific and are substituted by the job into a staged
copy; the files in git carry placeholders, so **this folder cannot be packed as
it stands**:

| Placeholder | Where | Value |
| --- | --- | --- |
| `{VERSION}` | nuspec, install script, VERIFICATION.txt | release version, no leading `v` |
| `{SHA256}` | install script | lowercase hex SHA256 of that version's MSI |
| `{RELEASE_NOTES}` | nuspec | prose summary plus the release URL |

`{SHA256}` is not copied from the `.msi.sha256` sidecar. The job downloads the
published MSI, hashes it, and *cross-checks* the sidecar, so the checksum is
pinned to the bytes a user will actually fetch and a dead or wrong release URL
fails the job rather than shipping.

The release-notes prose comes from
[`../winget/release-notes.txt`](../winget/release-notes.txt), shared with the
`winget-update` job (same first-line staleness gate: it must name the current
tag). The file lives under `winget/` for historical reasons; both channels read
it, so it only needs updating once per release.

`tools/LICENSE.txt` is **not** in git: the job copies the repository's root
`LICENSE` into the staged package at pack time, so the two cannot drift.

Note that `packageSourceUrl`, `projectSourceUrl`, `docsUrl` and
`bugTrackerUrl` are Chocolatey extensions to the nuspec schema. Pack with
`choco pack`; `nuget pack` errors on them.

## How moderation works

Chocolatey is a moderated repository, and the model differs from winget's in
ways worth knowing before the first submission:

- A push is not a pull request. The version lands in the moderation queue
  immediately and there is no branch to amend. If a reviewer requires a change,
  the fix is to correct the package and **push the same version again**; do not
  burn a new version number on a packaging fix.
- Three automated services run first, roughly 30 minutes after the push (the
  lag lets the CDN settle): the **validator** checks nuspec/script quality
  against the [CPMR
  rules](https://docs.chocolatey.org/en-us/community-repository/moderation/package-validator/rules/),
  the **verifier** actually installs and uninstalls the package on a clean VM,
  and the **scanner** submits it to VirusTotal.
- A clean automated run moves the version to *Ready*, then a human moderator
  reviews it. A first-ever package gets the most scrutiny; later versions
  usually clear in hours to a day.
- The **cleaner** auto-rejects a package that sits *Waiting* on the maintainer
  for 20 days (with a reminder, then 15 more days). Watch for the notification
  emails; make sure mail from chocolatey.org is not filtered.
- The verifier re-tests **already-approved** packages every two weeks and
  emails on failure. That matters here: the install script points at a GitHub
  release asset, so if an old release's assets ever disappear, that old version
  starts failing verification.
- Once the first version is approved, ask the site admins for **trusted
  package** status. Trusted packages skip human review and are approved on a
  clean automated run, which is the right end state for a vendor-maintained
  package pushed by a release pipeline.

## Manual fallback workflow (per release)

This doubles as the **pre-release rehearsal**: because `chocolatey-push` only
runs on a `v*` tag, the job cannot be exercised before the release it publishes,
so the way to shake out packaging problems first is to run steps 1 to 3 against
the previously published version and stop before step 4. Never push a rehearsal
nupkg; it would spend the first-version moderator review on a version you are
not releasing.

Run on a Windows box with the Chocolatey CLI, after the tag pipeline's
`mirror-github` job has finished (the install script points at the GitHub
Release asset, so it must exist and be final first; GitHub releases on this
repo are immutable).

1. **Render the templates.** Edit the first line to the release version before
   running any of this; the block refuses to continue on the placeholder,
   because pasting it unedited produces a URL for a release that does not
   exist, and GitHub answers that with a 404 that Windows PowerShell reports
   as the thoroughly unhelpful "The connection was closed unexpectedly".

   ```powershell
   $v = 'EDIT-ME'                  # e.g. 0.3.1

   if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "Set `$v to a real version first (got '$v')." }
   $msi = "FileHasher-$v.msi"
   $url = "https://github.com/fsantiago07044/filehasher/releases/download/v$v/$msi"
   try { Invoke-WebRequest $url -OutFile $msi -UseBasicParsing }
   catch { throw "Could not download $url ($($_.Exception.Message)). Check the release exists and has its assets." }
   $sha = (Get-FileHash $msi -Algorithm SHA256).Hash.ToLower()

   Remove-Item chocolatey-out -Recurse -Force -ErrorAction SilentlyContinue
   New-Item -ItemType Directory chocolatey-out | Out-Null
   Copy-Item chocolatey/* chocolatey-out -Recurse -Force
   Remove-Item chocolatey-out/README.md
   Copy-Item LICENSE chocolatey-out/tools/LICENSE.txt

   # The notes land inside a nuspec element, so escape XML metacharacters
   # exactly as the CI job does. Substituting anything containing < or > raw
   # makes `choco pack` fail with "'>' is an unexpected token".
   $summary = 'One-line summary of this release.'
   $notes = "$summary`n`nFull release notes: https://github.com/fsantiago07044/filehasher/releases/tag/v$v"
   $notes = $notes.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')

   Get-ChildItem chocolatey-out -Recurse -File | ForEach-Object {
     (Get-Content $_.FullName -Raw).Replace('{VERSION}', $v).Replace('{SHA256}', $sha).Replace('{RELEASE_NOTES}', $notes) |
       Set-Content $_.FullName -Encoding utf8
   }
   $sha
   ```

   Confirm the printed `$sha` against the release's `.msi.sha256` sidecar
   before going on.

2. **Pack:**

   ```powershell
   choco pack chocolatey-out\filehasher.nuspec --output-directory chocolatey-out
   ```

3. **Test the real install path** on a scratch VM rather than the build host,
   since this is a real machine-wide install (it will not disturb the FlaUI
   suite, which runs the exe out of `bin`, but it does change what is installed
   on that machine):

   ```powershell
   choco install filehasher --source .\chocolatey-out --version $v -y
   choco uninstall filehasher -y
   ```

   Check: the download's checksum is verified in the log; `FileHasher` appears
   in the Start Menu and launches; **Settings → Apps** shows FileHasher X.Y.Z,
   publisher FSP Productions, LLC; the uninstall removes the Program Files
   folder and the shortcut (this exercises Chocolatey's auto-uninstaller, which
   is what real users get, since the package ships no uninstall script).

4. **Push:**

   ```powershell
   choco push chocolatey-out\filehasher.$v.nupkg --source https://push.chocolatey.org/ --api-key <key>
   ```

   The key is in Bitwarden. A 409 means that version is already published.

5. **Watch the review** at
   `https://community.chocolatey.org/packages/filehasher/X.Y.Z` and respond to
   any validator, verifier, or moderator comment. Once approved:

   ```powershell
   choco install filehasher      # on a machine without it
   choco upgrade filehasher      # on a machine with the previous version
   ```

## Notes

- **Package version must equal** the tag version, the csproj `<Version>`, and
  the MSI ProductVersion. The pipeline already enforces tag == csproj and the
  MSI stamps the same value, so the only way to break this is by hand-packing
  with the wrong `$v`.
- **The id is `filehasher`, the title is `FileHasher`.** Chocolatey's naming
  convention is a lowercased, dot-free id and an officially-spelled title
  (CPMR0050 flags a title that is byte-identical to the id). Casing is frozen
  at the first published version, so it cannot be corrected later. No
  `.install`/`.portable` suffix, because only one form of the package is
  published.
- **The iconUrl must not be a GitHub raw link** (CPMR0076, a hard requirement)
  and must be hosted somewhere the maintainer controls. It is pinned to the
  icon file's own commit SHA on the mirror, served through jsDelivr; a branch
  URL would fight the CDN's permanent cache. Only change it if the artwork
  changes.
- **No `chocolateyuninstall.ps1` on purpose.** The MSI registers itself in
  Add/Remove Programs, so Chocolatey's auto-uninstaller removes it using the
  ProductCode recorded at install time. `softwareName = 'FileHasher*'` in the
  install script is the display-name pattern it matches.
- The package is x64-only and fails fast with a clear message on 32-bit
  Windows, matching the winget manifest's `Architecture: x64`.
- Nothing here needs the ProductCode. Unlike the winget installer manifest,
  which pins each version's regenerated ProductCode, Chocolatey discovers it
  from the registry at uninstall time.
