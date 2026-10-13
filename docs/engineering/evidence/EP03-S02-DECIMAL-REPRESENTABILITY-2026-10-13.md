# EP03-S02 decimal representability follow-up — 2026-10-13

## Scope

This child change addresses the independent review finding on PR #24 that JSON Schema numeric bounds did not ensure a number could be represented exactly by the .NET `decimal` fields used by telemetry DTOs. It changes contract validation only; it does not approve or map the candidate football feed.

## Change

- Added a reusable non-negative decimal schema with one exact-multiple and coefficient bound for each supported .NET decimal scale (0 through 28).
- Applied the rule to source clock values, event/tracking points and pitch dimensions. Pitch dimensions retain their existing 90–120 m by 45–90 m bounds.
- Added `ExactDecimalJsonConverter` attributes to the five decimal DTO members so default `System.Text.Json` deserialization rejects loss or overflow rather than silently rounding values after parsing.
- Added exact scale/coefficient, under-scale, excessive precision, overflow and extreme exponent regressions, plus structural checks for definition placement, cross-schema references and all 29 branch bounds.
- Updated the telemetry contract and API docs to describe the wire constraint.

## Observed verification

- Final code revision: `c62486f00a1a0d3f8afd303afe12031cfac737d8`.
- `dotnet test tests/PitchMind.Contracts.Tests/PitchMind.Contracts.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false`: **46 passed, 0 failed, 0 skipped**.
- `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false`: **81 passed, 0 failed, 5 skipped**. All five skips were PostgreSQL integration tests because `PITCHMIND_TEST_POSTGRES` was not configured.
- Both schema files parsed as JSON; the Council dependency-DAG self-test and `git diff --check` passed.
- No standards-based JSON Schema validator was available or run; schema structure and runtime deserialization were tested, but standalone JSON Schema conformance is still unverified.

## Review and disposition

An independent specialist review confirmed the 29-branch bounds are mathematically consistent under exact-number JSON Schema semantics and caught an initial misplaced definition before publication. A second review found extreme exponent edge cases in the runtime converter; they were fixed and the final exact-code diff was reviewed again with no remaining concrete defect identified.

EP03-S02 remains unaccepted. Product Owner acceptance, a general JSON Schema validation run, source-specific mapping/football review and independent GitHub approval remain outstanding. No Azure resource or deployment action occurred.
