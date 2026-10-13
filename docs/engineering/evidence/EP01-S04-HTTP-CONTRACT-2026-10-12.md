# EP01-S04 live HTTP contract evidence — 2026-10-12

**Scope:** Local validation of the existing v1 API contract and fail-closed responses. This is implementation evidence, not story acceptance.
**Base revision:** `862bf06d781ae24940b16fd41043fb6bccf92743` (GitHub `main`).
**Environment:** LOCAL, .NET 10. No Docker, Azure resources, database writes, or deployment.

## Change

- Added `scripts/api-contract-integration.py`, which starts a fresh Release API process bound to an ephemeral `127.0.0.1` port with the local runtime profile.
- The harness runs health and mapped-route checks, Analyst request validation and body-size boundary checks, then starts a separate fresh process for the rate-limit test so earlier requests cannot consume its quota.
- The HTTP checks assert the typed problem content type, status/code/instance fields, matching body/header correlation IDs, and the v1 contract header; unsupported content type and a genuinely omitted `Content-Type` header are separate cases.
- Fixed `scripts/api-smoke.py`: its valid Analyst request now includes the required `audience` field.
- Wired the harness into `scripts/validate-all.sh` and `pipelines/templates/validate-dotnet.yml` after the Release build.

## Verification actually run

- `dotnet restore PitchMind.slnx -p:NuGetAudit=false --ignore-failed-sources`: completed successfully. NuGet audit was disabled.
- `dotnet build PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false`: passed, 0 warnings and 0 errors.
- `dotnet test tests/PitchMind.Contracts.Tests/PitchMind.Contracts.Tests.csproj -c Release -m:1 --disable-build-servers --no-build --no-restore -p:NuGetAudit=false`: 16 passed, 0 failed.
- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-build --no-restore -p:NuGetAudit=false`: 60 passed, 0 failed, 5 skipped. All five skips are optional PostgreSQL integration tests without a configured test database.
- `python3 scripts/api-contract-integration.py`: passed against two fresh loopback API processes. Route and validation checks passed, including whitespace-only insight ID and a request with no `Content-Type` header; the clean-process rate-limit check observed 30 typed 503 responses and a typed 429 on request 31.
- `python3 -m unittest discover -s scripts -p 'test_*.py' -v`: 72 passed.
- `node scripts/validate_operating_model.mjs --self-test`: passed.
- `git diff --check`: passed.

The first integration attempt correctly caught that running the rate-limit test in the same process after request-contract checks consumed part of its fixed-window quota. The harness now runs that check in a separate process; the final run passed.

## Limits

This does not prove an Azure DevOps hosted run, branch protection, deployed behavior, canonical match data, PostgreSQL integration, or Product Owner acceptance. No cloud service was accessed or changed.
