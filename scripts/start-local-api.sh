#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

[[ ! -L docker/.env && -f docker/.env ]] || {
  echo "Run ./scripts/dev-up.sh first to create the private local credentials file." >&2
  exit 1
}
read_setting() {
  local key="$1" fallback="${2:-}"
  local count
  count="$(grep -c "^${key}=" docker/.env || true)"
  if [[ "$count" -eq 0 && -n "$fallback" ]]; then printf '%s' "$fallback"; return; fi
  if [[ "$count" -ne 1 ]]; then echo "docker/.env must contain exactly one ${key} setting." >&2; return 1; fi
  sed -n "s/^${key}=//p" docker/.env | head -n 1
}
api_password="$(read_setting PITCHMIND_API_PASSWORD "")"
redis_password="$(read_setting REDIS_PASSWORD "")"
postgres_port="$(read_setting POSTGRES_PORT 5432)"
redis_port="$(read_setting REDIS_PORT 6379)"
kafka_port="$(read_setting KAFKA_PORT 9092)"
postgres_db="$(read_setting POSTGRES_DB pitchmind)"
[[ "$api_password" =~ ^[0-9a-f]{48}$ && "$redis_password" =~ ^[0-9a-f]{48}$ ]] || {
  echo "Local credentials are invalid; rerun ./scripts/dev-up.sh to validate them." >&2
  exit 1
}
for port in "$postgres_port" "$redis_port" "$kafka_port"; do
  [[ "$port" =~ ^[0-9]{1,5}$ ]] && ((port > 0 && port <= 65535)) || {
    echo "Local service ports in docker/.env must be between 1 and 65535." >&2
    exit 1
  }
done
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://127.0.0.1:5080"
export Runtime__Profile=local
export CandidateFeed__Enabled=true
export ConnectionStrings__PitchMind="Host=127.0.0.1;Port=${postgres_port};Username=pitchmind_api;Password=${api_password};Database=${postgres_db};Include Error Detail=false"
export Redis__ConnectionString="127.0.0.1:${redis_port},password=${redis_password},ssl=false"
export Kafka__BootstrapServers="127.0.0.1:${kafka_port}"
export Identity__DevelopmentHeaderAuthEnabled=true
export Realtime__Provider=local

exec dotnet run --project src/PitchMind.Api/PitchMind.Api.csproj --configuration Release --no-restore
