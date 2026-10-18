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
postgres_port="$(read_setting POSTGRES_PORT 5432)"
postgres_db="$(read_setting POSTGRES_DB pitchmind)"
kafka_port="$(read_setting KAFKA_PORT 9092)"
[[ "$api_password" =~ ^[0-9a-f]{48}$ ]] || {
  echo "Local credentials are invalid; rerun ./scripts/dev-up.sh to validate them." >&2
  exit 1
}
for port in "$postgres_port" "$kafka_port"; do
  [[ "$port" =~ ^[0-9]{1,5}$ ]] && ((port > 0 && port <= 65535)) || {
    echo "Local service ports in docker/.env must be between 1 and 65535." >&2
    exit 1
  }
done

export Runtime__Profile=local
export CandidateFeed__Enabled=true
export ConnectionStrings__PitchMind="Host=127.0.0.1;Port=${postgres_port};Username=pitchmind_api;Password=${api_password};Database=${postgres_db};Include Error Detail=false"
export Kafka__BootstrapServers="127.0.0.1:${kafka_port}"
export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_ENVIRONMENT=Development

exec dotnet run --project src/PitchMind.EventWorker/PitchMind.EventWorker.csproj --configuration Release --no-restore
