#!/usr/bin/env bash
#
# ci/sign-msi.sh — runs in the `sign-msi` stage on the Linux signer runner.
#
# Second signing pass of the release: Authenticode-signs the MSI that the
# package-msi stage built on the Windows runner (WiX only runs on Windows;
# the exe inside the MSI was already signed here by ci/sign.sh in the first
# pass). Mirrors ci/sign.sh's conventions — same osslsigncode invocation,
# same PIN hygiene, same sidecar format, same $SIGNED_DIR persistence and
# FileHasher-latest.* symlink handling.
#
# Inputs (from .gitlab-ci.yml + the build stage's dotenv report + the
# package-msi stage's artifact):
#   msi-out/<basename>-unsigned.msi   (CI artifact from package-msi)
#   VERSION         - e.g. 0.3.0 (from build.env)
#   SIGNED_DIR      - destination for kept-forever signed deliverables
#   OSSLSIGNCODE    - path to osslsigncode
#   SIGNING_BASE_PATH - dir holding chain.pem
#   PKCS11_ENGINE   - PKCS#11 engine .so
#   PKCS11_MODULE   - PKCS#11 module .so
#   TIMESTAMP_URL   - RFC3161 timestamp URL
#   SIGN_NAME       - publisher name (-n)
#   SIGN_URL        - publisher URL (-i)
#   HSM_PIN         - HSM token PIN (masked CI/CD variable)
#   CI_COMMIT_TAG       - present on tag pipelines
#   CI_COMMIT_SHORT_SHA - always present
#
# Outputs (in CWD = $CI_PROJECT_DIR):
#   signed-msi-out/<basename>.msi          signed MSI (CI artifact)
#   signed-msi-out/<basename>.msi.sha256   sidecar (sha256sum -b format)
#
# Outputs (on disk, persistent):
#   $SIGNED_DIR/<basename>.{msi,msi.sha256}
#   $SIGNED_DIR/FileHasher-latest.{msi,msi.sha256}   (tag pipelines only)

set -euo pipefail

require() {
  local name="$1"
  if [[ -z "${!name:-}" ]]; then
    echo "ci/sign-msi.sh: required variable $name is not set" >&2
    exit 1
  fi
}

require VERSION
require SIGNED_DIR
require OSSLSIGNCODE
require SIGNING_BASE_PATH
require PKCS11_ENGINE
require PKCS11_MODULE
require TIMESTAMP_URL
require SIGN_NAME
require SIGN_URL
require HSM_PIN
require CI_COMMIT_SHORT_SHA

if [[ -n "${CI_COMMIT_TAG:-}" ]]; then
  BASENAME="FileHasher-${VERSION}"
  IS_TAG=1
else
  BASENAME="FileHasher-${VERSION}-build.${CI_COMMIT_SHORT_SHA}"
  IS_TAG=0
fi

UNSIGNED_MSI="msi-out/${BASENAME}-unsigned.msi"
[[ -f "$UNSIGNED_MSI" ]] || { echo "Unsigned MSI not found at $UNSIGNED_MSI (package-msi artifact missing?)" >&2; exit 1; }
[[ -x "$OSSLSIGNCODE" ]] || { echo "osslsigncode not executable at $OSSLSIGNCODE"  >&2; exit 1; }
[[ -f "${SIGNING_BASE_PATH}/chain.pem" ]] || { echo "Cert chain not found at ${SIGNING_BASE_PATH}/chain.pem" >&2; exit 1; }

# PIN handling — written to a 0600 tempfile, shredded on exit. Never echo.
PIN_FILE="$(mktemp)"
chmod 600 "$PIN_FILE"
cleanup() { shred -u "$PIN_FILE" 2>/dev/null || rm -f "$PIN_FILE"; }
trap cleanup EXIT
printf '%s' "$HSM_PIN" > "$PIN_FILE"

WORKDIR="signed-msi-out"
rm -rf "$WORKDIR"
mkdir -p "$WORKDIR"

SIGNED_MSI="${WORKDIR}/${BASENAME}.msi"

echo "Signing ${UNSIGNED_MSI}"
"${OSSLSIGNCODE}" sign \
  -pkcs11engine "${PKCS11_ENGINE}" \
  -pkcs11module "${PKCS11_MODULE}" \
  -certs        "${SIGNING_BASE_PATH}/chain.pem" \
  -key          'pkcs11:id=%01;type=private' \
  -readpass     "${PIN_FILE}" \
  -n            "${SIGN_NAME}" \
  -i            "${SIGN_URL}" \
  -h            sha256 \
  -ts           "${TIMESTAMP_URL}" \
  -in           "${UNSIGNED_MSI}" \
  -out          "${SIGNED_MSI}"

echo "Verifying signature on ${SIGNED_MSI}"
"${OSSLSIGNCODE}" verify -in "${SIGNED_MSI}"

( cd "${WORKDIR}" && sha256sum -b "${BASENAME}.msi" > "${BASENAME}.msi.sha256" )

# Persistent publish.
mkdir -p "${SIGNED_DIR}"
install -m 0644 "${SIGNED_MSI}"                       "${SIGNED_DIR}/${BASENAME}.msi"
install -m 0644 "${WORKDIR}/${BASENAME}.msi.sha256"   "${SIGNED_DIR}/${BASENAME}.msi.sha256"

if [[ $IS_TAG -eq 1 ]]; then
  ( cd "${SIGNED_DIR}"
    ln -sfn "${BASENAME}.msi"        "FileHasher-latest.msi"
    ln -sfn "${BASENAME}.msi.sha256" "FileHasher-latest.msi.sha256"
  )
  echo "FileHasher-latest.msi* symlinks updated."
fi

echo
echo "Persistent deliverables in ${SIGNED_DIR}:"
ls -la "${SIGNED_DIR}/${BASENAME}".msi* 2>/dev/null || true
