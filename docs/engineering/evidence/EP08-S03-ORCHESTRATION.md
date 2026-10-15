# EP08-S03 local orchestration evidence

**Status:** Implementation slice only; routing remains `unverified`, Product Owner acceptance pending.

## Implemented boundary

- `ExplanationAgentOrchestrator` validates one pre-authorized read-only context packet, asks one candidate generator for one audience-aware candidate, and returns only `ExplanationCandidateVerifier` output.
- Bounded options cover serialized packet bytes, evidence count and UTF-8 evidence item bytes, output tokens, wall-clock timeout, and immediate concurrency refusal.
- Cancellation, context denial/unavailability, malformed/future context, narrator failure, verifier rejection and successful handoffs have typed statuses.
- Trace metadata omits raw candidate text and all candidate-supplied citation IDs. Context evidence IDs enter traces only after packet validation; verifier-approved evidence IDs are the only generated evidence IDs emitted.
- No canonical write, API route, message consumer, live Foundry call, or authenticated host composition was added.

## Local verification

Tested code revision: `d1b2db98d50420d371f2a0419851050cff9cb1da` (based on `main` `052dd6976c4c65cf535a70b10b236d393fdd2e77`).

Commands:

```text
dotnet test tests/PitchMind.AgentWorker.Tests/PitchMind.AgentWorker.Tests.csproj -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false
dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false
node scripts/validate_operating_model.mjs --self-test
git diff --check
```

Observed results on the exact committed source: AgentWorker **27 passed, 0 failed, 0 skipped**; Application **166 passed, 0 failed, 0 skipped**; operating-model validator/self-test passed (12 epics, 59 stories, 28 Council skills, no dependency cycles, two approval gates); `git diff --check` passed. The .NET runs used the local .NET 10 test host. A sandboxed attempt aborted before tests because VSTest could not bind its loopback socket; the successful reruns were executed with local testhost socket access. `PitchMind.AgentWorker` and its Application/Domain references built as part of the AgentWorker run.

Coverage includes three-role handoff and verifier-only output, a single-generator baseline comparison, invented unsupported claim rejection without candidate-text/citation leakage, invalid/future/malformed/oversized UTF-8 evidence refusal before narration, caller cancellation, workflow timeout, context denial/unavailability and audience formatting with stable evidence.

## Remaining evidence and risks

- No independent human technical/security or football review has been submitted on the final restacked head.
- No hosted Azure DevOps run or Product Owner acceptance is recorded.
- Orchestrator is not registered in Worker; no authenticated host-to-context-tool composition or live Foundry call is executed.
- The single-generator comparison demonstrates the deterministic publication gate's safety effect, not a measured accuracy benefit from multiple model agents.
- Current request context uses 0..5400 seconds; added-time values above 5400 require a coordinated selected-time contract update.
- No semantic football-source certification, latency/cost evaluation, PostgreSQL execution or Azure deployment is claimed.
