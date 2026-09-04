# Microsoft Store distribution

FileHasher is listed on the Microsoft Store as an **unpackaged Win32 app**: the
Store points at the same signed MSI the winget and Chocolatey channels use, so
there is no MSIX build and no second installer to maintain.

The listing copy, field by field, is in [`listing.md`](listing.md). The Store
logo assets are in [`assets/`](assets/).

## Why unpackaged rather than MSIX

The Store has accepted EXE/MSI submissions since June 2021, and FileHasher
already satisfies every requirement it imposes:

| Store requirement | How FileHasher meets it |
| --- | --- |
| Installer is `.msi` or `.exe` | signed MSI from the tag pipeline |
| Binary and all PE files inside signed, chaining to a Microsoft Trusted Root CA | `ci/sign.sh` signs the exe, `ci/sign-msi.sh` signs the MSI, both on the HSM |
| Versioned download URL that never changes after submission | GitHub Release assets, which are immutable on this repo |
| Silent install, UAC prompt allowed | MSI authors no UI; the Store runs `/qn` itself |
| Standalone offline installer, no downloader stub | self-contained single-file exe wrapped in the MSI |

MSIX would buy free Microsoft signing and CDN hosting plus Store-managed
updates, at the cost of a second packaging format to build, test, and keep in
sync with the MSI. We already sign on our own HSM and already host on GitHub
Releases, so the trade is not worth taking for a utility this size.

Because the Store hosts nothing, an update is not a package upload: it is a new
versioned URL pointed at the new release. The Store keeps a copy of the
certified binary and re-certifies automatically if it ever notices the URL's
contents changed without a submission, which cannot happen here since GitHub
releases on this repo are immutable.

## First submission (once)

Everything in this section happens in Partner Center; nothing touches the repo
or the pipeline.

1. **Reserve the name first**, before anything else. Name reservation is
   first-come-first-served and instant. `FileHasher` as one word is not taken,
   but the Store already carries **File Hasher** (publisher Aftnet) and
   **Files Hasher** (Star Studio), so Partner Center may refuse ours as
   confusingly similar. If it does, reserve a distinct fallback rather than
   arguing: additional names can be reserved later and one of them chosen as
   the display name.
2. Create the app, choose product type **EXE/MSI app**, and fill in the fields
   from [`listing.md`](listing.md).
3. Complete the **age ratings** questionnaire. This is required once and it
   also gates the submission API, which cannot be used until a first submission
   exists.
4. Upload the Store logos from [`assets/`](assets/) and the screenshots (see
   "Assets" below).
5. Submit and wait for certification.

## Per-release update

Not automated, unlike winget and Chocolatey. After the tag pipeline's
`mirror-github` job has published the release:

1. Partner Center, the app, **Update** submission.
2. On the Packages page, replace the package URL with the new versioned one:

   ```
   https://github.com/fsantiago07044/filehasher/releases/download/vX.Y.Z/FileHasher-X.Y.Z.msi
   ```

3. Update **What's new in this version** from the CHANGELOG entry.
4. Submit; certification for an update is faster than the first one.

**Automating this later** is possible: there is a Microsoft Store submission
API for MSI/EXE apps. It is a heavier lift than the other two channels, because
it needs a Microsoft Entra ID directory with Global administrator rights, an
app registration associated with the Partner Center account and given the
Manager role, and a client secret that Microsoft caps at 24 months and
recommends keeping under 12, so the pipeline would gain a credential that
expires and silently breaks the job. Deliberately deferred until the manual
path has been walked a few times and the release cadence justifies it.

## Assets

`assets/` holds what can be derived from the app icon:

| File | Partner Center field | Notes |
| --- | --- | --- |
| `store-app-tile-icon-300.png` | 1:1 app tile icon (300 x 300) | recommended; the Store prefers it over the package's own icon |
| `store-box-art-1080.png` | 1:1 box art (1080 x 1080 or 2160 x 2160) | required Store logo |

Both are rendered from `FileHasherApp/assets/app-icon/hash-icon-master-1024.png`,
which is the largest art that exists; there is no vector source in the repo. The
300 px tile is a clean downscale. The 1080 px box art is a 5.5 percent upscale
from 1024, which is not visible at the sizes the Store renders it, but if the
original art is ever regenerated, produce it at 2160 x 2160 and replace both.
Both carry alpha; if a Store surface renders the transparency badly, flatten
onto a solid background rather than shipping a fringed icon.

**Screenshots are not in the repo and have to be retaken.** The Store requires
PNG desktop screenshots of **1366 x 768 or larger**, and every existing shot in
`docs/filehasher-winget-PR-assets/app-ui-screenshots/` is smaller than that (the
largest is 1123 x 767). Capture fresh ones on Windows with the main window sized
so the PNG lands at 1366 x 768 or above. One is required, Microsoft recommends
five to eight, and the guidance is to show real UI with no added logos, icons,
or marketing text overlaid. The obvious set: main window idle, a completed hash
run, a sidecar verification with mixed verdicts, the results right-click menu,
the inner-MSI scan, and the in-app help window.
