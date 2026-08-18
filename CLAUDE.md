# Working on FileHasher (Windows)

Repo layout, conventions, and the traps that have already bitten us once.
The macOS sibling lives in a separate repo (filehasher-macos, fork-linked on
GitLab); output formats (sidecars, CSV, logs) are kept compatible between the
two, but scan defaults intentionally differ (Windows keeps the .exe/.msi
default filter and always-recursive scans).

## Build and toolchain

- SDK pinned by `global.json` to **8.0.420, rollForward: disable**. Fabian's
  Win10 VM is the authoritative build/test machine; it keeps a private copy at
  `C:\dotnet-8.0.420` (Windows Terminal profile "cmd (.NET 8.0.420)" sets
  DOTNET_ROOT/PATH), immune to Microsoft Update and VS updates.
- On macOS you can still **compile-gate** changes (do this before every push):
  `mv global.json /tmp/gj && dotnet build <proj> -c Debug; mv /tmp/gj global.json`
  (SDK 10 builds net8.0-windows; EnableWindowsTargeting is set in both csproj).
- Two pre-existing CS8602 warnings in MainForm.cs (ctor-ordering lambdas) are
  known and harmless; don't chase them.
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

## Style

- No em dashes in any text written for this repo (docs, UI strings, comments);
  use semicolons, commas, colons, or parentheses.
- Version/copyright: the Windows app is published by FSP Productions, LLC
  (unlike the macOS app, published by Fabian personally). The support email
  subject convention is `FileHasher-Windows-<version>`, read from the assembly
  at runtime (`HelpContent.AppVersion`).
