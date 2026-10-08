#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
[[ -f docker/.env ]] || { echo "Run ./scripts/dev-up.sh first" >&2; exit 1; }
docker compose --env-file docker/.env -f docker/compose.yml ps
postgres_id="$(docker compose --env-file docker/.env -f docker/compose.yml ps -q postgres)"
redis_id="$(docker compose --env-file docker/.env -f docker/compose.yml ps -q redis)"
[[ -n "$postgres_id" && -n "$redis_id" ]] || { echo "Containers missing" >&2; exit 1; }
[[ "$(docker inspect -f '{{.State.Health.Status}}' "$postgres_id")" == healthy ]] || { echo "PostgreSQL unhealthy" >&2; exit 1; }
[[ "$(docker inspect -f '{{.State.Health.Status}}' "$redis_id")" == healthy ]] || { echo "Redis unhealthy" >&2; exit 1; }
echo "PASS: PostgreSQL and Redis healthy"
