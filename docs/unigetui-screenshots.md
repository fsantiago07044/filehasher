# UniGetUI icon and screenshot database

UniGetUI (formerly WingetUI) shows an icon and screenshots beside a package. It
does not read them from the winget manifest, which has no image fields at all;
it reads them from its own database, keyed by winget package identifier:

**`WebBasedData/screenshot-database-v2.json`** in
[Devolutions/UniGetUI](https://github.com/Devolutions/UniGetUI).

The database stores **URLs only**. The images stay in this repo, under
`docs/filehasher-winget-PR-assets/`, and are served from the GitHub mirror via
`raw.githubusercontent.com`.

Original submission: [PR #5259](https://github.com/Devolutions/UniGetUI/pull/5259).

## URLs are pinned to the release tag

The original entry pointed at `main`, which meant replacing a PNG silently
changed what every user saw and no PR was needed, or possible. **Changed
2026-09-19 to pin every URL to the release tag instead**, so that:

- each release's entry shows the screenshots that actually matched that version,
- changes are visible to UniGetUI's maintainers in a reviewable diff,
- a bad screenshot cannot go live the instant it is pushed.

The cost is one small PR per release, which is the intended trade.

Note this makes the release ORDER load-bearing: the images must be committed
before the tag is created, or the tag will not contain them and every URL in the
entry will 404. This is step 0 of the release runbook in `ci/README.md`.

## Entry format

Keyed by the winget PackageIdentifier, with `icon` as a single URL and `images`
as an array. The icon is pinned to the tag along with the screenshots, so an
entry is one coherent snapshot of a release rather than a mix of refs.

```json
"FSPProductions.FileHasher": {
  "icon": "https://raw.githubusercontent.com/fsantiago07044/filehasher/vX.Y.Z/docs/filehasher-winget-PR-assets/app-icon/hash-icon-256.png",
  "images": [
    "https://raw.githubusercontent.com/fsantiago07044/filehasher/vX.Y.Z/docs/filehasher-winget-PR-assets/app-ui-screenshots/main-ui-completion-state-simple.png",
    "https://raw.githubusercontent.com/fsantiago07044/filehasher/vX.Y.Z/docs/filehasher-winget-PR-assets/app-ui-screenshots/main-ui.png",
    "https://raw.githubusercontent.com/fsantiago07044/filehasher/vX.Y.Z/docs/filehasher-winget-PR-assets/app-ui-screenshots/main-ui-completion-inner-msi-scan.png",
    "https://raw.githubusercontent.com/fsantiago07044/filehasher/vX.Y.Z/docs/filehasher-winget-PR-assets/app-ui-screenshots/sidecar-file-explorer-view.png",
    "https://raw.githubusercontent.com/fsantiago07044/filehasher/vX.Y.Z/docs/filehasher-winget-PR-assets/app-ui-screenshots/sidecar-file-overwrite-dialog.png",
    "https://raw.githubusercontent.com/fsantiago07044/filehasher/vX.Y.Z/docs/filehasher-winget-PR-assets/app-ui-screenshots/completion-dialog-sidecars-overwritten.png"
  ]
}
```

## Per-release checklist

1. Regenerate the screenshots that changed and commit them (step 0 of the
   runbook). Keep the filenames stable unless the set itself is changing, so the
   diff in their repo is URLs only.
2. Tag and release as normal. Wait for GitLab's push mirror to carry the tag to
   GitHub; the URLs resolve only once the tag exists there.
3. **Verify every URL returns 200 before opening the PR.** A 404 in their
   database is worse than a stale screenshot:
   ```bash
   V=vX.Y.Z
   B="https://raw.githubusercontent.com/fsantiago07044/filehasher/$V/docs/filehasher-winget-PR-assets"
   for f in app-icon/hash-icon-256.png \
            app-ui-screenshots/main-ui-completion-state-simple.png \
            app-ui-screenshots/main-ui.png \
            app-ui-screenshots/main-ui-completion-inner-msi-scan.png \
            app-ui-screenshots/sidecar-file-explorer-view.png \
            app-ui-screenshots/sidecar-file-overwrite-dialog.png \
            app-ui-screenshots/completion-dialog-sidecars-overwritten.png; do
     printf '%s %s\n' "$(curl -s -o /dev/null -w '%{http_code}' "$B/$f")" "$f"
   done
   ```
4. Fork/refresh `Devolutions/UniGetUI`, edit `screenshot-database-v2.json`,
   change only this entry's URLs, and open the PR.

## Which screenshots the UI affects

Not all six show the app. `sidecar-file-explorer-view.png` is a File Explorer
window and survives any UI change. The three `main-ui*.png` shots show the
Target group and go stale whenever it changes. The two dialog shots go stale
only if the main window is visible behind them.
