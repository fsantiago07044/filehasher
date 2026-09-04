# Scoop distribution

FileHasher is distributed through [Scoop](https://scoop.sh) so end users can
run:

```powershell
scoop bucket add extras
scoop install filehasher
```

Scoop installs the **portable zip**, not the MSI: it unpacks
`FileHasher-X.Y.Z.zip` into the Scoop apps directory and creates a Start Menu
shortcut. Nothing is written to Program Files, nothing lands in Add/Remove
Programs, and no elevation is needed. That makes this the only one of the three
channels that installs per-user.

[`filehasher.json`](filehasher.json) is the manifest, kept here as the
authoritative copy. The published copy lives in
[ScoopInstaller/Extras](https://github.com/ScoopInstaller/Extras).

## Why this channel needs no pipeline stage

winget takes a PR per release and Chocolatey takes a push per release, both
automated in `.gitlab-ci.yml`. Scoop needs neither, because the manifest
describes how to find its own updates:

- `checkver` points at the GitHub releases, so the bucket can see a new tag.
- `autoupdate` is the template for rewriting the manifest against it, with
  `$version` substituted into the URL and `extract_dir`.
- `hash` reads `$url.sha256`, the sidecar already published beside the zip, so
  nothing has to download 63 MB to learn the checksum. Scoop parses the
  `sha256sum` format (`<hash> *<filename>`) natively.

The Extras bucket runs a bot (**excavator**) that executes exactly that on a
schedule and commits the result, so a new release reaches Scoop users without
anyone doing anything.

**The cost of that is silence when it breaks.** If the release asset names ever
change, or the `.sha256` sidecars stop being published next to the zip,
excavator stops producing updates and nobody is notified. The asset names in
`ci/release.sh` are therefore a contract with this channel, not just an
internal detail. After a release, confirm with `scoop update; scoop status`, or
watch for the bucket commit.

## Manifest decisions

Rules come from the Scoop [contributing
guide](https://github.com/ScoopInstaller/.github/blob/main/.github/CONTRIBUTING.md);
the manifest validates against
[`schema.json`](https://raw.githubusercontent.com/ScoopInstaller/Scoop/master/schema.json).

- **Field order is prescribed** by the contributing guide and the manifest
  follows it. Indentation is 4 spaces.
- **`license` must be an SPDX identifier**, hence `MIT` rather than a URL.
- **`architecture` is mandatory** unless an app ships 32-bit only, so the
  64-bit-only download still sits under `architecture.64bit`.
- **`shortcuts` but no `bin`.** The guide says a GUI app that takes no
  command-line arguments does not need a `bin` shim, and `Program.Main()` takes
  no arguments.
- **No `persist`.** The app keeps no configuration beside its executable; its
  logs go to `%AppData%\FileHasher\Logs`, which is outside the Scoop directory
  and survives updates on its own. Note the corollary: `scoop uninstall` leaves
  those logs behind.
- **`extract_dir` is required and must be templated.** The zip contains a
  versioned top-level folder (`FileHasher-X.Y.Z/FileHasher.exe`), so without
  `extract_dir` the executable would land one directory too deep, and without
  `$version` in the autoupdate copy the first update would break.

## Submitting to Extras

1. **Open an issue first** on ScoopInstaller/Extras proposing the package. The
   contributing guide is explicit that they are "very reluctant to accept
   random pull requests without a related issue created first."
2. Once a maintainer approves it, fork Extras, branch from `master`, and add
   `bucket/filehasher.json` as a copy of the manifest here.
3. Test locally on Windows before opening the PR:

   ```powershell
   scoop install .\filehasher.json
   scoop uninstall filehasher
   scoop checkver filehasher .\           # proves checkver finds the release
   scoop checkver filehasher .\ -u        # proves autoupdate rewrites correctly
   ```

4. PR title must be `filehasher: Add version X.Y.Z`.
5. Comment `/verify` on the PR to trigger the automatic manifest verifier, then
   address what it reports.

## If Extras declines

Nothing is lost: publish the same manifest from our own bucket, which has no
gatekeeping. Create a repository named `scoop-bucket` on the GitHub mirror
account with the manifest at `bucket/filehasher.json`, and users run:

```powershell
scoop bucket add fsantiago https://github.com/fsantiago07044/scoop-bucket
scoop install filehasher
```

The trade is discoverability and maintenance: an own bucket is not searched by
default, and excavator does not run against it, so autoupdate would need a
scheduled GitHub Action in that repository to do the same job.
