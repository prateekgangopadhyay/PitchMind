# EP08-S04 candidate lineage guard — local evidence

**Scope:** A deterministic, non-publishing lineage guard in the Application layer. This is an implementation slice, not EP08-S04 acceptance.

## Behavior changed

- Candidate metadata must match a SHA-256 fingerprint of the exact serialized `MatchIntelligenceContextPacket`, plus match, canonical/projection revisions, selected time, sequence watermark, source version/provenance/hashes and context policy.
- The checker recalculates actual serialized packet size instead of trusting the reported size. Packet evidence IDs must be unique, bounded object/array JSON, event/frame evidence, period 1–5, and stay within the packet's canonical revision, projection as-of clock and sequence watermark. Selected time is capped at 5400 seconds.
- Candidate output and claims are bounded. Every claim requires an evidence ID in the packet, and top-level narrative text must equal the individually cited claim text.
- Result type is `CandidateExplanationLineageCheck`; `CanBePublished` always returns false. This slice does not establish semantic entailment.

## Verification

Base: `2f3dd4e65a3a56a0c3a80ae8c21bd1a85701e9a1` (EP08-S02 merge). Implementation head: `6244a0111debfacb84f3c487f6741b3084d5e8d3`. SDK: .NET 10.0.401.

| Command | Result |
|---|---|
| `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | 110 passed, 0 failed, 0 skipped. |
| `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | 356 passed, 0 failed, 24 skipped. All skips are opt-in PostgreSQL integration tests; no test connection was configured. |
| `dotnet build PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | Succeeded, 0 warnings, 0 errors. |
| `node scripts/validate_operating_model.mjs --self-test` | Passed. |
| `git diff --check` | Passed. |

The adversarial test intentionally supplies prompt-injection text and a fabricated score in evidence/candidate text. The lineage guard reports only that packet lineage and references are bounded; `CanBePublished` remains false. This is evidence that the component is not a semantic verifier.

## Unverified / blockers

- Semantic entailment between text and cited event/frame, correct player/team attribution, score/stat checks, causal certainty and coach-intent rejection are not implemented or verified.
- No API/AgentWorker consumer, UI, persistence, Foundry call, PostgreSQL runtime integration, hosted CI, Azure identity/RBAC, model evaluation, cost/latency, or Azure deployment is included.
- Independent technical review and Product Owner acceptance remain pending; routing status remains `unverified`.
