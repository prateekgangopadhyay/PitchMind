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
  api_password="$(openssl rand -hex 24)"
  redis_password="$(openssl rand -hex 24)"
  printf 'POSTGRES_USER=pitchmind\nPOSTGRES_DB=pitchmind\nPOSTGRES_IMAGE=postgres:17-alpine\nPOSTGRES_PASSWORD=%s\nPITCHMIND_API_PASSWORD=%s\nPOSTGRES_PORT=5432\nREDIS_PASSWORD=%s\nREDIS_PORT=6379\nKAFKA_PORT=9092\n' "$postgres_password" "$api_password" "$redis_password" > docker/.env
  echo "Created gitignored docker/.env with generated local passwords."
fi
require_regular_credentials_file
chmod 600 docker/.env

if ! grep -q '^PITCHMIND_API_PASSWORD=.' docker/.env; then
  command -v openssl >/dev/null || { echo "OpenSSL required to create a local API password" >&2; exit 1; }
  api_password="$(openssl rand -hex 24)"
  printf '\nPITCHMIND_API_PASSWORD=%s\n' "$api_password" >> docker/.env
fi

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

postgres_major() {
  local image="$1"
  if [[ "$image" =~ (^|/)postgres:([0-9]+)([-.]|$) ]]; then
    printf '%s' "${BASH_REMATCH[2]}"
  else
    return 1
  fi
}

read_local_postgres_image() {
  local line count=0 value=""
  if [[ ${POSTGRES_IMAGE+x} ]]; then
    value="${POSTGRES_IMAGE}"
    [[ -n "$value" ]] || value="postgres:17-alpine"
  else
    while IFS= read -r line || [[ -n "$line" ]]; do
      [[ "$line" =~ ^[[:space:]]*(export[[:space:]]+)?POSTGRES_IMAGE([[:space:]]|=|:|$) ]] || continue
      count=$((count + 1))
      if [[ "$line" =~ ^POSTGRES_IMAGE=([^[:space:]#]+)$ ]]; then
        value="${BASH_REMATCH[1]}"
      else
        echo "docker/.env must contain at most one plain POSTGRES_IMAGE=postgres:<major>-alpine setting; Compose image was not started." >&2
        return 1
      fi
    done < docker/.env
    [[ "$count" -le 1 ]] || {
      echo "docker/.env contains duplicate POSTGRES_IMAGE settings; Compose image was not started." >&2
      return 1
    }
    [[ "$count" -eq 1 ]] || value="postgres:17-alpine"
  fi
  [[ "$value" =~ ^postgres:[0-9]+-alpine$ ]] || {
    echo "POSTGRES_IMAGE must be a postgres:<major>-alpine image; Compose image was not started." >&2
    return 1
  }
  printf '%s' "$value"
}

ensure_existing_postgres_volume_compatible() {
  local configured_image="$1" compose_config postgres_volume existing_volumes volume_name volume_exists=0 configured_major data_version data_major
  if ! compose_config="$(env -u POSTGRES_PASSWORD -u PITCHMIND_API_PASSWORD -u REDIS_PASSWORD \
    docker compose --env-file docker/.env -f docker/compose.yml config --format json)"; then
    echo "Cannot resolve the local Compose configuration; refusing startup to protect local data." >&2
    return 1
  fi
  if ! postgres_volume="$(printf '%s' "$compose_config" | python3 -c '
import json, re, sys
try:
    config = json.load(sys.stdin)
    name = config["volumes"]["postgres-data"]["name"]
except (KeyError, TypeError, json.JSONDecodeError):
    raise SystemExit(1)
if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,254}", name):
    raise SystemExit(1)
print(name)
')"; then
    echo "Cannot determine a valid resolved PostgreSQL data volume; refusing Compose startup to protect local data." >&2
    return 1
  fi
  [[ -n "$postgres_volume" ]] || {
    echo "Cannot determine the configured PostgreSQL data volume; refusing Compose startup to protect local data." >&2
    return 1
  }
  existing_volumes="$(docker volume ls -q --filter "name=$postgres_volume")"
  while IFS= read -r volume_name; do
    [[ "$volume_name" == "$postgres_volume" ]] && volume_exists=1
  done <<< "$existing_volumes"
  [[ "$volume_exists" -eq 1 ]] || return 0
  if ! configured_major="$(postgres_major "$configured_image")"; then
    echo "Cannot verify the configured PostgreSQL image; refusing Compose startup to protect local data." >&2
    return 1
  fi
  if ! data_version="$(docker run --rm --pull=never --network none \
    --mount "type=volume,source=$postgres_volume,target=/pgdata,readonly" \
    --entrypoint /bin/sh "$configured_image" -c 'cat /pgdata/PG_VERSION' 2>/dev/null)"; then
    echo "Cannot read PostgreSQL PG_VERSION from existing volume $postgres_volume; refusing Compose startup to protect local data." >&2
    return 1
  fi
  if [[ "$data_version" =~ ^([0-9]+)(\.[0-9]+)?$ ]]; then
    data_major="${BASH_REMATCH[1]}"
  else
    echo "Cannot parse PostgreSQL PG_VERSION from existing volume $postgres_volume; refusing Compose startup to protect local data." >&2
    return 1
  fi
  if [[ "$configured_major" != "$data_major" ]]; then
    echo "Refusing Compose startup: volume $postgres_volume contains PostgreSQL major version $data_major but POSTGRES_IMAGE requests $configured_major. The data volume was not changed. Back up and perform a PostgreSQL major-version migration before changing the configured image." >&2
    return 1
  fi
}

if ! postgres_password="$(read_local_password POSTGRES_PASSWORD)" \
  || ! api_password="$(read_local_password PITCHMIND_API_PASSWORD)" \
  || ! redis_password="$(read_local_password REDIS_PASSWORD)" \
  || [[ "$postgres_password" == "$api_password" ]] \
  || [[ "$postgres_password" == "$redis_password" ]] \
  || [[ "$api_password" == "$redis_password" ]]; then
  echo "docker/.env must contain distinct 48-character lowercase hexadecimal POSTGRES_PASSWORD, PITCHMIND_API_PASSWORD and REDIS_PASSWORD values with at least 12 unique hex digits each. Replace the local values; secrets were not displayed." >&2
  exit 1
fi

if ! postgres_image="$(read_local_postgres_image)"; then
  exit 1
fi
ensure_existing_postgres_volume_compatible "$postgres_image"

if ! env -u POSTGRES_PASSWORD -u PITCHMIND_API_PASSWORD -u REDIS_PASSWORD \
  docker compose --env-file docker/.env -f docker/compose.yml up -d --wait; then
  echo "Local services did not become healthy. Inspect Docker logs, then run ./scripts/dev-down.sh to stop the partial stack; volumes are preserved." >&2
  exit 1
fi
echo "Local PostgreSQL, Redis and Kafka are healthy."
