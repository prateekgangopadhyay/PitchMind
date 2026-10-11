# EP06-S04 local restack evidence — 2026-10-15

## Scope

- Story: EP06-S04, Identity, authorization and abuse controls.
- Base: merged EP06-S01 commit `20d1375bc96f4609ec5308bfc48fb27fa81930c4`.
- Work branch: `codex/ep06-s04-main-restack`.
- State: local implementation candidate; independent exact-diff review is complete; Product Owner acceptance is a separate gate. This file records observed implementation and tests only.

## Implemented in this candidate

- Entra JWT bearer configuration requires HTTPS `login.microsoftonline.com`, an audience, signed tokens, issuer and lifetime validation, and the `roles` claim. Azure startup fails closed without this configuration.
- Canonical and legacy match routes require read roles. Replay, evidence, and Analyst operations require the narrower `analyst` or `admin` roles. Development header authentication is opt-in and rejected outside the local Development profile.
- Canonical handlers are mapped in both profiles. Local PostgreSQL readers are registered only for the local profile; Azure currently uses explicit unavailable readers and returns sanitized `503` after authorization. No synthetic fixture fallback or public synthetic demo is served.
- API read and Analyst rate limits run before request-body handling and authorization. Request bodies remain capped at 4 KiB, including chunked bodies. CORS preflight and origin validation, CSP/security headers, typed authorization problems, correlation IDs, and protected-action audit hashing are covered in the API composition.
- API, C4, ADR, OpenAPI, story-packet, and execution-register descriptions were reconciled with this runtime behavior.

## Verification performed

Commands were executed locally on this candidate; no Azure resources or deployments were touched.

- `dotnet test PitchMind.slnx --configuration Release -p:NuGetAudit=false -m:1 --no-restore` — **274 passed, 0 failed, 24 skipped**. Project counts: API 12/12, Application 67/67, Architecture 22/22, Contracts 88/88, Domain 67/67, Infrastructure 18 passed and 24 PostgreSQL-dependent skipped.
- `python3 scripts/api-contract-integration.py` — passed health/fail-closed reads, typed malformed/invalid request responses, exact 4 KiB body boundary, declared and chunked 413 responses, and Analyst 429 behavior.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, 33 discoverable skills, zero cycles, two approval gates.
- `python3 -c 'import yaml; d=yaml.safe_load(open("docs/api/openapi-canonical-bff-v1.yaml")); print("OpenAPI",d["openapi"],"paths",len(d["paths"]))'` — parsed OpenAPI 3.1.0 with 8 paths.
- `git diff --check` — passed.
- A separate read-only reviewer identified documentation drift, missing CORS regression coverage, and rate-limit ordering concerns. This candidate corrects those findings. The reviewer’s final read-only exact-diff recheck found no remaining actionable P0–P2 issues; the reviewer did not execute tests or verify Azure/browser/PostgreSQL behavior. After a test-isolation cleanup, the API test project was rerun separately: 12 passed, 0 failed.

## Remaining limits

- The 24 PostgreSQL integration tests were skipped because no `PITCHMIND_TEST_POSTGRES` connection was configured. Canonical database behavior is not proven by this run.
- No real Entra tenant, issuer metadata, tenant role assignment, or production token was used. The signed JWT test uses a local test signing key and substituted test metadata.
- CORS was checked with ASP.NET TestServer preflight requests; no real browser, ingress, or Azure proxy was exercised. Per-IP partitioning behind the production proxy remains unverified.
- Hosted CI and Product Owner acceptance are pending. A new GitHub PR has not yet been created for this restack. This evidence does not accept EP06-S04 or its dependencies.
- No Azure resource change, infrastructure apply, or deployment occurred.
