# CI/CD setup notes

The build, sign, and release pipeline (`.gitlab-ci.yml` at the repo root) runs across two
GitLab Runners on the local LAN.

| Runner tag       | Host                          | Stages it runs                              |
|------------------|-------------------------------|---------------------------------------------|
| `windows`        | Windows Server 2025 VM        | `audit`, `build`, `test`, `package-msi`, `winget` |
| `linux-signer`   | Ubuntu 24.04 host (HSM token) | `sign`, `sign-msi`, `release`, `mirror`     |

Both runners pull from `https://internal-host/`, so neither host needs inbound
connectivity. Outbound HTTPS to the GitLab server is the only network requirement; the
Linux signer additionally needs outbound access to the timestamp authority
(`timestamp.digicert.com`) and to whatever upstream the HSM driver may contact.

## Pipeline triggers

| Trigger                                | What runs                                          | `latest` symlinks | GitLab Release | GitHub Release |
|----------------------------------------|----------------------------------------------------|-------------------|----------------|----------------|
| Push a tag matching `vMAJOR.MINOR.PATCH` | `audit → build → test → sign → package-msi → sign-msi → release → mirror → winget` | updated           | created        | created (mirrored) |
| "Run pipeline" web button (any ref)    | `audit → build → test → sign → package-msi → sign-msi` | unchanged         | not created    | not created    |

Manual web-button runs produce output suffixed with `-build.<short_sha>` so they cannot
overwrite an official release artifact.

The `audit` stage runs `dotnet list package --vulnerable --include-transitive` against the
solution to surface NuGet dependencies with known CVEs (queried from the GitHub Advisory
Database at restore time). It is marked `allow_failure: true` — findings show as a yellow
warning on the pipeline summary, never blocking a release on their own. The intent is
visibility, not gating; reviewing those warnings is a manual step on the maintainer.

The `mirror` stage runs `ci/mirror-to-github.sh` and creates a GitHub Release on the
configured mirror repo (currently `fsantiago07044/filehasher`, hardcoded in the script)
with the same six signed assets that the prior `release` stage attached to the GitLab
Release. The git history and tags are mirrored separately via GitLab's built-in push
mirror (Settings → Repository → Mirroring repositories). The `mirror` stage is marked
`allow_failure: true` — if `gh release create` fails for any reason (PAT expired, GitHub
down, mirror push hasn't propagated the tag yet, etc.) the stage shows a yellow warning
but the overall release is still considered successful. Retry the mirror job to upload
to GitHub after fixing the underlying issue; the script is idempotent (uses `gh release
view` + `gh release upload --clobber` on retry).

To explicitly silence an advisory after reviewing it and judging it not to apply, add its
GitHub Advisory URL to **two** places:

1. The `$suppressedAdvisories` array at the top of the audit job's script in
   `.gitlab-ci.yml` — filters the finding out of the CI's pass/fail decision so the
   yellow warning stops appearing on the pipeline.
2. A `<NuGetAuditSuppress Include="…" />` element in the relevant project's `.csproj`
   — silences the matching `NU1903`/`NU1904` warning during local `dotnet restore`
   for developers running the build outside CI.

Both entries should carry a comment explaining the justification, since "this advisory
doesn't apply to us" decisions need to be revisitable later when the codebase or threat
model changes. The two lists are kept in sync manually; the audit script does not parse
csproj entries.

## One-time prerequisites

### Windows runner (Windows Server 2025 with Desktop Experience, from scratch)

The runner runs jobs as a logged-in interactive user, **not** as a Windows service.
Windows services run in Session 0 with no access to the interactive desktop, which
breaks the FlaUI UI tests — they drive a real WinForms window via UI Automation and
need a real desktop to render on. The supported pattern is: auto-login a dedicated
user, then auto-start `gitlab-runner.exe run` from that user's Startup folder. Do
**not** run `gitlab-runner install`.

Prerequisite: the host is Windows Server 2025 with the Desktop Experience role
(Server Core does not work — UI Automation against WinForms is not reliable there).

Run all commands from an elevated (Administrator) PowerShell session unless a step
is explicitly tagged `[as gitlab-runner]`.

#### 1. Install the pinned .NET SDK

`global.json` pins the SDK with `rollForward: disable`, so the runner needs the
**exact** patch it names (10.0.400 as of the 0.4.0 cycle) or every build fails
fast. Install that patch explicitly rather than whatever the feed calls latest:

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile "$env:TEMP\dotnet-install.ps1"
& "$env:TEMP\dotnet-install.ps1" -Version 10.0.400 -InstallDir 'C:\Program Files\dotnet'
# Open a fresh PowerShell window so PATH picks up dotnet, then verify:
dotnet --list-sdks    # the pinned patch must appear verbatim
```

`winget install --id Microsoft.DotNet.SDK.10` also works, but it tracks the
newest 10.0.x feature band, which drifts away from the pin; if you use it,
check `dotnet --list-sdks` against `global.json` afterwards. SDKs install
side-by-side, so leaving the old .NET 8 SDK in place is harmless (and lets the
runner still build pre-0.4.0 tags, whose `global.json` pins 8.0.420).

#### 1b. Install the WiX toolset

The `package-msi` job builds the MSI on this runner with the `wix` CLI (a dotnet
global tool) and ICE-validates it via `wix msi validate` — both are Windows-only
(WiX refuses to run elsewhere: wixtoolset/issues#7154, closed not-planned).
Install it **as the `gitlab-runner` user** (after step 2 creates it and you've
signed in once), because dotnet global tools are per-user:

```powershell
# [as gitlab-runner]
dotnet tool install --global wix --version "5.*"
& "$env:USERPROFILE\.dotnet\tools\wix.exe" --version   # expect 5.x
```

The version is pinned to major 5 deliberately: WiX v6 and later require a
per-host acceptance of the Open Source Maintenance Fee (OSMF) EULA before the
tool will run, which is exactly the kind of silent CI dependency we don't
want. `.gitlab-ci.yml` invokes the tool by its absolute path
(`C:\Users\gitlab-runner\.dotnet\tools\wix.exe`) so PATH staleness in the
runner session can't bite.

#### 1c. Install wingetcreate (winget submission automation)

The `winget-update` job submits each release to `microsoft/winget-pkgs` with
`wingetcreate`. Use the standalone exe at a fixed path (no MSIX/user-PATH
dependency); download as Administrator, the runner user only needs to execute
it. The exe is framework-dependent and (as of wingetcreate 1.12) requires the
**.NET 9 runtime**, which is not covered by the .NET 10 SDK from step 1 —
install it alongside (builds are unaffected; `global.json` pins the SDK):

```powershell
winget install --id Microsoft.DotNet.Runtime.9 --silent --accept-package-agreements --accept-source-agreements
New-Item -ItemType Directory -Force C:\GitLab-Runner\tools | Out-Null
Invoke-WebRequest -Uri "https://aka.ms/wingetcreate/latest" -OutFile "C:\GitLab-Runner\tools\wingetcreate.exe"
& C:\GitLab-Runner\tools\wingetcreate.exe --version
```

If `wingetcreate --version` complains a newer `Microsoft.NETCore.App`
framework is missing after a future re-download, install the matching newer
runtime the same way.

The standalone exe also needs two native prerequisites that the MSIX install
would have carried along; both were discovered on the v0.3.1 maiden run,
where `wingetcreate` crashed at manifest validation with
`DllNotFoundException: WinGetUtil.dll` (0x8007007E):

```powershell
# WinGetUtil.dll (native manifest-validation library) next to the exe; it is
# distributed via the Microsoft.WindowsPackageManager.Utils NuGet package,
# not bundled into the standalone wingetcreate.exe. Pick the latest stable
# version from https://www.nuget.org/packages/Microsoft.WindowsPackageManager.Utils
$v = '1.29.280'; $t = "$env:TEMP\wpm-utils"
Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.windowspackagemanager.utils/$v/microsoft.windowspackagemanager.utils.$v.nupkg" -OutFile "$t.zip"
Expand-Archive "$t.zip" $t -Force
Copy-Item "$t\runtimes\win-x64\native\WinGetUtil.dll" 'C:\GitLab-Runner\tools\WinGetUtil.dll'

# VC++ 2015-2022 x64 runtime: WinGetUtil.dll is native C++ and fails to load
# without msvcp140.dll / vcruntime140*.dll, which a from-scratch Server
# install does not have. Same 0x8007007E error ("or one of its dependencies").
Invoke-WebRequest https://aka.ms/vs/17/release/vc_redist.x64.exe -OutFile "$env:TEMP\vc_redist.x64.exe"
Start-Process "$env:TEMP\vc_redist.x64.exe" -ArgumentList '/install','/quiet','/norestart' -Wait
```

The `winget-update` job also self-heals a missing `WinGetUtil.dll` by
downloading it into the job workspace (see `.gitlab-ci.yml`), but the VC++
runtime needs the Administrator install above; the runner user cannot
install it mid-job.

To update it later, re-run the download. The job references the tool via its
`WINGETCREATE` variable in `.gitlab-ci.yml`; also see the `WINGET_PAT`
variable under "GitLab project settings".

#### 2. Create the runner user account

The user is non-administrative; FileHasher does not require elevation, and running
the runner as a standard user matches end-user reality.

```powershell
$password = Read-Host -AsSecureString "Password for the new gitlab-runner local user"
New-LocalUser -Name "gitlab-runner" `
              -Password $password `
              -PasswordNeverExpires `
              -UserMayNotChangePassword `
              -FullName "GitLab Runner"
Add-LocalGroupMember -Group "Users" -Member "gitlab-runner"
```

Sign in once interactively as `gitlab-runner` (Switch User → gitlab-runner) so the
user profile is created at `C:\Users\gitlab-runner`, then sign back out.

#### 3. Configure auto-login

Use Microsoft Sysinternals Autologon — it stores the password in the LSA secret store
rather than the registry in plaintext (the legacy method).

```powershell
Invoke-WebRequest -Uri "https://download.sysinternals.com/files/AutoLogon.zip" `
                  -OutFile "$env:TEMP\AutoLogon.zip"
Expand-Archive "$env:TEMP\AutoLogon.zip" -DestinationPath "$env:TEMP\AutoLogon" -Force
Start-Process "$env:TEMP\AutoLogon\Autologon64.exe" -Verb RunAs
```

In the Autologon dialog: enter the local machine name as the domain (or `.`),
`gitlab-runner` as the username, the password from step 2, then click **Enable**.
Reboot once and verify the box auto-logs in to the `gitlab-runner` desktop.

#### 4. Disable lock screen, screen saver, and sleep

If the desktop locks or blanks while a UI test is running, FlaUI's UI Automation
queries fail or hang.

```powershell
# [as gitlab-runner] — disable screen saver under this user.
New-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name ScreenSaveActive  -Value 0 -PropertyType String -Force | Out-Null
New-ItemProperty -Path "HKCU:\Control Panel\Desktop" -Name ScreenSaveTimeOut -Value 0 -PropertyType String -Force | Out-Null
```

```powershell
# [as Admin] — never sleep, never blank the display, never lock.
powercfg /change standby-timeout-ac 0
powercfg /change standby-timeout-dc 0
powercfg /change monitor-timeout-ac 0
powercfg /change monitor-timeout-dc 0
New-Item -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization" -Force | Out-Null
New-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization" `
                 -Name NoLockScreen -Value 1 -PropertyType DWord -Force | Out-Null
```

#### 5. Allow PowerShell to run scripts

```powershell
Set-ExecutionPolicy -Scope LocalMachine -ExecutionPolicy RemoteSigned -Force
```

#### 6. Install GitLab Runner

```powershell
New-Item -ItemType Directory -Path "C:\GitLab-Runner" -Force | Out-Null
Invoke-WebRequest `
  -Uri    "https://gitlab-runner-downloads.s3.amazonaws.com/latest/binaries/gitlab-runner-windows-amd64.exe" `
  -OutFile "C:\GitLab-Runner\gitlab-runner.exe"
# Let the runner user write its config.toml, build dirs, and NuGet cache.
icacls "C:\GitLab-Runner" /grant "gitlab-runner:(OI)(CI)M" /T
```

#### 7. Create a runner in the GitLab UI and capture its token

In the GitLab project: **Settings → CI/CD → Runners → New project runner**. Set:

- Operating systems: Windows
- Tags: `windows` (and check "Run untagged jobs" off)
- Description: `Windows build runner (Server 2025 + Desktop Experience)`

Click **Create runner**. GitLab shows a runner authentication token starting with
`glrt-…` — copy it; you will not be able to see it again.

#### 8. Register the runner

```powershell
cd C:\GitLab-Runner
.\gitlab-runner.exe register `
  --non-interactive `
  --url         "https://internal-host/" `
  --token       "glrt-paste-the-token-from-step-7" `
  --executor    "shell" `
  --shell       "powershell" `
  --description "Windows build runner (Server 2025 + Desktop Experience)"
```

This writes `C:\GitLab-Runner\config.toml`. Do **not** run `gitlab-runner install`
— installing as a service runs jobs in Session 0 without desktop access.

#### 9. Auto-start the runner at login

Drop a Startup-folder shortcut so the runner launches when `gitlab-runner` auto-logs
in. Run as Admin:

```powershell
$startupDir = "C:\Users\gitlab-runner\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup"
$shortcut   = Join-Path $startupDir "gitlab-runner.lnk"
$wsh        = New-Object -ComObject WScript.Shell
$lnk        = $wsh.CreateShortcut($shortcut)
$lnk.TargetPath       = "C:\GitLab-Runner\gitlab-runner.exe"
$lnk.Arguments        = "run"
$lnk.WorkingDirectory = "C:\GitLab-Runner"
$lnk.WindowStyle      = 7   # minimized
$lnk.Save()
```

#### 10. Reboot and verify

```powershell
Restart-Computer
```

After the reboot the machine should auto-log in as `gitlab-runner`, and a minimized
`gitlab-runner.exe` window should appear in the taskbar within ~30 seconds. The
runner row in **Settings → CI/CD → Runners** in the GitLab UI should turn green.

To smoke-test the job side, trigger a pipeline manually: **Build → Pipelines →
Run pipeline** on `main`. The `test` job should pick up on the `windows` runner and
exercise the FlaUI tests against a real WinForms window. If the FlaUI tests time
out waiting for windows or controls:

- RDP into the host and confirm the desktop is unlocked and showing the
  `gitlab-runner` session — not a lock screen.
- From a `gitlab-runner` PowerShell window, confirm `dotnet --version` returns 8.x
  and that `C:\GitLab-Runner\gitlab-runner.exe verify` reports the runner online.

### Linux signer runner (Ubuntu 24.04 host)

All commands below are run as root.

```sh
# 1. Install gitlab-runner from the official Omnibus repo.
curl -L "https://packages.gitlab.com/install/repositories/runner/gitlab-runner/script.deb.sh" | bash
apt-get install -y gitlab-runner

# 2. Install supporting tools used by ci/sign.sh and ci/release.sh.
apt-get install -y zip curl coreutils

# 3. Install release-cli (single static binary).
curl -L --output /usr/local/bin/release-cli \
  "https://gitlab.com/api/v4/projects/gitlab-org%2Frelease-cli/packages/generic/release-cli/latest/release-cli-linux-amd64"
chmod +x /usr/local/bin/release-cli
release-cli --version

# 4. Install osslsigncode ≥ 2.2 (2.2 reimplemented MSI signing natively; older
#    builds need libgsf and print "libgsf is not available, msi support is
#    disabled" when ci/sign-msi.sh feeds them an MSI). The distro package is
#    fine — /usr/local/bin/osslsigncode is a symlink to it so the pipeline's
#    OSSLSIGNCODE path stays stable; the retired pre-2.2 custom build is kept
#    at /usr/local/bin/osslsigncode-legacy.
apt-get install -y osslsigncode
ln -sfn /usr/bin/osslsigncode /usr/local/bin/osslsigncode

# 4b. Fix the PKCS#11 engine. Stock noble libengine-pkcs11-openssl (libp11
#     0.4.12) + OpenSSL 3.0.13 SEGFAULTS osslsigncode during HSM signing
#     (Ubuntu bug 2119094 / Debian bug 1074764). Build current libp11 from
#     source — its `make install` overwrites the system engine at
#     /usr/lib/x86_64-linux-gnu/engines-3/pkcs11.so in place (configure
#     auto-detects OpenSSL's enginesdir), so no pipeline paths change. Hold
#     the distro package so an apt upgrade can't clobber the fixed engine.
apt-get install -y build-essential autoconf automake libtool pkg-config libssl-dev
git clone https://github.com/OpenSC/libp11 && (cd libp11 && ./bootstrap && ./configure && make && make install)
apt-mark hold libengine-pkcs11-openssl

# 4c. Confirm the signing stack is in place.
test -x /usr/local/bin/osslsigncode
test -f /usr/lib/x86_64-linux-gnu/engines-3/pkcs11.so
test -f /usr/lib/x86_64-linux-gnu/opensc-pkcs11.so
test -f /root/signing-directory/signing-dir/chain.pem

# 5. Create the kept-forever deliverable directory (root-owned).
install -d -m 0755 /src/filehasher/signed-builds

# 6. Create a runner in the GitLab UI and register it.
#    In the project: Settings → CI/CD → Runners → New project runner.
#    OS: Linux. Tags: linux-signer. Run untagged: off. Click "Create runner".
#    GitLab will show a runner authentication token starting with `glrt-…` —
#    copy it; it is shown only once.
gitlab-runner register \
  --non-interactive \
  --url         "https://internal-host/" \
  --token       "glrt-paste-the-token-from-the-ui" \
  --executor    "shell" \
  --description "Ubuntu signing runner (HSM)"

# 7. Switch the gitlab-runner service to run as root so it can access the HSM
#    USB device and read /root/signing-directory/.
gitlab-runner stop
gitlab-runner uninstall
gitlab-runner install --user root
gitlab-runner start
systemctl status gitlab-runner
```

> **⚠ After every gitlab-runner package upgrade, re-verify step 7.** The
> 18.11.3 → 19.2.1 upgrade (2026-08-04) regenerated
> `/etc/systemd/system/gitlab-runner.service` with the default
> `--user gitlab-runner`, silently reverting step 7 and breaking the next
> sign job ("Cert chain not found" — the job user could no longer traverse
> `/root`). Check with:
>
> ```sh
> systemctl cat gitlab-runner | grep ExecStart   # must show --user root
> ```
>
> If it was reset, re-run step 7 (`uninstall` / `install --user root` /
> `daemon-reload` / `restart`), then delete the stale builds checkout the
> wrong-user run left behind — root's git refuses it with
> "detected dubious ownership":
>
> ```sh
> rm -rf /home/gitlab-runner/builds/*/0/root/filehasher
> ```

### GitHub release mirroring (one-time)

The `mirror` stage uses the `gh` CLI on the Linux signer host to push each `vX.Y.Z`
release to the GitHub mirror repo as a GitHub Release with the same six signed assets
that the prior `release` stage published to GitLab. The GitLab → GitHub git history /
tag mirror itself is set up via GitLab UI (Settings → Repository → Mirroring repositories)
and is independent of this stage; the script here only handles the asset-side gap.

```sh
# 1. Install gh CLI on the signer host (as root).
mkdir -p -m 755 /etc/apt/keyrings
curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | \
  tee /etc/apt/keyrings/githubcli-archive-keyring.gpg > /dev/null
chmod go+r /etc/apt/keyrings/githubcli-archive-keyring.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | \
  tee /etc/apt/sources.list.d/github-cli.list > /dev/null
apt update
apt install -y gh

# 2. Generate a fine-grained PAT at https://github.com/settings/personal-access-tokens/new
#      Repository access:    Only select repositories → the FileHasher mirror repo
#      Repository permissions: Contents = Read and write
#    Copy the token (starts with github_pat_) — GitHub only shows it once.

# 3. Authenticate gh against github.com as root. The token persists to
#    /root/.config/gh/hosts.yml, where the gitlab-runner (which also runs as
#    root) picks it up automatically when the mirror job invokes gh.
echo "<paste-the-PAT-here>" | gh auth login --with-token
gh auth status   # verify
```

The mirror target repo is hardcoded as `GITHUB_REPO` near the top of
`ci/mirror-to-github.sh`; edit there if the mirror moves. PAT rotation: re-run step 3
with a fresh token when the existing one expires — no GitLab-side config to update.

### GitLab project settings

In **Settings → CI/CD → Variables**, add:

| Key                 | Value                                                            | Type     | Flags                                            |
|---------------------|------------------------------------------------------------------|----------|--------------------------------------------------|
| `HSM_PIN`           | the PKCS#11 token PIN                                            | Variable | **Masked**, **Hidden**, **Protected**            |
| `SIGNING_BASE_PATH` | absolute path on the signer host to the dir holding `chain.pem`  | Variable | **Masked**, **Hidden**, **Protected**            |
| `WINGET_PAT`        | classic GitHub PAT (`public_repo` scope) on the `fsantiago07044` account, used by `winget-update` to push the manifest branch to the winget-pkgs fork and open the upstream PR | Variable | **Masked**, **Hidden**, **Protected**            |

`WINGET_PAT` is deliberately a separate token from the mirror-stage PAT (which
is a fine-grained token scoped to Contents on the mirror repo and lives in the
signer's `gh` config): the winget token needs the classic `public_repo` scope
because fine-grained tokens cannot open pull requests against repositories
they don't own (`microsoft/winget-pkgs`). Rotate it in the GitLab variable
when it expires; nothing on the runners stores it.

In **Settings → Repository → Protected tags**, add a wildcard `v*` so both variables
are only injected into pipelines triggered by those tags.

`SIGNING_BASE_PATH` is held in CI/CD variables (rather than hardcoded in
`.gitlab-ci.yml`) so the internal disk layout of the signer host doesn't appear in the
public-mirror copy of this repo. Both variables carry all three privacy flags:

- **Masked** — redacts the value from job log output so a runaway echo or stack
  trace cannot leak it into the pipeline logs.
- **Hidden** — prevents the value from being viewable in the GitLab UI after
  creation, even by project maintainers. The value can be replaced but not read
  back; this protects against shoulder-surfing and stops the value from being
  exposed if a maintainer account is later compromised.
- **Protected** — restricts injection to pipelines running on protected refs
  (the `v*` tag glob, plus `main`). A pipeline triggered from an unprotected
  branch never sees the variable, and `ci/sign.sh`'s `require` check fails
  fast in that case with a clear message.

## Releasing a new version

1. Bump `<Version>` in `FileHasherApp/FileHasherApp.csproj`. If the working
   version carries a prerelease suffix (`X.Y.Z-beta` during a test cycle),
   strip it to plain `X.Y.Z` here: the tag glob and the `build` job's
   tag-versus-csproj check both accept only `MAJOR.MINOR.PATCH`, so a tag
   pipeline cannot run against a suffixed version. Nothing else needs
   reverting; the About dialog and support-email subject read the version at
   runtime.
2. In `CHANGELOG.md`, move entries from `## [Unreleased]` into a new
   `## [X.Y.Z] - YYYY-MM-DD` section, and update the compare-link footer at the bottom of the file.
3. Update `winget/release-notes.txt`: first line `vX.Y.Z` (the exact new tag),
   second line a one-sentence prose summary of the release. The `winget-update`
   job injects it as the manifest's `ReleaseNotes` and fails if the version
   line does not match the tag; the winget-pkgs validation bot flags a
   submission that drops the property the previous version had.
4. Commit and push to `main`.
5. Tag (GPG-signed; release tags are always signed) and push:
   ```sh
   git tag -s vX.Y.Z -m "FileHasher vX.Y.Z"
   git push origin vX.Y.Z
   ```
   The repo also sets `tag.gpgsign=true` locally, so a plain `git tag` would
   sign too; the explicit `-s` keeps the runbook correct on a fresh clone
   without that config. Verify with `git tag -v vX.Y.Z` before pushing.
6. Watch the pipeline. When `release` finishes:
   - `/src/filehasher/signed-builds/FileHasher-X.Y.Z.{exe,exe.sha256,zip,zip.sha256,msi,msi.sha256}`
     exist on the Ubuntu host.
   - `FileHasher-latest.*` symlinks point at the new files.
   - A GitLab Release for `vX.Y.Z` exists with the six assets attached as Generic
     Package links.
7. Before (or right after) tagging, glance at the `fsantiago07044/winget-pkgs`
   fork on GitHub and click **Sync fork** if it is behind upstream.
   `wingetcreate` attempts the sync itself, but on a fork that has drifted for
   weeks the GitHub fast-forward can fail ("The forked repository could not be
   synced with the upstream commits"), which fails the `winget-update` job;
   the fix is the one-click UI sync followed by a job retry.
8. Once `mirror-github` finishes, the `winget-update` job automatically opens
   the version's PR against `microsoft/winget-pkgs` (the job log prints the PR
   URL). Watch it for `Needs-Author-Feedback` labels; version updates usually
   merge within hours. If the job yellow-flags, the generated manifests are
   attached as job artifacts and the manual fallback workflow is in
   [`../winget/README.md`](../winget/README.md).

If the `test` job fails because the tag does not match `<Version>` in the csproj,
fix the csproj or recreate the tag at the right commit and push again.

## Manual / ad-hoc re-build

Use **Build → Pipelines → Run pipeline** in the GitLab UI. The output will be named
`FileHasher-X.Y.Z-build.<short_sha>.*` so it never overwrites release artifacts.
`latest` symlinks are unchanged. No GitLab Release is created.

**Run it on `main`, or on another protected ref.** `HSM_PIN`, `SIGNING_BASE_PATH`
and `WINGET_PAT` are Protected variables, and GitLab injects those only into
pipelines on protected branches and tags; `main` and the `v*` tag glob are the
only protected refs on this project. A web pipeline on an ordinary feature branch
runs audit, build and test normally and then dies nine seconds into `sign` with
`required variable SIGNING_BASE_PATH is not set`, which looks like a signer-host
problem and is not one. To smoke-test a branch through the signing stages, merge
it to `main` first and run the pipeline there.

## Security notes

- `HSM_PIN` is never echoed by the pipeline. `ci/sign.sh` writes it to a `mktemp`
  file with `0600` permissions and `shred -u`'s it on exit via a `trap`.
- `osslsigncode` is invoked with `-readpass <file>` rather than `-pass <pin>` so the
  PIN never appears in `ps` output.
- The signer runs as root because the cert chain lives under `/root/signing-directory/`
  and to avoid udev-rule wrangling for HSM USB device access. To lock down further
  later: create a dedicated non-root user, move the cert chain to a directory it can
  read, add the user to the group your token's udev rule grants device access to,
  re-run `gitlab-runner install --user <name>`.
- The `HSM_PIN` variable is **Protected**, so it is only injected into pipelines on
  protected refs (the `v*` tag glob). A pipeline triggered from an unprotected branch
  will not have access to it and the `sign` stage will fail fast.
