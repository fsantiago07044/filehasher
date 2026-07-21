#!/usr/bin/env bash
#
# ci/release.sh — runs in the `release` stage on the Linux signer runner.
#
# Uploads the signed deliverables to the project's Generic Package Registry
# and creates a GitLab Release for ${CI_COMMIT_TAG} with those packages
# attached as assets. Release notes are extracted from CHANGELOG.md.
#
# Reads the signed deliverables from $SIGNED_DIR on the linux-signer host
# directly. We do not pass them through GitLab artifacts — they would exceed
# the GitLab server's 100 MB artifact upload limit and they are already on
# disk on this host (sign.sh wrote them there).
#
# Inputs:
#   VERSION              - e.g. 0.1.2 (from build.env)
#   CI_COMMIT_TAG        - e.g. v0.1.2 (release stage only runs on tag pipelines)
#   SIGNED_DIR           - persistent deliverable dir, e.g. /src/filehasher/signed-builds
#   CI_API_V4_URL
#   CI_PROJECT_ID
#   CI_PROJECT_URL
#   CI_JOB_TOKEN

set -euo pipefail

require() {
  local name="$1"
  if [[ -z "${!name:-}" ]]; then
    echo "ci/release.sh: required variable $name is not set" >&2
    exit 1
  fi
}

require VERSION
require CI_COMMIT_TAG
require SIGNED_DIR
require CI_API_V4_URL
require CI_PROJECT_ID
require CI_PROJECT_URL
require CI_JOB_TOKEN

ZIP_NAME="FileHasher-${VERSION}.zip"
ZIP_SHA="FileHasher-${VERSION}.zip.sha256"
EXE_NAME="FileHasher-${VERSION}.exe"
EXE_SHA="FileHasher-${VERSION}.exe.sha256"
MSI_NAME="FileHasher-${VERSION}.msi"
MSI_SHA="FileHasher-${VERSION}.msi.sha256"

ZIP_PATH="${SIGNED_DIR}/${ZIP_NAME}"
ZIP_SHA_PATH="${SIGNED_DIR}/${ZIP_SHA}"
EXE_PATH="${SIGNED_DIR}/${EXE_NAME}"
EXE_SHA_PATH="${SIGNED_DIR}/${EXE_SHA}"
MSI_PATH="${SIGNED_DIR}/${MSI_NAME}"
MSI_SHA_PATH="${SIGNED_DIR}/${MSI_SHA}"

for f in "$ZIP_PATH" "$ZIP_SHA_PATH" "$EXE_PATH" "$EXE_SHA_PATH" "$MSI_PATH" "$MSI_SHA_PATH"; do
  [[ -f "$f" ]] || { echo "Missing release input: $f" >&2; exit 1; }
done

PKG_BASE="${CI_API_V4_URL}/projects/${CI_PROJECT_ID}/packages/generic/filehasher/${VERSION}"

upload() {
  local local_path="$1" remote_name="$2"
  echo "Uploading ${local_path}"
  curl --fail --silent --show-error \
       --header "JOB-TOKEN: ${CI_JOB_TOKEN}" \
       --upload-file "${local_path}" \
       "${PKG_BASE}/${remote_name}" >/dev/null
}

upload "$ZIP_PATH"     "$ZIP_NAME"
upload "$ZIP_SHA_PATH" "$ZIP_SHA"
upload "$EXE_PATH"     "$EXE_NAME"
upload "$EXE_SHA_PATH" "$EXE_SHA"
upload "$MSI_PATH"     "$MSI_NAME"
upload "$MSI_SHA_PATH" "$MSI_SHA"

# Extract the [VERSION] section from CHANGELOG.md.
NOTES_FILE="$(mktemp)"
trap 'rm -f "$NOTES_FILE"' EXIT

awk -v ver="$VERSION" '
  $0 ~ "^## \\["ver"\\]" { found=1; print; next }
  found && /^## \[/      { exit }
  found                  { print }
' CHANGELOG.md > "$NOTES_FILE"

if [[ ! -s "$NOTES_FILE" ]]; then
  echo "No CHANGELOG.md section found for [$VERSION]; using a generic description."
  printf 'Release **%s**.\n\nSee [CHANGELOG.md](%s/-/blob/%s/CHANGELOG.md) for details.\n' \
         "$VERSION" "$CI_PROJECT_URL" "$CI_COMMIT_TAG" > "$NOTES_FILE"
fi

# release-cli takes the description as a string; pass via --description "$(cat …)"
# rather than an env var so very long notes are handled cleanly.
release-cli create \
  --name        "FileHasher ${CI_COMMIT_TAG}" \
  --tag-name    "${CI_COMMIT_TAG}" \
  --description "$(cat "$NOTES_FILE")" \
  --assets-link "{\"name\":\"${ZIP_NAME}\",\"url\":\"${PKG_BASE}/${ZIP_NAME}\",\"link_type\":\"package\"}" \
  --assets-link "{\"name\":\"${ZIP_SHA}\",\"url\":\"${PKG_BASE}/${ZIP_SHA}\",\"link_type\":\"other\"}" \
  --assets-link "{\"name\":\"${EXE_NAME}\",\"url\":\"${PKG_BASE}/${EXE_NAME}\",\"link_type\":\"package\"}" \
  --assets-link "{\"name\":\"${EXE_SHA}\",\"url\":\"${PKG_BASE}/${EXE_SHA}\",\"link_type\":\"other\"}" \
  --assets-link "{\"name\":\"${MSI_NAME}\",\"url\":\"${PKG_BASE}/${MSI_NAME}\",\"link_type\":\"package\"}" \
  --assets-link "{\"name\":\"${MSI_SHA}\",\"url\":\"${PKG_BASE}/${MSI_SHA}\",\"link_type\":\"other\"}"

echo "Release ${CI_COMMIT_TAG} created."
