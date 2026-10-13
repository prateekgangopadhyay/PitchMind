# PR #21 current-main restack verification — 2026-10-13

## Stack and scope

PR #21 (`codex/ep01-s01-verification`) had integrated PR #45, its evidence-only PR #168, and EP01-S03 child work, but was based on an older `main` revision and GitHub reported it non-mergeable. The parent feature head before this restack was `ae9dc27c40a490a650c0988a9e8a6607c38c4b07`. Current `main` at restack was `c9c16d9b44643ef8c0eea454fe73a53c8ca7cd1c` (tree `0296291ba147849096a52b9b64449a808affaa89`).

The local merge resolved conflicts across 14 files. It retained current-main API runtime-profile validation and strict analyst request parsing; preserved PR #21's test-only golden-fixture boundary, architecture guards, C4/API updates, and EP01-S03 gate probe; and kept the current-main candidate feed quality dictionaries and structural audit. No canonical source-feed admission or Azure infrastructure operation was added.

Local integration commit: `218826a1e31bd3f0ebd49bad458969b175cf9c48`; tree `fa46a8a3c94e645f01aeff476a333f724f95d8c9`; parents are the previous PR #21 head and current `main` above.

## Executed local evidence

- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false`: **71 passed, 0 failed, 5 skipped**. The five skips are opt-in PostgreSQL tests; no test database connection was configured. NuGet audit was disabled.
- Architecture suite: **22 passed** within the full suite. EP01-S03 deliberate failure test passed **1/1** with `PITCHMIND_GATE_FAILURE_PROBE` unset; with it set to `1`, it failed **1/1** and returned exit code 1 as designed.
- `python3 -m unittest discover -s scripts -p 'test_*.py'`: **91 passed**.
- Frontend using the lockfile and offline npm cache: `npm ci --offline --no-audit --no-fund` completed; `npm test`: **6 passed**; `npm run build`: passed; `npm run lint`: passed.
- `python3 scripts/api-contract-integration.py`: passed. Live loopback checks confirmed `/api/health`, unavailable match routes (503), malformed/invalid analyst payloads (400), unsupported media (415), 4096-byte bound, oversized requests (413), and rate limiting (30 typed 503 responses followed by typed 429). The script started and stopped temporary local API processes; it did not start Docker services.
- `node scripts/validate_operating_model.mjs`: passed (12 epics, 59 stories, 28 Council skills, 5 design skills, 0 cycles, 2 approval gates).
- `git diff --check` against current `main` passed after this evidence update.

## Not verified / decision

Bicep CLI is not installed in this environment, so the candidate topology was not recompiled at this restacked head; previous Bicep compilation evidence applies only to its earlier exact revision. The full PostgreSQL integration path was skipped, no hosted Azure DevOps checks or required-check binding were observed, and no Azure resource inventory, what-if, provisioning, mutation or deployment was performed.

PR #21 remains a draft for exact-head independent technical review. Product Owner acceptance for EP01-S01, EP01-S02, EP01-S03, EP01-S04 and EP02-S01 remains pending. The merge restack and local tests do not accept any story.
