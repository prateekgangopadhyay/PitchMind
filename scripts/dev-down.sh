#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
[[ -f docker/.env ]] || { echo "No docker/.env found; nothing to stop."; exit 0; }
source scripts/lib/local-docker-target.sh
require_local_docker_target
docker compose --env-file docker/.env -f docker/compose.yml down
