# EP01-S01 current-main verification — 2026-10-16

## Scope and status

This verification checks the existing architecture implementation at exact source revision `4928aae433ae36f9b9b178b8beea1c25938ee306` in an isolated local checkout. No implementation code changed. The story remains unverified and unaccepted because independent human technical review, hosted CI, and Product Owner acceptance remain pending.

The architecture implementation maps all seven source projects to the responsibilities documented in [C4 Level 2](../../architecture/02-container.md). The architecture test suite verifies the project-reference map, resolved/compiler references and production fixture-resource boundary. The explicit negative test injects the forbidden `PitchMind.Domain` → `PitchMind.Infrastructure` project edge into the policy input and asserts rejection.

## Verification performed

- SDK selected by `global.json`: .NET SDK `10.0.401`.
- `dotnet restore PitchMind.slnx --locked-mode -p:NuGetAudit=false` — passed; locked packages restored in the local isolated checkout. NuGet audit was disabled, so package vulnerability audit is not evidenced.
- `dotnet clean PitchMind.slnx -c Release -m:1 --disable-build-servers` — passed, 0 warnings and 0 errors.
- `dotnet build PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false` — passed, 0 warnings and 0 errors.
- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-build --no-restore -p:NuGetAudit=false` — 511 passed, 0 failed, 29 skipped. The skipped tests were 28 opt-in PostgreSQL Infrastructure tests and one PostgreSQL readiness Contracts test; none ran.
- `dotnet test tests/PitchMind.Architecture.Tests/PitchMind.Architecture.Tests.csproj -c Release -m:1 --disable-build-servers --no-build --no-restore --filter FullyQualifiedName~ForbiddenDomainReference_IsRejectedByTheArchitecturePolicy -p:NuGetAudit=false` — 1 passed, 0 failed, 0 skipped.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, 33 discoverable skills, zero cycles, and two approval gates.
- `git diff --check` — passed.

VSTest's sandboxed invocation aborted before test execution because its loopback communication socket was denied. The test suite and filtered negative regression were rerun with local execution permission and passed.

## Remaining evidence and limits

- The 29 skipped PostgreSQL tests were not verified against a database. Hosted Azure DevOps CI, frontend tests, API smoke, and Azure execution were not performed in this verification increment.
- NuGet audit was disabled; no package vulnerability result is claimed.
- The source guard is a bounded lexical regression check, not proof against every possible runtime-generated or encoded football literal.
- The candidate Manchester United–Liverpool feed remains unadmitted because time/period semantics, coordinate units/orientation, player metric meanings, canonical event mappings, football realism, and independent rights/privacy review are unresolved.
- Independent human technical review and Product Owner acceptance remain pending. This local test run does not accept EP01-S01.
- No Azure resource or deployment action occurred.
