# EP01-S01 current-main verification — 2026-10-15

## Scope and status

This local verification checks the existing EP01-S01 implementation at source revision `385efff02d95cbea93f7f74cccf42ea6dd4e7d61`. No implementation code changed in this verification increment. The implementation is locally verified for the checks listed below; independent technical review, hosted CI, and Product Owner acceptance remain pending. EP01-S01 is not accepted.

The source project map in `tests/PitchMind.Architecture.Tests/DependencyTests.cs` is consistent with the seven project responsibilities in `docs/architecture/02-container.md`. The architecture suite evaluates imported and target-created MSBuild items and resolved/compiler references, and it checks the actual source-project graph. `ForbiddenDomainReference_IsRejectedByTheArchitecturePolicy` is a negative policy regression that injects a forbidden `PitchMind.Domain` → `PitchMind.Infrastructure` edge into the policy input and asserts that the edge is rejected.

## Verification performed

- Resolved SDK: .NET SDK `10.0.401` selected by `global.json` (`10.0.100`, `latestFeature` roll-forward).
- `dotnet clean PitchMind.slnx -c Release -m:1 --disable-build-servers` — passed, 0 warnings and 0 errors.
- `dotnet build PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false` — passed, 0 warnings and 0 errors.
- `dotnet test tests/PitchMind.Architecture.Tests/PitchMind.Architecture.Tests.csproj -c Release --no-build --no-restore -m:1 --disable-build-servers --logger 'console;verbosity=minimal'` — 24 passed, 0 failed, 0 skipped.
- The same architecture test project filtered to `FullyQualifiedName~ForbiddenDomainReference_IsRejectedByTheArchitecturePolicy` — 1 passed, 0 failed, 0 skipped.
- Supplementary local data-path check, `PITCHMIND_DOCKER_ENV=/Users/prateekgangopadhyay/Code/PitchMind/docker/.env ./scripts/test-ep07-s05-live-api-e2e.py` — PostgreSQL migration/fixture seed 1/1; API Release build 0 warnings/errors; persisted PostgreSQL → local protected API → Playwright journey 1/1. The fixture contains explicit test-only rows and does not use the candidate feed.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, no dependency cycles.
- `git diff --check` — passed.

VSTest's first sandboxed attempt could not bind its loopback communication socket before executing tests. The same test commands were rerun with local loopback permission and passed. NuGet audit was disabled for the local no-restore build; no package audit result is claimed.

## Remaining evidence and limits

- Full-solution tests, the full PostgreSQL integration suite, frontend unit tests, a separate legacy API smoke, Azure DevOps hosted CI, Azure operations, and deployment were not run for this verification increment. The supplementary live journey above is a single selected API/browser scenario, not full PostgreSQL or Azure acceptance.
- The production fixture scanner is a lexical regression heuristic, not a proof against every possible runtime-generated or encoded football literal.
- The candidate Manchester United–Liverpool JSON remains unadmitted: its clock/period semantics, coordinate units and orientation, player metric meanings, canonical event mappings, and football realism have not been certified. The local canonical BFF can read persisted PostgreSQL projections, but that does not certify or serve this candidate feed.
- The independent read-only audit identified the API/status wording correction recorded in the follow-up PR. No source, build, or test implementation finding was reported. Human technical review and Product Owner decision remain pending.
- No Azure resource or deployment action occurred.
