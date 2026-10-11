# EP08-S03 orchestration evidence

**Implementation branch:** `codex/ep08-s03-claim-gated-handoffs`
**Stack:** EP08-S04 claim-trust branch → EP08-S02 Foundry adapter branch
**Scope verdict:** CHANGES REQUIRED — local deterministic workflow evidence only; story acceptance remains open.

## Current-base restack verification (2026-10-12)

The four EP08-S03 commits were replayed onto exact EP08-S04 PR #51 head `612f844e025b371158d9700f393ab22223031048`; restack verification is recorded at local commit `142a976ecb0d24b2ac3b77f996c6c80f9349819e` on `codex/ep08-s03-restacked`. The latest base added required source-version/provenance fields to the context packet, so the AgentWorker test fixture was corrected before this run.

- `dotnet test tests/PitchMind.AgentWorker.Tests/PitchMind.AgentWorker.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false`: **27 passed, 0 failed, 0 skipped**.
- `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false`: **66 passed, 0 failed, 0 skipped**.
- `tests/PitchMind.Infrastructure.Tests` on an isolated UUID-named database in the existing local PostgreSQL service: **18 passed, 0 failed, 0 skipped**. The disposable database was dropped afterward. The repository harness could not be used directly because this checkout had no `docker/.env`; no existing application database was used.
- Council operating-model validation passed (12 epics, 59 stories, 28 Council skills, acyclic routing); design-skill registry self-test and `git diff --check` passed.

A solution-wide build was **not established**: the no-restore build found missing NuGet assets in unrelated API/Contracts/EventWorker projects, and the subsequent restore did not complete during this verification. The three affected story test projects compiled and passed. GitHub CI, live Foundry evaluation, authenticated runtime composition, human review and PO acceptance remain unverified. No Azure resources or deployments were touched.

## Requirement trace

| Acceptance requirement | Implemented evidence | Current result / limitation |
|---|---|---|
| Distinct roles with traced inputs/outputs and handoffs | `ExplanationAgentOrchestrator` emits Evidence Curator, Football Narrator, Claim Verifier stages with packet fingerprint, evidence IDs, status, duration and provider token counts. | Fake-port tests assert all three role transitions and the same selected-context fingerprint. No production trace sink or request consumer is composed. |
| Read-only canonical context | Workflow requests context via `IMatchIntelligenceReadOnlyTool`; curator rejects unauthorized context failures and validates match, insight, revision, selected time, sequence watermark, hashes, evidence IDs/types/time/sequence/trust marker/JSON/size. | Orchestrator unit tests use a fake port. The separate PostgreSQL integration suite exercised the repository readers against empty, isolated disposable databases; no canonical match data or live authorization provider was exercised. |
| Verifier rejection never becomes a displayed fact | Workflow result carries `VerifiedExplanation` only; candidate prose and candidate-supplied citation IDs are not copied into traces or workflow results. Context evidence IDs are traced only after packet validation; verifier trace inputs use those validated packet IDs, and only verified explanation IDs can be trace outputs. EP08-S04 gates final output. | Test injects a candidate citation to `future-event`; rejection result serialization excludes both the candidate text and citation ID. A malformed context packet's evidence IDs are also excluded. S04 is evidence-only reformulation, not semantic football verification. |
| Meaningful fan and analyst handoff | Same evidence IDs/context are retained while the deterministic verifier applies the audience-specific safe form. | Two audience tests pass; there is no user study or real-model audience-fit evaluation. |
| Single-agent baseline comparison | Test compares one raw single-narrator candidate with the identical candidate passed through curation and verification. Each path invokes the candidate generator once. | Demonstrates a deterministic safe-output gate with no additional model call. It does not establish improved real-model accuracy or user preference. |
| Bounded cost and failure | Default cap: 32 KiB context, 12 evidence rows, 256 output tokens, 20-second workflow timeout, 4 concurrent model calls, 3 handoffs, no retry. Trace captures provider-reported token use without raw content. | Configuration/unit paths only. No provider metering, regional price, real latency, or cost-per-explanation measurement. |

## Focused tests executed

Command: `dotnet test tests/PitchMind.AgentWorker.Tests/PitchMind.AgentWorker.Tests.csproj -c Release --filter 'FullyQualifiedName~ExplanationAgentOrchestratorTests|FullyQualifiedName~FoundryExplanationAdapterTests' -m:1 --disable-build-servers -p:NuGetAudit=false`

Result: **27 passed, 0 failed, 0 skipped** (2026-10-10, local test runner). The suite covers the Foundry adapter tests and orchestrator cases for:

- role order, shared context fingerprint, evidence-ID handoffs, and verified-only output;
- unknown evidence citation rejection and removal of candidate prose;
- future evidence rejection before the narrator call;
- UTF-8 byte-limit rejection for a multibyte payload before the narrator call;
- denied read-context failure before the narrator call;
- context-tool and model-provider exception handling without leaking exception details;
- match/revision/time mismatch rejection;
- model-unavailable and workflow-timeout outcomes without retries;
- caller cancellation during context retrieval;
- fan/analyst presentation with unchanged evidence IDs; and
- same-candidate one-call single-agent baseline comparison.

Focused `MatchIntelligenceContextTests` and `ExplanationCandidateVerifierTests` passed **24/24**. Full regression without a configured database: `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false` exited 0 with **135 passed, 0 failed, 16 skipped**; all skips were PostgreSQL integration cases. The infrastructure test `PointInTimeProjectionIsMissingForUnknownMatchAndAllowsAddedTime` now asserts the reader allows 5,761 seconds and still rejects negative time. After the change, all **16/16 PostgreSQL integration tests passed** when run individually against unique disposable local databases; every database was dropped after its test. `dotnet build PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false --no-restore` exited 0 with zero warnings and errors. The Council operating-model validator, dependency DAG self-test, and `git diff --check` passed.

The orchestrator tests use fake context/model ports and do not call Foundry or Azure. The isolated PostgreSQL integration tests call the local PostgreSQL service but use empty test databases; no canonical match dataset was ingested. A real-model adversarial evaluation, authenticated worker consumption, security red-team, measured FinOps and PO acceptance remain unverified.

The selected-time contract preserves non-negative source elapsed seconds without an internal 5,400-second ceiling. Regression coverage exercises 5,761 seconds through the context builder, Foundry adapter, verifier, orchestrator, and PostgreSQL projection reader. The requester identity is not caller-selectable in the request DTO; authorization is delegated to the mandatory host-bound `IMatchIntelligenceScopeAuthorizer`. The candidate source has not been ingested or approved as canonical.

The independent exact-base review identified that unverified context/candidate evidence IDs could appear in rejection traces. The workflow now waits until context validation before tracing packet IDs, omits narrator candidate IDs, and uses validated packet IDs as verifier trace inputs. Focused AgentWorker tests passed after this correction. Independent re-review of the corrected head is pending.

## Design and architecture changes

- Added the explicit EP08-S04 dependency to EP08-S03 routing because the story's acceptance gate requires verifier rejection to gate output.
- Added [ADR-012](../../adr/ADR-012-bounded-agent-handoffs.md) and updated [C4 Level 3](../../architecture/03-components.md), the API implementation boundary, and this story's LLD.
- No public API, React contract, canonical data schema, database migration or Domain behavior changed.
- No Azure resources, model deployments or environments were accessed or changed. The hosted AgentWorker remains idle; the orchestrator is not DI-registered until a real authorized context-tool implementation and request consumer exist.

## Remaining blockers and owners

- **AgentWorker/Application integration:** provide a real `IMatchIntelligenceReadOnlyTool` implementation and authenticated request/message consumer; owner: backend/AI architecture.
- **Semantic football validation:** validate sourced event meaning beyond evidence IDs and truth-class reformulation; owner: football analytics + application security.
- **Foundry evaluation and FinOps:** compare real single-agent and staged responses on a governed evaluation set; record model/version, audience fit, false claims, latency and cost; owners: AI architecture + FinOps + QA.
- **Independent review and acceptance:** technical review and Product Owner acceptance remain pending, as requested; no reviewer request was sent.
