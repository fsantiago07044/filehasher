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

## Where it is published

Live in our own bucket, [fsantiago07044/scoop-bucket](https://github.com/fsantiago07044/scoop-bucket),
created 2026-09-04 from Scoop's official `ScoopInstaller/BucketTemplate`, so
the CI and auto-update wiring are theirs rather than hand-rolled.

**Not** in the official `extras` bucket, and this is the reason: the Extras
package-request form opens with a checkbox marked *required* reading
"Reasonably well-known and widely used (e.g. if it's a GitHub project, it
should have at least 100 stars and/or 50 forks)", plus a required
"Some Indication of Popularity/Repute" field. The GitHub mirror is at 0 stars
and 0 forks, so that box cannot be ticked honestly. Revisit when the project
has traction; the manifest is already written and validates against Scoop's
schema, so it becomes a ten-minute job. No issue has been opened, and no
duplicate exists.

## Which copy of the manifest is authoritative

Both, at different times, and this matters.

`scoop/filehasher.json` here is the **structural** source: the fields, the
shortcuts, the checkver and autoupdate blocks. `bucket/filehasher.json` in the
bucket repo is the **published** copy, and excavator rewrites its `version`,
`url`, `hash` and `extract_dir` on every release.

So after any release the bucket copy is ahead of this one on version, and that
is correct. Never hand-edit the bucket to bump a version; excavator does that.
Do change this copy when the manifest's *shape* changes (a new field, a
different shortcut), then copy it over and let excavator resume from there.

## Verified end to end

On the Win10 VM, 2026-09-04, as a standard non-elevated user:

- `scoop bucket add fsantiago …` then `scoop install filehasher` installed
  0.3.1 [64bit] from the `fsantiago` bucket.
- `extract_dir` worked: `FileHasher.exe` sits at the root of the app directory,
  not one level down inside `FileHasher-0.3.1/`.
- The executable's Authenticode signature verified **Valid**, subject
  `CN="FSP Productions, LLC"`.
- A Start Menu shortcut was created, and nothing appeared in Add/Remove
  Programs, which is the expected per-user behaviour.
- The app launched, and `scoop uninstall filehasher` removed both the app
  directory and the shortcut.

The excavator workflow ran clean on a manual dispatch, but 0.3.1 is already the
newest release so it had nothing to do. The autoupdate path is therefore proven
only as far as "runs without error"; the real test is the next release.

Two traps worth remembering if you ever script this against the VM again: Scoop
must install **non-elevated**, so a script fired from the elevated scheduled
task has to be handed to the shell to run under the normal token; and staging
that script in `C:\Windows\Temp` fails, because a file written there by an
elevated process is unreadable to a standard user ("Windows cannot access the
specified device, path, or file"). Stage under `C:\Users\Public` instead.
