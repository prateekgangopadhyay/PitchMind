#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore PitchMind.slnx --locked-mode
python3 scripts/check_nuget_vulnerabilities.py PitchMind.slnx
dotnet build PitchMind.slnx --no-restore -c Release
dotnet test PitchMind.slnx --no-build -c Release
python3 scripts/api-contract-integration.py
(
  cd frontend
  if [[ -f package-lock.json ]]; then npm ci; else npm install --no-audit --no-fund; fi
  # Low is the minimum npm advisory severity; fail on any reported severity.
  npm audit --audit-level=low
  npm run build
  npm test
  npm run lint
)
python3 -m unittest discover -s scripts -p 'test_*.py' -v
python3 scripts/test_secret_scan.py -v
python3 scripts/test_generate_sbom.py -v
bash scripts/secret-scan.sh
sbom_temp_dir="$(mktemp -d)"
trap 'rm -rf "$sbom_temp_dir"' EXIT
PITCHMIND_SBOM_VERSION="$(git rev-parse --short HEAD)" \
  bash scripts/generate-sbom.sh . "$sbom_temp_dir/pitchmind-source.spdx.json"
node scripts/validate_operating_model.mjs
node scripts/validate_operating_model.mjs --self-test
node scripts/test_design_skills.mjs
python3 scripts/validate_iac_dependency_contract.py
echo "PASS: .NET, dependency and secret scans, source SBOM, frontend, all script tests, IaC dependency contract and council validation"
