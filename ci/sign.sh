#!/usr/bin/env bash
#
# ci/sign.sh — runs in the `sign` stage on the Linux signer runner.
#
# Inputs (from .gitlab-ci.yml + the build stage's dotenv report):
#   PUBLISH_DIR     - relative path to the dotnet publish output
#   SIGNED_DIR      - destination for kept-forever signed deliverables
#   VERSION         - e.g. 0.1.2 (from build.env)
#   OSSLSIGNCODE    - path to osslsigncode
#   SIGNING_BASE    - dir holding chain.pem
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
#   signed-out/<basename>.exe                signed binary (CI artifact)
#   signed-out/<basename>.exe.sha256         sidecar (sha256sum -b format)
#   signed-out/<basename>.zip                publish dir bundled, exe replaced with signed
#   signed-out/<basename>.zip.sha256         sidecar
#
# Outputs (on disk, persistent):
#   $SIGNED_DIR/<basename>.{exe,exe.sha256,zip,zip.sha256}
#   $SIGNED_DIR/FileHasher-latest.{exe,exe.sha256,zip,zip.sha256}   (tag pipelines only)
#
# <basename> is FileHasher-${VERSION} on tag pipelines, otherwise
# FileHasher-${VERSION}-build.${CI_COMMIT_SHORT_SHA}.

set -euo pipefail

require() {
  local name="$1"
  if [[ -z "${!name:-}" ]]; then
    echo "ci/sign.sh: required variable $name is not set" >&2
    exit 1
  fi
}

require PUBLISH_DIR
require SIGNED_DIR
require VERSION
require OSSLSIGNCODE
require SIGNING_BASE
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

UNSIGNED_EXE="${PUBLISH_DIR}/FileHasher.exe"
[[ -f "$UNSIGNED_EXE" ]] || { echo "Unsigned executable not found at $UNSIGNED_EXE" >&2; exit 1; }
[[ -x "$OSSLSIGNCODE" ]] || { echo "osslsigncode not executable at $OSSLSIGNCODE"  >&2; exit 1; }
[[ -f "${SIGNING_BASE}/chain.pem" ]] || { echo "Cert chain not found at ${SIGNING_BASE}/chain.pem" >&2; exit 1; }

# PIN handling — written to a 0600 tempfile, shredded on exit. Never echo.
PIN_FILE="$(mktemp)"
chmod 600 "$PIN_FILE"
cleanup() { shred -u "$PIN_FILE" 2>/dev/null || rm -f "$PIN_FILE"; }
trap cleanup EXIT
printf '%s' "$HSM_PIN" > "$PIN_FILE"

WORKDIR="signed-out"
rm -rf "$WORKDIR"
mkdir -p "$WORKDIR"

SIGNED_EXE="${WORKDIR}/${BASENAME}.exe"

echo "Signing ${UNSIGNED_EXE}"
"${OSSLSIGNCODE}" sign \
  -pkcs11engine "${PKCS11_ENGINE}" \
  -pkcs11module "${PKCS11_MODULE}" \
  -certs        "${SIGNING_BASE}/chain.pem" \
  -key          'pkcs11:id=%01;type=private' \
  -readpass     "${PIN_FILE}" \
  -n            "${SIGN_NAME}" \
  -i            "${SIGN_URL}" \
  -h            sha256 \
  -ts           "${TIMESTAMP_URL}" \
  -in           "${UNSIGNED_EXE}" \
  -out          "${SIGNED_EXE}"

echo "Verifying signature on ${SIGNED_EXE}"
"${OSSLSIGNCODE}" verify -in "${SIGNED_EXE}"

# Build the release zip: copy publish dir contents, replace unsigned exe with signed one.
ZIP_STAGE="${WORKDIR}/${BASENAME}"
mkdir -p "$ZIP_STAGE"
cp -r "${PUBLISH_DIR}/." "${ZIP_STAGE}/"
cp -f "${SIGNED_EXE}" "${ZIP_STAGE}/FileHasher.exe"
( cd "$ZIP_STAGE" && sha256sum -b FileHasher.exe > FileHasher.exe.sha256 )

( cd "${WORKDIR}" && zip -qr "${BASENAME}.zip" "${BASENAME}" )
ZIP_PATH="${WORKDIR}/${BASENAME}.zip"
rm -rf "$ZIP_STAGE"

# Sidecars for the standalone deliverables.
( cd "${WORKDIR}" && sha256sum -b "${BASENAME}.exe" > "${BASENAME}.exe.sha256" )
( cd "${WORKDIR}" && sha256sum -b "${BASENAME}.zip" > "${BASENAME}.zip.sha256" )

# Persistent publish.
mkdir -p "${SIGNED_DIR}"
install -m 0644 "${SIGNED_EXE}"                       "${SIGNED_DIR}/${BASENAME}.exe"
install -m 0644 "${WORKDIR}/${BASENAME}.exe.sha256"   "${SIGNED_DIR}/${BASENAME}.exe.sha256"
install -m 0644 "${ZIP_PATH}"                         "${SIGNED_DIR}/${BASENAME}.zip"
install -m 0644 "${WORKDIR}/${BASENAME}.zip.sha256"   "${SIGNED_DIR}/${BASENAME}.zip.sha256"

if [[ $IS_TAG -eq 1 ]]; then
  ( cd "${SIGNED_DIR}"
    ln -sfn "${BASENAME}.exe"        "FileHasher-latest.exe"
    ln -sfn "${BASENAME}.exe.sha256" "FileHasher-latest.exe.sha256"
    ln -sfn "${BASENAME}.zip"        "FileHasher-latest.zip"
    ln -sfn "${BASENAME}.zip.sha256" "FileHasher-latest.zip.sha256"
  )
  echo "FileHasher-latest.* symlinks updated."
fi

echo
echo "Persistent deliverables in ${SIGNED_DIR}:"
ls -la "${SIGNED_DIR}/${BASENAME}".* 2>/dev/null || true
