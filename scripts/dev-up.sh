#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
command -v docker >/dev/null || { echo "Docker CLI not installed" >&2; exit 1; }
docker info >/dev/null || { echo "Start Docker Desktop first" >&2; exit 1; }
if [[ ! -f docker/.env ]]; then
  command -v openssl >/dev/null || { echo "OpenSSL required for local password generation" >&2; exit 1; }
  umask 077
  postgres_password="$(openssl rand -hex 24)"
  redis_password="$(openssl rand -hex 24)"
  printf 'POSTGRES_USER=pitchmind\nPOSTGRES_DB=pitchmind\nPOSTGRES_PASSWORD=%s\nPOSTGRES_PORT=5432\nREDIS_PASSWORD=%s\nREDIS_PORT=6379\n' "$postgres_password" "$redis_password" > docker/.env
  echo "Created gitignored docker/.env with generated local passwords."
fi
[[ ! -L docker/.env ]] || { echo "Refusing to use docker/.env symlink; keep local credentials in a regular file." >&2; exit 1; }
chmod 600 docker/.env
docker compose --env-file docker/.env -f docker/compose.yml up -d --wait
docker compose --env-file docker/.env -f docker/compose.yml exec -T postgres sh -c \
  'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -f /docker-entrypoint-initdb.d/10-local-outbox-deliveries.sql' >/dev/null
echo "Local PostgreSQL and Redis are healthy."
