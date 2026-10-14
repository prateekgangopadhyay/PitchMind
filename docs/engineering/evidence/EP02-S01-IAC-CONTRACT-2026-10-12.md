# EP02-S01 source dependency contract evidence — 2026-10-12

**Scope:** locally validate the repository's Azure resource-to-workload dependency matrix. This is source-level evidence only. It is not a fresh Azure inventory, what-if, deployment, or private-connectivity test.

**Code commits:** `a96694fc9f9e10923bd5210d62e733f6121eef2e`, `d8fd0aa0c54964dc542060f12456b3012cfba926` on `codex/ep02-s01-iac-dependency-contract`.

## Change

- Added the versioned 27-row `infra/resource-dependency-contract.json` for declared, candidate-only, demo-only, missing, and deferred infrastructure dependencies.
- Added `scripts/validate_iac_dependency_contract.py`, which matches declared Bicep symbols, checks candidate opt-in gates, requires critical missing workload/identity/monitoring rows, and rejects available/reachable claims for disabled-public-access services without reviewable repository evidence.
- Added regression tests for duplicate/missing rows, stale declaration symbols, gate drift, missing private-connectivity evidence, evidence-path traversal, and symlinked directories.
- Connected the validator to `scripts/validate-all.sh` and documented that passing it makes no Azure/runtime claim.
- No C4, ADR, public API, Bicep resource, or Azure environment contract changed.

## Checks run

| Check | Result |
|---|---|
| `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_iac_dependency_contract -v` | 8 passed, 0 failed. |
| `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_iac_dependency_contract.py` | Passed; 27 dependency rows, no Azure calls. |
| `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py'` | 72 passed, 0 failed. |
| `node scripts/validate_operating_model.mjs` | Passed; 12 epics, 59 stories, 28 Council skills, 0 routing cycles. |
| `node scripts/validate_operating_model.mjs --self-test` | Passed. |
| `bash -n scripts/validate-all.sh`; `git diff --check` | Passed. |

`AZURE_CONFIG_DIR=/private/tmp/pitchmind-ep02-azconfig az bicep version` reported that the Bicep CLI is not installed. No installation was attempted. No Azure login, inventory, what-if, resource mutation, or deployment was run.

## Acceptance limits

The matrix records private Storage and Key Vault as blocked pending tested routes, DNS, and scoped identities. The PostgreSQL/ACA candidate remains disabled and unconnected. The new tests prove repository-contract consistency only. Resource existence and policy, subnet/SKU feasibility, identity and RBAC, DNS resolution, workload smoke, diagnostics and cost controls remain unverified. EP02-S01 remains unaccepted pending independent review, fresh permitted evidence, and Product Owner acceptance. No Azure DEV deployment or Azure resource mutation was performed.
