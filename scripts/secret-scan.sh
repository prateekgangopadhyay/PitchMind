#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
scan_source="${1:-$repo_root}"
if [[ ! -d "$scan_source" ]]; then
  printf 'Secret scan source must be a directory: %s\n' "$scan_source" >&2
  exit 2
fi
scan_source="$(cd -- "$scan_source" && pwd -P)"

# Gitleaks v8.30.1 is intentionally not used: its upstream release was reported
# to return no findings for rules that matched in prior versions. Keep the
# scanner on an immutable, verified container digest until a replacement is
# independently exercised against the canary test below.
gitleaks_image='ghcr.io/gitleaks/gitleaks:v8.29.1@sha256:aa036a2f4bdfe3cc3c55fa4326308efabb4a6be498c883c864fd1d0d5585438a'

if ! command -v docker >/dev/null 2>&1; then
  printf 'Docker is required for the pinned local secret scan.\n' >&2
  exit 2
fi
if ! docker image inspect "$gitleaks_image" >/dev/null 2>&1; then
  docker pull "$gitleaks_image" >/dev/null
fi

exec docker run --rm \
  --network=none \
  --read-only \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  -v "${scan_source}:/scan:ro" \
  -w /scan \
  "$gitleaks_image" \
  dir --redact --no-banner --no-color /scan
