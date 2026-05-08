# CI/CD setup notes

The build, sign, and release pipeline (`.gitlab-ci.yml` at the repo root) runs across two
GitLab Runners on the local LAN.

| Runner tag       | Host                          | Stages it runs       |
|------------------|-------------------------------|----------------------|
| `windows`        | Windows Server 2025 VM        | `test`, `build`      |
| `linux-signer`   | Ubuntu 24.04 host (HSM token) | `sign`, `release`    |

Both runners pull from `https://internal-host/`, so neither host needs inbound
connectivity. Outbound HTTPS to the GitLab server is the only network requirement; the
Linux signer additionally needs outbound access to the timestamp authority
(`timestamp.digicert.com`) and to whatever upstream the HSM driver may contact.

## Pipeline triggers

| Trigger                                | What runs                       | `latest` symlinks | GitLab Release |
|----------------------------------------|---------------------------------|-------------------|----------------|
| Push a tag matching `vMAJOR.MINOR.PATCH` | `test → build → sign → release` | updated           | created        |
| "Run pipeline" web button (any ref)    | `test → build → sign`           | unchanged         | not created    |

Manual web-button runs produce output suffixed with `-build.<short_sha>` so they cannot
overwrite an official release artifact.

## One-time prerequisites

### Windows runner (already in place from the previous nightly setup)

Confirm:

- `gitlab-runner` registered with executor `shell`, shell `powershell`, tag `windows`.
- .NET 8 SDK on `PATH`.
- An interactive desktop session is available to the runner so the FlaUI UI tests in
  `FileHasherApp.Tests` can drive a real WinForms window. Typical setup is auto-login
  for the runner user with the runner started as that user (not as `LocalSystem`).

If the runner is currently registered for nightly-only operation, no changes are needed —
the new pipeline reuses the same runner via the `windows` tag.

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

# 4. Confirm osslsigncode and the PKCS#11 stack are still in place.
test -x /usr/local/bin/osslsigncode
test -f /usr/lib/x86_64-linux-gnu/engines-3/pkcs11.so
test -f /usr/lib/x86_64-linux-gnu/opensc-pkcs11.so
test -f /root/signing-directory/signing-dir/chain.pem

# 5. Create the kept-forever deliverable directory (root-owned).
install -d -m 0755 /src/filehasher/signed-builds

# 6. Register the runner against the FileHasher project.
#    Get the registration token from Settings → CI/CD → Runners in the GitLab UI.
gitlab-runner register \
  --non-interactive \
  --url             "https://internal-host/" \
  --registration-token "<paste-project-runner-token>" \
  --executor        "shell" \
  --description     "Ubuntu signing runner (HSM)" \
  --tag-list        "linux-signer" \
  --run-untagged    "false"

# 7. Switch the gitlab-runner service to run as root so it can access the HSM
#    USB device and read /root/signing-directory/.
gitlab-runner stop
gitlab-runner uninstall
gitlab-runner install --user root
gitlab-runner start
systemctl status gitlab-runner
```

### GitLab project settings

In **Settings → CI/CD → Variables**, add:

| Key       | Value         | Type     | Flags                     |
|-----------|---------------|----------|---------------------------|
| `HSM_PIN` | the token PIN | Variable | **Masked**, **Protected** |

In **Settings → Repository → Protected tags**, add a wildcard `v*` so the masked +
protected `HSM_PIN` is only injected into pipelines triggered by those tags.

## Releasing a new version

1. Bump `<Version>` in `FileHasherApp/FileHasherApp.csproj`.
2. In `CHANGELOG.md`, move entries from `## [Unreleased]` into a new
   `## [X.Y.Z] - YYYY-MM-DD` section, and update the compare-link footer at the bottom of the file.
3. Commit and push to `main`.
4. Tag and push:
   ```sh
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```
5. Watch the pipeline. When `release` finishes:
   - `/src/filehasher/signed-builds/FileHasher-X.Y.Z.{exe,exe.sha256,zip,zip.sha256}`
     exist on the Ubuntu host.
   - `FileHasher-latest.*` symlinks point at the new files.
   - A GitLab Release for `vX.Y.Z` exists with the four assets attached as Generic
     Package links.

If the `test` job fails because the tag does not match `<Version>` in the csproj,
fix the csproj or recreate the tag at the right commit and push again.

## Manual / ad-hoc re-build

Use **Build → Pipelines → Run pipeline** in the GitLab UI. Pick any ref. The output
will be named `FileHasher-X.Y.Z-build.<short_sha>.*` so it never overwrites release
artifacts. `latest` symlinks are unchanged. No GitLab Release is created.

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
