# EP01-S04 contract verification — 2026-10-15

**Scope:** Revalidate the existing shared v1 DTO, error and selected-time contracts on a child stack of EP01-S01 draft PR #280. This slice updates React DTO types and adds compile-time plus Vitest contract checks; it does not change API runtime behavior.

**Base tree:** `76147d1dfc9d78f0b9999f97203ee8b28f268412`, which contains the EP01-S01 code commit `610a7644240a55584f059a924af831e51c30ed72` based on main `3df1d0aacaecc424181c95824fab73bcf99659e6`. EP01-S04 changes and this evidence update were uncommitted during verification; the test results below were observed against those working-tree changes.

## Verification observed

- `dotnet test tests/PitchMind.Contracts.Tests/PitchMind.Contracts.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` — 146 passed, 0 failed, 0 skipped.
- `python3 scripts/api-contract-integration.py` — passed health and fail-closed routes, malformed/invalid Analyst requests, media-type handling, exact 4096-byte and oversized content-length/chunked request limits, and 30/31 request rate-limit boundary.
- The error matrix returned `application/problem+json` and verified typed status/code, correlation and contract-version headers.
- An independent review found missing React fields for competition context and source hashes; the React DTOs now match the corresponding C# response contracts, and `api.contract.test.ts` pins those fields at compile and unit-test time, including required-property checks.
- `cd frontend && npm test` — 89 passed across 6 files.
- `cd frontend && npm run build` — passed; Vite emitted only the existing SignalR pure-annotation warnings.
- `cd frontend && npm run lint` — passed.
- Independent static re-review of the final DTO and type-test diff at base HEAD `76147d1dfc9d78f0b9999f97203ee8b28f268412`, including the uncommitted working-tree changes — no actionable findings. The reviewer did not run tests; verification results above are from this local run.

## Limits and acceptance

This run verifies the legacy fail-closed contract; it does not verify the separate PostgreSQL-backed canonical BFF route family, live database behavior, hosted Azure DevOps, accessibility, or Product Owner acceptance. NuGet audit was disabled. EP01-S04 remains unaccepted. No Azure operation or deployment occurred.
