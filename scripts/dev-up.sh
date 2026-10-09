#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
[[ ! -L docker/.env ]] || { echo "Refusing to use docker/.env symlink; keep local credentials in a regular file." >&2; exit 1; }
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

read_local_password() {
  local key="$1" line count=0 value=""
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ "$line" == "$key="* ]] || continue
    count=$((count + 1))
    value="${line#*=}"
  done < docker/.env
  [[ "$count" -eq 1 && "$value" =~ ^[0-9a-f]{48}$ ]] || return 1
  printf '%s' "$value"
}

if ! postgres_password="$(read_local_password POSTGRES_PASSWORD)" \
  || ! redis_password="$(read_local_password REDIS_PASSWORD)" \
  || [[ "$postgres_password" == "$redis_password" ]]; then
  echo "docker/.env must contain distinct, generated 48-character lowercase hexadecimal POSTGRES_PASSWORD and REDIS_PASSWORD values. Replace the local values; secrets were not displayed." >&2
  exit 1
fi

docker compose --env-file docker/.env -f docker/compose.yml up -d --wait
echo "Local PostgreSQL and Redis are healthy."
