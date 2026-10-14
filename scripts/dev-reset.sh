#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "${1:-}" == "--force" ]]; then
  [[ $# -eq 1 ]] || { echo "Usage: ./scripts/dev-reset.sh [--force]" >&2; exit 2; }
else
  [[ $# -eq 0 ]] || { echo "Usage: ./scripts/dev-reset.sh [--force]" >&2; exit 2; }
  [[ -t 0 ]] || { echo "Reset removes all local PostgreSQL and Redis data; run interactively or pass --force." >&2; exit 2; }
  echo "This permanently deletes the local PostgreSQL and Redis volumes."
  read -r -p 'Type RESET to continue: ' confirmation
  [[ "$confirmation" == "RESET" ]] || { echo "Reset cancelled."; exit 1; }
fi

[[ -f docker/.env && ! -L docker/.env ]] || { echo "Run ./scripts/dev-up.sh first; no regular docker/.env found." >&2; exit 1; }
source scripts/lib/local-docker-target.sh
require_local_docker_target
docker compose --env-file docker/.env -f docker/compose.yml down --volumes --remove-orphans
echo "Local PostgreSQL and Redis data volumes removed. The local credentials file was retained."
