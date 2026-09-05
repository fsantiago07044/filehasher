#!/usr/bin/env bash
#
# ci/dispatch-scoop.sh — runs in the `scoop` stage on the Linux signer.
#
# Nudges the Excavator workflow in the Scoop bucket
# (github.com/fsantiago07044/scoop-bucket) so it picks up this release
# immediately instead of waiting for its own schedule.
#
# Scoop is the one PULL-based channel: nothing is published to it. Excavator
# runs on a cron ('20 */4 * * *'), reads the GitHub Release through the
# manifest's checkver, and commits the new version and checksum itself. This
# job only shortens the wait from up to four hours to about a minute.
#
# The more valuable effect is turning a silent failure loud. GitHub disables a
# scheduled workflow after 60 days of repository inactivity, which a long gap
# between releases can trigger. Without this job, the first anyone would learn
# of it is Scoop users quietly sitting on a stale version. With it, the
# dispatch fails here and yellow-flags the pipeline at release time.
#
# Note it depends on mirror-github having succeeded: Excavator's whole view of
# the world is the GitHub Release. That job is allow_failure, so if the mirror
# failed there is nothing for Excavator to find, and this job's own success
# would be misleading. It therefore checks the release exists first.
#
# Inputs:
#   VERSION             from the build stage's dotenv report
#   SCOOP_DISPATCH_PAT  Protected + Masked CI/CD variable. Fine-grained PAT,
#                       scoped to the scoop-bucket repo, Actions: read+write
#                       and nothing else (see ci/README.md section 1f).

set -euo pipefail

REPO="fsantiago07044/scoop-bucket"
WORKFLOW="excavator.yml"
MIRROR="fsantiago07044/filehasher"

[ -n "${VERSION:-}" ]            || { echo "VERSION is not set" >&2; exit 1; }
[ -n "${SCOOP_DISPATCH_PAT:-}" ] || { echo "SCOOP_DISPATCH_PAT is not set; see ci/README.md section 1f." >&2; exit 1; }

auth=(-H "Authorization: Bearer ${SCOOP_DISPATCH_PAT}"
      -H "Accept: application/vnd.github+json"
      -H "X-GitHub-Api-Version: 2022-11-28")

# Excavator can only see what mirror-github published.
code=$(curl -s -o /dev/null -w '%{http_code}' "https://api.github.com/repos/${MIRROR}/releases/tags/v${VERSION}")
if [ "$code" != "200" ]; then
  echo "GitHub release v${VERSION} not found (http ${code}). Excavator reads the GitHub Release," >&2
  echo "so there is nothing for it to pick up. Check the mirror-github job first." >&2
  exit 1
fi
echo "GitHub release v${VERSION} is present"

# A workflow disabled for inactivity is the failure this job exists to surface.
state=$(curl -s "${auth[@]}" "https://api.github.com/repos/${REPO}/actions/workflows/${WORKFLOW}" \
        | sed -n 's/.*"state"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)
echo "excavator workflow state: ${state:-unknown}"
if [ "$state" != "active" ]; then
  echo "The Excavator workflow is not active (state: ${state:-unknown})." >&2
  echo "GitHub disables scheduled workflows after 60 days of repository inactivity." >&2
  echo "Re-enable it in the Actions tab of https://github.com/${REPO} and re-run this job." >&2
  exit 1
fi

code=$(curl -s -o /dev/null -w '%{http_code}' -X POST "${auth[@]}" \
        -d '{"ref":"master"}' \
        "https://api.github.com/repos/${REPO}/actions/workflows/${WORKFLOW}/dispatches")
if [ "$code" != "204" ]; then
  echo "Dispatch was refused (http ${code})." >&2
  exit 1
fi

echo "Excavator dispatched. It will pick up v${VERSION} and commit the manifest bump."
echo "  https://github.com/${REPO}/actions/workflows/${WORKFLOW}"
