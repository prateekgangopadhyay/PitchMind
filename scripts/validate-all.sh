#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore PitchMind.slnx
dotnet build PitchMind.slnx --no-restore -c Release
dotnet test PitchMind.slnx --no-build -c Release
python3 scripts/api-contract-integration.py
(
  cd frontend
  if [[ -f package-lock.json ]]; then npm ci; else npm install --no-audit --no-fund; fi
  npm run build
  npm test
  npm run lint
)
python3 -m unittest discover -s scripts -p 'test_*.py' -v
node scripts/validate_operating_model.mjs
node scripts/validate_operating_model.mjs --self-test
node scripts/test_design_skills.mjs
python3 scripts/validate_iac_dependency_contract.py
echo "PASS: .NET, frontend, feed validation, IaC dependency contract and council validation"
