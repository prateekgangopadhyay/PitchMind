# EP08-S04 local verification evidence

**State:** Local implementation and refreshed test evidence recorded on the EP08-S04 branch stacked on the current EP08-S02 PR #50 head. PR #51 remains draft/pending; Product Owner acceptance is not recorded.

## Delivered behavior

- `ExplanationCandidateVerifier` matches candidate match, canonical revision and selected time to the bounded context packet, checks citation IDs against packet evidence, and revalidates projection time, evidence clocks and evidence sequence watermark.
- The verifier never copies model-authored prose or claim text. Its response is deterministic wording from the packet's claim category, selected time and evidence kind/count, with explicit no-causation/no-intent limitations; exact evidence IDs remain in structured citations.
- Provider-supplied model and request-trace identifiers are treated as untrusted candidate metadata and are not included in the verified result.
- This blocks fabricated prose from reaching a future caller but does not semantically prove the generated claim. No public API/UI or AgentWorker request consumer currently invokes the verifier.

## Local commands executed

On the restacked local source, these commands completed:

| Command | Result |
|---|---|
| `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | 64 passed, 0 failed, 0 skipped |
| `dotnet test tests/PitchMind.AgentWorker.Tests/PitchMind.AgentWorker.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | 10 passed, 0 failed, 0 skipped |
| `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | 168 passed, 0 failed, 18 skipped (all PostgreSQL integration tests) |
| `dotnet build PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | Passed, 0 warnings, 0 errors |
| `node scripts/validate_operating_model.mjs` | Passed: 12 epics, 59 stories, 28 Council skills, 0 cycles, 2 approval gates |
| `git diff --check HEAD` | Passed |

The 18 PostgreSQL integration tests were skipped because no local database was configured. Tests used local code and fake providers; no live Foundry call, Azure resource mutation or DEV deployment occurred. VSTest required permission to bind its ephemeral local loopback communication socket. The initial restack exposed that the S04 packet fixture was missing S01 source-version/provenance fields; the fixture was corrected before the passing runs.

## Independent review

The first read-only review found that the verifier did not independently recheck packet time/sequence bounds and returned provider-controlled model/trace identifiers as if they were safe provenance. The verifier now rejects evidence after `SelectedTimeSeconds` or `ThroughSequence`, and the verified result omits those provider fields. Regression tests cover evidence clock/sequence, projection time, negative sequence watermark, and provider metadata. Follow-up review rechecked the fixes and found no remaining issue; the review agent did not execute tests.

## Remaining gates

- The verifier reformulates only safe packet metadata; semantic/source validation of model claim text remains unimplemented.
- No API/UI or AgentWorker consumer calls this verifier, and no live Foundry, Entra identity or RBAC validation was run.
- PostgreSQL tests, CI execution, formal technical review and Product Owner acceptance remain pending.
- No Azure resources were provisioned or changed; no DEV deployment occurred.
