#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore PitchMind.slnx
dotnet build PitchMind.slnx --no-restore -c Release
dotnet test PitchMind.slnx --no-build -c Release
(
  cd frontend
  if [[ -f package-lock.json ]]; then npm ci; else npm install --no-audit --no-fund; fi
  npm run build
  npm test
  npm run lint
)
echo "PASS: .NET and frontend validation"
