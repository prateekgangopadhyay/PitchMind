# EP03-S02 decimal representability follow-up — 2026-10-13

## Scope

This child change addresses the independent review finding on PR #24 that JSON Schema numeric bounds did not ensure a number could be represented exactly by the .NET `decimal` fields used by telemetry DTOs. It changes contract validation only; it does not approve or map the candidate football feed.

## Change

- Added a reusable non-negative decimal schema with one exact-multiple and coefficient bound for each supported .NET decimal scale (0 through 28).
- Applied the rule to source clock values, event/tracking points and pitch dimensions. Pitch dimensions retain their existing 90–120 m by 45–90 m bounds.
- Added deserialization regressions for sub-scale, excess-fractional-precision and dimension values that .NET cannot represent, plus structural checks for definition placement, cross-schema references and all 29 branch bounds.
- Updated the telemetry contract and API docs to describe the wire constraint.

## Observed verification

- `dotnet test tests/PitchMind.Contracts.Tests/PitchMind.Contracts.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false`: build completed, including the changed test assembly. VSTest aborted before executing tests because its testhost could not resolve `Microsoft.CodeCoverage/17.14.1` from the local package cache. **No test pass count is claimed for this child head.**
- Python `Decimal` boundary check over the schema's 29 branches: 9 representative exact/invalid values behaved as expected, including `1e-29`, 28/29-place fractions, and values adjacent to the maximum coefficient at scale 1.
- Both schema files parsed as JSON; `git diff --check` passed.
- No standards-based JSON Schema validator was run. The boundary script evaluates the declared `multipleOf` and `maximum` rules; it is not a general JSON Schema implementation.

## Review and disposition

An independent specialist review confirmed the 29-branch bounds are mathematically consistent under exact-number JSON Schema semantics and caught an initial misplaced definition before publication. The definition was moved under the event schema's root `$defs`; the structural test now checks placement and the external frame-schema reference. A final independent review of that correction remains pending.

EP03-S02 remains unaccepted. The original acceptance criteria, Product Owner acceptance, a successful current-head test run, a general JSON Schema validation run, source-specific mapping/football review and independent GitHub approval remain outstanding. No Azure resource or deployment action occurred.
