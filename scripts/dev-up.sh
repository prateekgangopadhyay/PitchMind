#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
require_regular_credentials_file() {
  [[ ! -L docker/.env ]] || {
    echo "Refusing to use docker/.env symlink; keep local credentials in a regular file." >&2
    exit 1
  }
  if [[ -e docker/.env || -L docker/.env ]]; then
    [[ -f docker/.env ]] || {
      echo "Refusing non-regular docker/.env path; keep local credentials in a regular file." >&2
      exit 1
    }
  fi
}

require_regular_credentials_file
command -v docker >/dev/null || { echo "Docker CLI not installed" >&2; exit 1; }

source scripts/lib/local-docker-target.sh
require_local_docker_target
docker info >/dev/null || { echo "Start Docker Desktop first" >&2; exit 1; }
if [[ ! -f docker/.env ]]; then
  command -v openssl >/dev/null || { echo "OpenSSL required for local password generation" >&2; exit 1; }
  umask 077
  postgres_password="$(openssl rand -hex 24)"
  redis_password="$(openssl rand -hex 24)"
  printf 'POSTGRES_USER=pitchmind\nPOSTGRES_DB=pitchmind\nPOSTGRES_PASSWORD=%s\nPOSTGRES_PORT=5432\nREDIS_PASSWORD=%s\nREDIS_PORT=6379\n' "$postgres_password" "$redis_password" > docker/.env
  echo "Created gitignored docker/.env with generated local passwords."
fi
require_regular_credentials_file
chmod 600 docker/.env

read_local_password() {
  local key="$1" line count=0 value="" unique_chars="" char index
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ "$line" =~ ^[[:space:]]*(export[[:space:]]+)?${key}([[:space:]]|=|:|$) ]] || continue
    count=$((count + 1))
    if [[ "$line" =~ ^[[:space:]]*(export[[:space:]]+)?${key}[[:space:]]*[:=][[:space:]]*(.*)$ ]]; then
      value="${BASH_REMATCH[2]}"
    else
      value=""
    fi
  done < docker/.env
  [[ "$count" -eq 1 && "$value" =~ ^[0-9a-f]{48}$ ]] || return 1
  for ((index = 0; index < ${#value}; index++)); do
    char="${value:index:1}"
    [[ "$unique_chars" == *"$char"* ]] || unique_chars+="$char"
  done
  [[ ${#unique_chars} -ge 12 ]] || return 1
  printf '%s' "$value"
}

if ! postgres_password="$(read_local_password POSTGRES_PASSWORD)" \
  || ! redis_password="$(read_local_password REDIS_PASSWORD)" \
  || [[ "$postgres_password" == "$redis_password" ]]; then
  echo "docker/.env must contain distinct 48-character lowercase hexadecimal POSTGRES_PASSWORD and REDIS_PASSWORD values with at least 12 unique hex digits each. Replace the local values; secrets were not displayed." >&2
  exit 1
fi

if ! env -u POSTGRES_PASSWORD -u REDIS_PASSWORD \
  docker compose --env-file docker/.env -f docker/compose.yml up -d --wait; then
  echo "Local services did not become healthy. Inspect Docker logs, then run ./scripts/dev-down.sh to stop the partial stack; volumes are preserved." >&2
  exit 1
fi
echo "Local PostgreSQL and Redis are healthy."
