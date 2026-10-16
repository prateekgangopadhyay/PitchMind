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


## Follow-up Architecture suite on code-equivalent checkout — 2026-10-16

The isolated docs checkout's first full Architecture run remained 22/26 because four test-time API project evaluations lacked `project.assets.json`. A follow-up ran on checkout commit `75106280353d5b15885b863072d467dbcd987868`, whose `src/`, `tests/`, and `pipelines/` trees compare byte-for-byte identical to implementation source `c8494b918d08167ab721b8229f4ce7db9abd06c8`. Its existing restore assets matched the unchanged API project file. With local loopback execution permission, `dotnet test tests/PitchMind.Architecture.Tests/PitchMind.Architecture.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false --no-restore` built and passed **26/26, 0 failed, 0 skipped**. The initial asset-missing run and stalled isolated restore remain recorded above; the offline restore attempt did not complete. This is Architecture-suite evidence for the unchanged main code/test/pipeline tree, not a full solution result or a hosted pipeline run.

## Restacked current-main verification — 2026-10-16

PR #295 was restacked onto current main `5f6f3367743bfac754d9b6ba7144091f1baed9a`. The tested documentation-descendant commit was `75108eedaab20d9a5298ed9ab64ebab001e936c9`; its `src/`, `tests/`, and `pipelines/` trees compare byte-for-byte with current main.

- `dotnet restore PitchMind.slnx -p:NuGetAudit=false --disable-parallel` — passed for all projects. A preceding sandboxed attempt was silent for about 90 seconds and was cancelled; the authorized local-network retry completed successfully.
- `dotnet test tests/PitchMind.Architecture.Tests/PitchMind.Architecture.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` — **30 passed, 0 failed, 0 skipped**.
- The selected `DeliberateGateFailureProbe` passed **1/1** with `PITCHMIND_GATE_FAILURE_PROBE` unset. With the variable set to `1`, it failed **1/1** and the test command returned exit code **1**, as intended.
- PyYAML parsing and focused assertions passed for main + `codex/*` PR targets, `runGateFailureProbe=false`, `enableDevDelivery=false`, `succeeded()` gating, the exact failure-probe filter/environment, and absence of a `continueOnError` override.
- `node scripts/validate_operating_model.mjs`, `node scripts/validate_operating_model.mjs --self-test`, and `git diff --check` passed.

This is current-main LOCAL evidence only. No Azure DevOps pipeline was registered or queued; hosted execution, test-result publishing, GitHub required-check/branch-policy binding, hosted failure-to-block behavior, independent human review, and Product Owner acceptance remain unverified. No Azure command, resource change, or deployment occurred. EP01-S03 remains unaccepted.

After the checks, main advanced to `60cad153ee0031262a94378df199068ad217a031` with a Product Owner ledger documentation update. The draft branch was rebased onto that current head; `src/`, `tests/`, and `pipelines/` remain byte-identical to the tested tree. The restack changes no implementation/test source and the above test results remain applicable to the current code tree.
