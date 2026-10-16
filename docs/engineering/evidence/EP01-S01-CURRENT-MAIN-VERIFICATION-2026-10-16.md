# EP01-S01 current-main verification — 2026-10-16

## Scope and status

This verification records the architecture implementation at exact current `main` revision `62d5892eb34a9a3219ba1e645f4a5b74ed1eeeb4` (tree `ec8b32aaeb1fde4795d0d2a019120da2b5372dfa`). The full Release tests and build ran on code commit `f690a0db34266e3aa6658e057896f077f88355e1` (tree `dc19d588517a388228de141d3d9cbbd2b740f589`). The two trees differ only in `docs/backlog/STORY-EXECUTION.md` and `docs/engineering/evidence/EP01-S01-NUMERIC-ALIAS-GUARD-2026-10-16.md`; the production and test source files are identical. This increment updates evidence only. The story remains unverified and unaccepted because hosted CI and Product Owner acceptance remain pending.

The architecture implementation maps all seven source projects to the responsibilities documented in [C4 Level 2](../../architecture/02-container.md). The architecture test suite verifies the project-reference map, resolved/compiler references and production fixture-resource boundary. The explicit negative test injects the forbidden `PitchMind.Domain` → `PitchMind.Infrastructure` project edge into the policy input and asserts rejection.

## Verification performed

- SDK selected by `global.json`: .NET SDK `10.0.401`.
- `dotnet restore PitchMind.slnx --locked-mode -p:NuGetAudit=false` — passed; locked packages restored in the local isolated checkout. NuGet audit was disabled, so package vulnerability audit is not evidenced.
- `dotnet clean PitchMind.slnx -c Release -m:1 --disable-build-servers` — passed, 0 warnings and 0 errors.
- `dotnet build PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false` — passed, 0 warnings and 0 errors.
- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-build --no-restore -p:NuGetAudit=false` — 512 passed, 0 failed, 29 skipped. The skipped tests were 28 opt-in PostgreSQL Infrastructure tests and one PostgreSQL readiness Contracts test; none ran.
- Release architecture suite — 26 passed, 0 failed, 0 skipped.
- Filtered `ProductionFixtureGuard_FailsClosedWhenCandidateNumericAliasesAreShadowedOrMutated` regression — 1 passed, 0 failed, 0 skipped.
- Filtered `ForbiddenDomainReference_IsRejectedByTheArchitecturePolicy` regression — 1 passed, 0 failed, 0 skipped.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, 33 discoverable skills, zero cycles, and two approval gates.
- `git diff --check` — passed.

VSTest's sandboxed invocation aborted before test execution because its loopback communication socket was denied. The test suite and filtered negative regression were rerun with local execution permission and passed.

## Remaining evidence and limits

- The 29 skipped PostgreSQL tests were not verified against a database. Hosted Azure DevOps CI, frontend tests, API smoke, and Azure execution were not performed in this verification increment.
- NuGet audit was disabled; no package vulnerability result is claimed.
- The source guard is a bounded lexical regression check, not proof against every possible runtime-generated or encoded football literal.
- Independent read-only review of the exact alias-guard implementation and evidence found no actionable issue; the reviewer did not run tests.
- The candidate Manchester United–Liverpool feed remains unadmitted because time/period semantics, coordinate units/orientation, player metric meanings, canonical event mappings, football realism, and independent rights/privacy review are unresolved.
- Independent human technical review and Product Owner acceptance remain pending. This local test run does not accept EP01-S01.
- No Azure resource or deployment action occurred.
