# Working on FileHasher (Windows)

Repo layout, conventions, and the traps that have already bitten us once.
The macOS sibling lives in a separate repo (filehasher-macos, fork-linked on
GitLab); output formats (sidecars, CSV, logs) are kept compatible between the
two, but scan **defaults** intentionally differ. Both apps now share the same
scan MODEL: a Subfolders depth (unlimited / this folder only / N levels) rather
than a recursion boolean, and the same rule for which preferences persist. What
differs is where each starts: Windows keeps the `.exe`/`.msi` default filter and
unlimited depth, macOS scans every file type and stops at the top level. Neither
default was changed when the model converged, because doing so would silently
alter what existing users' scans cover.

## Project layout

- **`FileHasher.Core`** (plain `net10.0`, AnyCPU) holds the engine: HashWorker,
  SidecarVerifier, MsiExtractor, HelpContent, Logger, AppSettings and the DTOs.
  **`FileHasherApp`** (`net10.0-windows`, x64) is only the WinForms layer:
  MainForm, HelpForm, Program, ColorProgressBar. Extracted in the 0.5.0 cycle so
  the planned CLI (`docs/cli-design.md`) can drive the same code; the product
  promise that a hash written by one FileHasher verifies in another depends on
  there being one implementation, not two that drift.
- Core stays **AnyCPU and non-Windows-targeted** on purpose: an AnyCPU library
  referenced from an x64 project is warning-free, and pinning it would block the
  cross-platform RIDs the CLI needs. `RootNamespace` is `FileHasher` in both, so
  moved files kept their namespace and no `using` changed.
- MsiExtractor compiles anywhere (WiX DTF ships netstandard2.0) but only **runs**
  on Windows, because those wrappers call native `msi.dll`.
- **`<Version>` is in BOTH csproj and they move together.** Since HelpContent
  moved to Core, `HelpContent.AppVersion` calls
  `Assembly.GetExecutingAssembly()` and therefore reports **Core's** version in
  the About box and the support mailto. Release builds are safe because CI
  passes `-p:Version=<tag>` as a global property, which propagates through the
  ProjectReference (verified); local builds do not, so bumping only the app
  csproj leaves the About box stale.

## Build and toolchain

- Target framework is **net10.0-windows**, SDK pinned by `global.json` to
  **10.0.400, rollForward: disable**. The 0.4.0 cycle moved the project off
  net8.0-windows/8.0.420 because .NET 8 goes EOL 2026-11-10; .NET 10 is LTS
  (EOL 2028-11-14). Fabian's Win10 VM is the authoritative build/test machine;
  it keeps a private SDK copy at `C:\dotnet-10.0.400` (Windows Terminal profile
  sets DOTNET_ROOT/PATH), immune to Microsoft Update and VS updates. Install or
  refresh that copy with:

  ```powershell
  Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile "$env:TEMP\dotnet-install.ps1"
  & "$env:TEMP\dotnet-install.ps1" -Version 10.0.400 -InstallDir C:\dotnet-10.0.400
  ```

  Tags before v0.4.0 pin 8.0.420, so keep the old private copy
  (`C:\dotnet-8.0.420`) around to rebuild older releases. Any schtasks-driven
  test run must set DOTNET_ROOT and PATH to the private copy it wants, or
  dotnet exits 0x8000809B (SdkResolveFailure).
- Reproducibility is pinned at two levels: `global.json` fixes the SDK, and
  `<RuntimeFrameworkVersion>` in FileHasherApp.csproj fixes the runtime packs
  embedded in the self-contained exe (10.0.11, the runtime bundled with SDK
  10.0.400). Bump the two together; the property drives both
  Microsoft.NETCore.App and Microsoft.WindowsDesktop.App.
- On macOS, **compile-gate** changes before every push, leaving `global.json`
  in place. The system dotnet root (`/usr/local/share/dotnet`, root-owned) has
  8.0.420 and 10.0.201, neither of which satisfies the pin, so 10.0.400 lives
  in the user-local root `~/.dotnet` (installed with `dotnet-install.sh
  --install-dir "$HOME/.dotnet"`, no sudo needed). Gate with:

  ```sh
  DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" \
    dotnet build filehasher.sln -c Debug
  ```

  EnableWindowsTargeting is set in both csproj, so net10.0-windows compiles
  (and even `publish -r win-x64 --self-contained`) cross-platform. Never gate
  on a different SDK than the pin: with rollForward `disable`, a build on some
  other patch can go green on code the runner rejects, which defeats the gate.
- Two pre-existing CS8602 warnings in MainForm.cs (ctor-ordering lambdas) are
  known and harmless; don't chase them.
- **.NET 10 WinForms analyzers are errors, not warnings.** WFO1000 ("does not
  configure the code serialization for its property content") fired on all
  three settable properties of the owner-drawn `ColorProgressBar` and broke the
  build on the first net10 compile; every public settable property on a control
  now needs `[DesignerSerializationVisibility(...)]` (this form builds its UI
  in code, so Hidden is the right answer) or a `[DefaultValue]`.
- The .NET 10 SDK **prunes framework-provided packages** from the restore
  graph, so FlaUI's transitive System.Drawing.Common 5.0.2 (advisory
  GHSA-rxg9-xrhp-64gj) no longer appears and `dotnet list package --vulnerable`
  comes back clean. The reviewed `<NuGetAuditSuppress>` and the audit job's
  parallel list are inert for now; they are kept as a safety net, not because
  the finding is still live.
- C# gotcha that already cost a round trip: nullability annotations are erased
  at the IL level, so constructors/overloads differing only by `?` collide
  (CS0111).

## UI test suite (FlaUI + xUnit)

Read the README's "Automated Testing" section first; it documents every
AutomationId and test class. Hard-won specifics:

- **WinForms Controls** (Button, TextBox, ListBox, RichTextBox, LinkLabel,
  Form): `Name` surfaces as UIA AutomationId; `ByAutomationId` works.
- **ToolStripMenuItems do NOT reliably surface Name as AutomationId.** Always
  locate menu items with the id-then-visible-text fallback
  (`TestHelpers.FindMenuItem`) and expect to match on the visible text
  (ampersands stripped, real `…` ellipsis character).
- **Finding app popups/secondary windows**: do not trust FlaUI's
  `GetAllTopLevelWindows`. Scan BOTH `desktop.FindAllChildren(ByProcessId)`
  AND the main window's own UIA subtree (see AppFixture.FindAppWindows and
  TestHelpers.GetOpenContextMenu). Owner-owned windows (`Form.Show(owner)`)
  are parented UNDER THE OWNER in the UIA tree, not as desktop children;
  this cost two VM round trips to learn.
- An open owned window can sit on top of the main window and swallow
  coordinate-based clicks aimed at the menu bar; check for an existing
  window before driving menus.
- Menu items in popups: prefer `.AsMenuItem().Invoke()` over `.Click()`.
- Tests launch the real exe: `AppFixture.FindExe` checks **Debug before
  Release** at each directory level, so a stale Debug build shadows a fresh
  Release build; clean or set `FILEHASHER_EXE` when testing Release/published
  binaries. UI tests need an interactive, unlocked desktop.
- Test classes marked `[Collection("Serial")]` must not run in parallel; new
  UI test classes should follow that and take one app instance per test unless
  purely read-only.
- **`AppFixture.Dispose` kills the app rather than closing it, and that is now
  load-bearing.** Kill skips `OnFormClosing`, which skips the settings save, so
  one test selecting SHA512 cannot change what the next test's launch sees. If
  this is ever changed to a graceful close, `FILEHASHER_SETTINGS` becomes the
  only thing preventing order-dependent failures.
- **`FILEHASHER_SETTINGS`** overrides the settings file path, and the fixture
  points every launched instance at a throwaway file. Without it the UI tests
  read the developer's own `%APPDATA%\FileHasher\settings.json`, so
  `MainFormStateTests` asserting SHA256-by-default would fail on a machine where
  someone last closed the app on MD5. Mirrors `FILEHASHER_EXE`.
- Not every test drives the UI. `SettingsStoreTests`, `MsiExtractorTests`,
  `SidecarVerifierTests`, `MainFormPathSeedTests` and
  `MainFormSettingsRoundTripTests` need no desktop; the last constructs MainForm
  in-process on an **STA thread** (xUnit threads are MTA).
- Count test **cases**, not attributes: a `[Theory]` contributes one per
  `[InlineData]`. Counting `[Fact]`/`[Theory]` lines undercounts badly.

## CI / release

- `.gitlab-ci.yml` is **tag-driven** (`vX.Y.Z` tags → full
  audit/build/test/sign/msi/release/mirror/winget pipeline) plus a manual
  web-button mode. Branch pushes trigger nothing.
- Release tags are **GPG-signed** (`git tag -s`); see `ci/README.md` for the
  release runbook. The GitHub mirror (fsantiago07044/filehasher) and winget
  (FSPProductions.FileHasher) are downstream of the pipeline; don't hand-edit
  either.
- The exe is Authenticode-signed on a Linux HSM host; WiX/MSI work happens on
  the Windows runner. Never re-sign or move `signed-builds/` artifacts by hand.

## Behaviour rules worth not relearning

- **Scan depth, not recursion.** `HashOptions.MaxDepth`: null unlimited, 0 the
  target folder only, N levels below. Windows defaults to **unlimited**, macOS to
  **0**, and neither moved when the model converged, because changing either
  silently alters what existing users' scans cover.
- **The verifier must get the same depth as the hash run**, or a bounded verify
  invents `NO SIDECAR` rows for files the hash run never visited.
- **Which preferences persist is a rule, not a judgement:** if a control's
  enabled state depends on the target, its value describes that target rather
  than a standing preference, so it is not persisted; and nothing that writes
  files is persisted. That leaves algorithm, include-metadata and inner-MSI.
  `MainFormSettingsRoundTripTests` asserts the persisted field set by
  reflection, so adding a field for the target path, scan-all-file-types, the
  Subfolders depth or anything behind the sidecar/CSV gates fails a test rather
  than shipping. This took three rounds of getting it wrong to arrive at.
- `settings.json` is written on close, so it does not exist until the app has
  been closed once. Load absorbs every failure (missing, corrupt, truncated,
  newer schema) and returns defaults, because failing to read a preference must
  never stop the app starting.
- **UI changes invalidate three sets of screenshots**: the Microsoft Store
  (`msstore/screenshots/`), UniGetUI (`docs/unigetui-screenshots.md`, URLs
  pinned to the release tag, so **commit images before tagging**), and the Mac
  App Store in the sibling repo. Raise regenerating them rather than waiting to
  be asked.
- **Pre-release tags create no pipeline, by design.** Betas are built by hand
  through a manual web run with `<Version>` set to `0.5.0-beta.N`; only
  `vX.Y.Z` publishes. Never widen the `workflow:` rules: the strict regex on the
  six publish jobs is the only thing between a beta and four public channels,
  and `ci/mirror-to-github.sh` has no `--prerelease`, so a mirrored beta would
  become GitHub's Latest and Scoop's excavator would ship it.

## Style

- No em dashes in any text written for this repo (docs, UI strings, comments);
  use semicolons, commas, colons, or parentheses.
- Version/copyright: the Windows app is published by FSP Productions, LLC
  (unlike the macOS app, published by Fabian personally). The support email
  subject convention is `FileHasher-Windows-<version>`, read from the assembly
  at runtime (`HelpContent.AppVersion`).
