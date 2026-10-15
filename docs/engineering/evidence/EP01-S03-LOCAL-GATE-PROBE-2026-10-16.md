# EP01-S03 current-main local gate probe — 2026-10-16

## Scope

This is a LOCAL verification of the Azure Pipelines failure-probe mechanics, not an Azure DevOps run. The implementation source is current main `c8494b918d08167ab721b8229f4ce7db9abd06c8`; the test checkout was docs-only descendant `8eb655af339f25468dc9e4006fc80815a2493a09`, with application, pipeline, and test source matching that main commit.

## Observed checks

- `dotnet test tests/PitchMind.Architecture.Tests/PitchMind.Architecture.Tests.csproj -c Release --no-build --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false --filter 'FullyQualifiedName~DeliberateGateFailureProbe'`: **1 passed** with `PITCHMIND_GATE_FAILURE_PROBE` unset.
- The same selected test with `PITCHMIND_GATE_FAILURE_PROBE=1`: **1 failed**, process exit code **1** as intended by the gate probe.
- The full Architecture suite built and ran **26 tests: 22 passed, 4 failed, 0 skipped**. All four failures occurred while test code tried to evaluate `src/PitchMind.Api/PitchMind.Api.csproj`; the isolated checkout had no `src/PitchMind.Api/obj/project.assets.json` (`NETSDK1004`). This is not a passing Architecture suite.
- `dotnet restore PitchMind.slnx --locked-mode -p:NuGetAudit=false --ignore-failed-sources` produced no progress for over a minute and was interrupted. No full-solution restore/build/test result is claimed.
- `node scripts/validate_operating_model.mjs --self-test` passed.
- PyYAML parsed `pipelines/azure-pipelines.yml`; a focused assertion confirmed PR targets include `main` and `codex/*`, `runGateFailureProbe` defaults to false, and the probe job is conditioned on explicit opt-in.
- `git diff --check` passed for the evidence update.

## Limits

No Azure DevOps pipeline was registered or queued. Hosted pipeline execution, test-result publishing, required-check/branch-policy binding, and hosted failure-to-block behavior remain unverified. No Azure command or resource operation occurred. EP01-S03 remains unaccepted pending hosted evidence, exact-head independent review, and Product Owner acceptance.
