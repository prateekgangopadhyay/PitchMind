#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 2 ]]; then
  printf 'Usage: %s <source-directory> <output-spdx-json>\n' "$0" >&2
  exit 2
fi

sbom_source="$1"
sbom_output_input="$2"
if [[ ! -d "$sbom_source" ]]; then
  printf 'SBOM source must be a directory: %s\n' "$sbom_source" >&2
  exit 2
fi
sbom_source="$(cd -- "$sbom_source" && pwd -P)"
if [[ -e "$sbom_output_input" || -L "$sbom_output_input" ]]; then
  printf 'SBOM output path must not already exist or be a symlink.\n' >&2
  exit 2
fi
sbom_output="$(python3 -c 'import pathlib,sys; print(pathlib.Path(sys.argv[1]).expanduser().resolve())' "$sbom_output_input")"
case "$sbom_output" in
  "$sbom_source"|"$sbom_source"/*)
    printf 'SBOM output must be outside the scanned source directory.\n' >&2
    exit 2
    ;;
esac
sbom_output_parent="$(mkdir -p -- "$(dirname -- "$sbom_output")" && cd -- "$(dirname -- "$sbom_output")" && pwd -P)"
sbom_output="${sbom_output_parent}/$(basename -- "$sbom_output")"

syft_image='ghcr.io/anchore/syft:v1.54.1@sha256:3eb5379ba7b409c3f4069b686110527af0c47df993fa5c10d13e7cf34f49b1aa'
if ! command -v docker >/dev/null 2>&1; then
  printf 'Docker is required to generate the pinned source SBOM.\n' >&2
  exit 2
fi
if ! docker image inspect "$syft_image" >/dev/null 2>&1; then
  docker pull "$syft_image" >/dev/null
fi

sbom_version="${PITCHMIND_SBOM_VERSION:-working-tree}"
docker run --rm \
  --network=none \
  --read-only \
  --tmpfs /tmp:rw,nosuid,nodev,noexec,size=64m,mode=1777 \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  -e HOME=/tmp \
  -e SYFT_CACHE_DIR=/tmp/syft-cache \
  -e SYFT_CHECK_FOR_APP_UPDATE=false \
  -v "${sbom_source}:/scan:ro" \
  -v "${sbom_output_parent}:/out:rw" \
  "$syft_image" \
  scan dir:/scan \
  --exclude '**/.git/**' \
  --exclude '**/node_modules/**' \
  --exclude '**/bin/**' \
  --exclude '**/obj/**' \
  --exclude '**/dist/**' \
  --exclude '**/__pycache__/**' \
  --exclude '**/.venv/**' \
  --exclude '**/venv/**' \
  --source-name pitchmind-source \
  --source-version "$sbom_version" \
  --quiet \
  -o "spdx-json=/out/$(basename -- "$sbom_output")"

test -s "$sbom_output"
