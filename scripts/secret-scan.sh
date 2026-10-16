#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
scan_source="${1:-$repo_root}"
if [[ ! -d "$scan_source" ]]; then
  printf 'Secret scan source must be a directory: %s\n' "$scan_source" >&2
  exit 2
fi
scan_source="$(cd -- "$scan_source" && pwd -P)"

# Keep the scanner version pinned and exercise its behavior with a synthetic
# canary that avoids the default stopwords discussed in upstream issue #2170.
gitleaks_image='ghcr.io/gitleaks/gitleaks:v8.29.1@sha256:aa036a2f4bdfe3cc3c55fa4326308efabb4a6be498c883c864fd1d0d5585438a'

if ! command -v docker >/dev/null 2>&1; then
  printf 'Docker is required for the pinned local secret scan.\n' >&2
  exit 2
fi
if ! docker image inspect "$gitleaks_image" >/dev/null 2>&1; then
  docker pull "$gitleaks_image" >/dev/null
fi

# Supply the pinned tool's built-in rules through the runner environment. This
# prevents a repository-local .gitleaks.toml from replacing them. The scanner
# also ignores all inline gitleaks:allow comments below.
trusted_gitleaks_config=$'[extend]\nuseDefault = true\n\n[allowlist]\npaths = [\n  \'\'\'docker/\\.env\'\'\',\n  \'\'\'(^|/)__pycache__/\'\'\',\n]\n'

exec docker run --rm \
  --network=none \
  --read-only \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  -e "GITLEAKS_CONFIG_TOML=$trusted_gitleaks_config" \
  -v "${scan_source}:/scan:ro" \
  -w /scan \
  "$gitleaks_image" \
  dir --ignore-gitleaks-allow --redact --no-banner --no-color /scan
