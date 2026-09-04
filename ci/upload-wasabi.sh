#!/usr/bin/env bash
#
# ci/upload-wasabi.sh — runs in the `wasabi` stage on the Linux signer.
#
# Publishes the six signed release assets to Wasabi S3, which is where the
# Microsoft Store listing points. The Store will not accept a package URL that
# redirects, and every GitHub release-download URL 302s to a short-lived signed
# URL on release-assets.githubusercontent.com, so GitHub cannot serve that one
# field. Wasabi serves the object directly, 200 with no redirect.
#
# Only the Store uses these URLs. winget, Chocolatey and Scoop all follow
# redirects and stay pointed at GitHub, which keeps Wasabi egress (and the free
# egress policy, which allows monthly egress up to the account's stored volume)
# comfortable. Do not repoint the other channels here without a reason.
#
# Inputs (from .gitlab-ci.yml + the build stage's dotenv report):
#   VERSION                   e.g. 0.4.0
#   SIGNED_DIR                dir holding the six signed deliverables
#   WASABI_ACCESS_KEY_ID      Protected + Masked CI/CD variable
#   WASABI_SECRET_ACCESS_KEY  Protected + Masked CI/CD variable
#
# The credentials belong to the Wasabi IAM sub-user `filehasher-ci`, which can
# PutObject under filehasher/* and nothing else: no DeleteObject, no bucket
# policy rights, no writes outside the prefix. A leaked CI token can add a
# release, not remove or alter one.

set -euo pipefail

BUCKET="fsp-productions-downloads"
ENDPOINT="https://s3.wasabisys.com"
PREFIX="filehasher/windows/${VERSION}"
PUBLIC_BASE="${ENDPOINT}/${BUCKET}/${PREFIX}"

require() { [ -n "${!1:-}" ] || { echo "required variable $1 is not set" >&2; exit 1; }; }
require VERSION; require SIGNED_DIR
require WASABI_ACCESS_KEY_ID; require WASABI_SECRET_ACCESS_KEY

export AWS_ACCESS_KEY_ID="$WASABI_ACCESS_KEY_ID"
export AWS_SECRET_ACCESS_KEY="$WASABI_SECRET_ACCESS_KEY"
export AWS_DEFAULT_REGION="us-east-1"

FILES=(
  "FileHasher-${VERSION}.exe"        "FileHasher-${VERSION}.exe.sha256"
  "FileHasher-${VERSION}.zip"        "FileHasher-${VERSION}.zip.sha256"
  "FileHasher-${VERSION}.msi"        "FileHasher-${VERSION}.msi.sha256"
)
for f in "${FILES[@]}"; do
  [ -f "${SIGNED_DIR}/${f}" ] || { echo "Missing asset: ${SIGNED_DIR}/${f}" >&2; exit 1; }
done

# A binary the Store has certified must never change at its URL. Versioned keys
# make that automatic, but a re-run of this job against an existing version
# would still overwrite, so refuse instead. (The CI user is granted ListBucket
# on this prefix precisely so it can make this check.)
existing=$(aws --endpoint-url "$ENDPOINT" s3api list-objects-v2 \
             --bucket "$BUCKET" --prefix "${PREFIX}/" \
             --query 'length(Contents)' --output text 2>/dev/null || echo "None")
if [ "$existing" != "None" ] && [ "$existing" != "0" ]; then
  echo "Refusing to upload: ${PREFIX}/ already holds ${existing} object(s)." >&2
  echo "A published installer must never change at its URL; the Store certifies the bytes." >&2
  echo "If this is a genuine re-run of a failed upload, remove the prefix by hand with an" >&2
  echo "admin key and re-run. If the release content changed, cut a new version instead." >&2
  exit 1
fi

content_type() {
  case "$1" in
    *.sha256) echo "text/plain; charset=utf-8" ;;
    *.msi)    echo "application/x-msi" ;;
    *.zip)    echo "application/zip" ;;
    *)        echo "application/octet-stream" ;;
  esac
}

for f in "${FILES[@]}"; do
  echo "uploading ${f}"
  aws --endpoint-url "$ENDPOINT" s3 cp "${SIGNED_DIR}/${f}" "s3://${BUCKET}/${PREFIX}/${f}" \
      --content-type "$(content_type "$f")" \
      --cache-control "public, max-age=31536000, immutable" \
      --only-show-errors
done

# Prove the public URL serves the same bytes that were signed. This is the
# check that matters: the Store downloads from this URL, not from our disk.
echo
for f in "FileHasher-${VERSION}.exe" "FileHasher-${VERSION}.zip" "FileHasher-${VERSION}.msi"; do
  want=$(awk '{print $1}' "${SIGNED_DIR}/${f}.sha256")
  got=$(curl -fsSL "${PUBLIC_BASE}/${f}" | sha256sum | awk '{print $1}')
  if [ "$want" != "$got" ]; then
    echo "VERIFY FAILED for ${f}: sidecar ${want}, served ${got}" >&2
    exit 1
  fi
  echo "verified ${f} (${got})"
done

# Redirects are the whole reason this job exists; assert it stayed that way.
redirects=$(curl -s -o /dev/null -w '%{num_redirects}' "${PUBLIC_BASE}/FileHasher-${VERSION}.msi")
[ "$redirects" = "0" ] || { echo "Package URL now redirects (${redirects} hop(s)); the Store will reject it." >&2; exit 1; }

echo
echo "Microsoft Store package URL for this release:"
echo "  ${PUBLIC_BASE}/FileHasher-${VERSION}.msi"
