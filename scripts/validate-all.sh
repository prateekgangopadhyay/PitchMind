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
python3 -m unittest discover -s scripts -p 'test_profile_simulation_feed.py' -v
echo "PASS: .NET, frontend and JSON schema-profiler unit validation"
