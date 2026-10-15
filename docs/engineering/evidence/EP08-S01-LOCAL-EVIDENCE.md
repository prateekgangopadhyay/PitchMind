# EP08-S01 local verification evidence

**State:** Partial implementation only; the routing register remains `unverified`, and Product Owner acceptance is pending. This evidence concerns an Application-level read-only packet builder and PostgreSQL reader bounds. It does not establish an integrated AI or cloud path.

**Code commit:** `c6e5bcb12ac6ca5a7e0cd4f656393f5f45ec03d5` (`fix(EP08-S01): bound context summaries and unavailable reads`), based on PR #48 head `12813d8350965b5ca91e75daff02eba3b3af14cf` and current `main` commit `6178d656283fb375711e8836fd09e93e46877989`.

## Observed implementation

- `MatchIntelligenceContextBuilder` requires `IMatchIntelligenceScopeAuthorizer`; the request has no actor identity field.
- The selected packet records distinct `canonicalRevision` and `projectionRevision`, selected time, as-of clock/sequence, score, detector version, source version/provenance/hash, state hash and evidence source revisions.
- The builder requires the insight canonical/projection revision pair and source hash to match the selected projection. It refuses unsupported truth classes, absent source lineage, malformed evidence, future or unresolved references, evidence over configured limits and oversized serialized packets.
- Stored `observed`, `derived`, `evidence-supported-pattern`, `supported-interpretation`, `hypothesis` and `simulated` classes remain distinct packet categories; unknown classes are rejected.
- Evidence text and source metadata are explicitly labeled untrusted. The Application use case has no canonical write or model-generation dependency.
- `PostgresMatchInsightReader` filters summaries larger than 6 KiB UTF-8 with `octet_length` in SQL, before returning/materializing the summary text; the Application builder enforces the same byte limit. It rejects insight references above the configured evidence count and limits returned evidence rows and UTF-8 payload bytes in SQL.
- The context builder maps unavailable projection, insight and evidence ports to `MatchStateUnavailable`, `ClaimUnavailable` and `EvidenceUnavailable` structured refusals respectively. It catches only `CanonicalReadUnavailableException`; cancellation continues to propagate.
- No API endpoint, AgentWorker host registration, Foundry call or Azure adapter is added. The required authorizer still needs a real trusted host implementation before composition.

## Local command evidence

| Command | Observed result |
|---|---|
| Focused Application tests after review fixes: `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | **93 passed, 0 failed, 0 skipped** (local test runner) |
| `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | **315 passed, 0 failed, 24 skipped** on the pre-review-fix implementation head, across API (12), Application (86), Architecture (22), Contracts (110), Domain (67), and Infrastructure (18 passed / 24 skipped). All 24 skips were PostgreSQL-dependent integration tests; this result does not verify the current review-fix head. |
| Build of `PitchMind.Infrastructure.Tests` after review fixes | Passed compilation. The focused PostgreSQL test containing the oversized-summary assertion was skipped because `PITCHMIND_TEST_POSTGRES` is unset; PostgreSQL SQL behavior is unverified locally. |
| `node scripts/validate_operating_model.mjs` | Passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, 33 discoverable skills, no dependency cycles, 2 approval gates. |
| `git diff --check` | Passed after documentation/code edits. |

The focused Application test run covers positive packet construction, scope denial before reads, canonical/projection revision mismatches, source-hash mismatch, truth-class preservation and unknown-class rejection, missing lineage, future sequence/time, malformed/oversized payloads, UTF-8 summary/evidence limits, structured refusals for each unavailable read stage, cancellation propagation from each stage, and the final serialized packet limit. The Infrastructure integration test additionally checks that an oversized persisted summary is absent from both detail and page reader results when PostgreSQL is configured; because `PITCHMIND_TEST_POSTGRES` was unset, that database test was skipped and SQL behavior remains unverified locally.

## Not verified / blockers to acceptance

- PostgreSQL query execution and PostgreSQL-backed packet retrieval; all 24 integration tests requiring the database were skipped because the local Docker socket was not accessible.
- API/AgentWorker composition, authenticated identity binding, Foundry interaction, prompt-injection/adversarial claim verification, latency/token/cost limits, and deterministic model-outage response.
- EP05-S04 football/PO acceptance and EP06-S01 Product Owner/hosted integration acceptance, despite their implementation code being on current main.
- Candidate Manu–Liverpool source admission, rights/football quality, coordinate and clock semantics; this story slice does not use that candidate feed.
- Independent technical review of the restacked code head and Product Owner acceptance.

No Azure resource, pipeline, deployment, or GitHub Actions change was performed.
