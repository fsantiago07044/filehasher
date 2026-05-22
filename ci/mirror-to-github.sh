#!/usr/bin/env bash
#
# ci/mirror-to-github.sh — runs in the `mirror` stage on the Linux signer runner.
#
# Mirrors the v${VERSION} release to GitHub by creating a GitHub Release on
# the repo configured below with the same four signed assets that the prior
# `release` stage published to GitLab. Uses the gh CLI, which finds its
# authentication state in /root/.config/gh/hosts.yml — set up one-time via
# `echo "<PAT>" | gh auth login --with-token` (see "GitHub release mirroring"
# in ci/README.md).
#
# Inputs (from .gitlab-ci.yml + the build stage's dotenv report):
#   VERSION              - e.g. 0.2.0 (from build.env)
#   CI_COMMIT_TAG        - e.g. v0.2.0 (mirror runs only on tag pipelines)
#   SIGNED_DIR           - /src/filehasher/signed-builds
#
# This script is marked allow_failure: true in the pipeline, so a transient
# GitHub outage, an expired PAT, or a not-yet-propagated mirror push surfaces
# as a yellow warning rather than blocking an otherwise-successful release.

set -euo pipefail

require() {
  local name="$1"
  if [[ -z "${!name:-}" ]]; then
    echo "ci/mirror-to-github.sh: required variable $name is not set" >&2
    exit 1
  fi
}

require VERSION
require CI_COMMIT_TAG
require SIGNED_DIR

# Target GitHub repo for the mirror. The Layer-1 push mirror in GitLab sends
# the git history (commits + tags) here automatically; this script only
# handles the asset side that GitLab Releases don't propagate themselves.
GITHUB_REPO="fsantiago07044/filehasher"

EXE="FileHasher-${VERSION}.exe"
EXE_SHA="FileHasher-${VERSION}.exe.sha256"
ZIP="FileHasher-${VERSION}.zip"
ZIP_SHA="FileHasher-${VERSION}.zip.sha256"

for f in "$EXE" "$EXE_SHA" "$ZIP" "$ZIP_SHA"; do
  [[ -f "${SIGNED_DIR}/${f}" ]] || { echo "Missing asset: ${SIGNED_DIR}/${f}" >&2; exit 1; }
done

# Sanity-check the gh auth state up-front so we fail with a clear message if
# the PAT was revoked or expired rather than mid-upload.
if ! gh auth status >/dev/null 2>&1; then
  echo "gh CLI is not authenticated against github.com. Run on the signer host as root:" >&2
  echo "  echo '<github-PAT>' | gh auth login --with-token" >&2
  exit 1
fi

# Extract release notes from CHANGELOG.md for the matching [VERSION] section.
# Identical pattern to ci/release.sh's GitLab-Release extraction, with one
# difference: skip the "## [X.Y.Z]" header line itself, since GitHub renders
# the release title separately and a duplicate header at the top of the body
# would just be visual noise.
NOTES_FILE="$(mktemp)"
trap 'rm -f "$NOTES_FILE"' EXIT

awk -v ver="$VERSION" '
  $0 ~ "^## \\["ver"\\]" { found=1; next }
  found && /^## \[/      { exit }
  found                  { print }
' CHANGELOG.md > "$NOTES_FILE"

if [[ ! -s "$NOTES_FILE" ]]; then
  echo "No CHANGELOG.md section found for [$VERSION]; using a generic description."
  printf 'Release **%s**.\n\nSee [CHANGELOG.md](https://github.com/%s/blob/%s/CHANGELOG.md) for details.\n' \
         "$VERSION" "$GITHUB_REPO" "$CI_COMMIT_TAG" > "$NOTES_FILE"
fi

# Idempotency: on a retry of the mirror job (e.g. after a transient GitHub
# failure), the GitHub Release may already exist. `gh release create` would
# refuse to recreate it. Detect the pre-existing release and switch to
# `gh release upload --clobber` so the retry attaches/refreshes assets
# without erroring on the second-create.
if gh release view "${CI_COMMIT_TAG}" --repo "${GITHUB_REPO}" >/dev/null 2>&1; then
  echo "GitHub release ${CI_COMMIT_TAG} already exists on ${GITHUB_REPO}; refreshing assets via gh release upload."
  gh release upload "${CI_COMMIT_TAG}" \
    --repo "${GITHUB_REPO}" \
    --clobber \
    "${SIGNED_DIR}/${EXE}" \
    "${SIGNED_DIR}/${EXE_SHA}" \
    "${SIGNED_DIR}/${ZIP}" \
    "${SIGNED_DIR}/${ZIP_SHA}"
else
  echo "Creating GitHub release ${CI_COMMIT_TAG} on ${GITHUB_REPO}..."
  gh release create "${CI_COMMIT_TAG}" \
    --repo "${GITHUB_REPO}" \
    --target "${CI_COMMIT_SHA}" \
    --title "FileHasher ${CI_COMMIT_TAG}" \
    --notes-file "${NOTES_FILE}" \
    "${SIGNED_DIR}/${EXE}" \
    "${SIGNED_DIR}/${EXE_SHA}" \
    "${SIGNED_DIR}/${ZIP}" \
    "${SIGNED_DIR}/${ZIP_SHA}"
fi

echo "GitHub mirror complete: https://github.com/${GITHUB_REPO}/releases/tag/${CI_COMMIT_TAG}"
