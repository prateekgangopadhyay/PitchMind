#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

api_password="$(grep '^PITCHMIND_API_PASSWORD=' docker/.env | cut -d= -f2)"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://127.0.0.1:5080"
export Runtime__Profile=local
export ConnectionStrings__PitchMind="Host=127.0.0.1;Port=5432;Username=pitchmind_api;Password=${api_password};Database=pitchmind;Include Error Detail=false"
export Identity__DevelopmentHeaderAuthEnabled=true
export Realtime__Provider=local

exec dotnet run --project src/PitchMind.Api/PitchMind.Api.csproj --configuration Release --no-restore
