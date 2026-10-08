#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore PitchMind.slnx
python3 scripts/check_nuget_vulnerabilities.py PitchMind.slnx
dotnet build PitchMind.slnx --no-restore -c Release
dotnet test PitchMind.slnx --no-build -c Release
(
  cd frontend
  if [[ -f package-lock.json ]]; then npm ci; else npm install --no-audit --no-fund; fi
  npm audit --audit-level=high
  npm run build
  npm test
  npm run lint
)
python3 -m unittest discover -s scripts -p 'test_profile_simulation_feed.py' -v
python3 -m unittest discover -s scripts -p 'test_check_nuget_vulnerabilities.py' -v
node scripts/validate_operating_model.mjs
node scripts/validate_operating_model.mjs --self-test
node scripts/test_design_skills.mjs
echo "PASS: .NET, dependency audits, frontend, feed profiler, design and council contract validation"
