# EP08-S01 local verification evidence

**State:** Partial implementation only; the routing register remains `unverified`, and Product Owner acceptance is pending. This evidence concerns an Application-level read-only packet builder and PostgreSQL reader bounds. It does not establish an integrated AI or cloud path.

**Code commit:** `677f312f2da2ba0263524175fde19016186ba731`, based on current `main` commit `6178d656283fb375711e8836fd09e93e46877989`.

## Observed implementation

- `MatchIntelligenceContextBuilder` requires `IMatchIntelligenceScopeAuthorizer`; the request has no actor identity field.
- The selected packet records distinct `canonicalRevision` and `projectionRevision`, selected time, as-of clock/sequence, score, detector version, source version/provenance/hash, state hash and evidence source revisions.
- The builder requires the insight canonical/projection revision pair and source hash to match the selected projection. It refuses unsupported truth classes, absent source lineage, malformed evidence, future or unresolved references, evidence over configured limits and oversized serialized packets.
- Stored `observed`, `derived`, `evidence-supported-pattern`, `supported-interpretation`, `hypothesis` and `simulated` classes remain distinct packet categories; unknown classes are rejected.
- Evidence text and source metadata are explicitly labeled untrusted. The Application use case has no canonical write or model-generation dependency.
- `PostgresMatchInsightReader` rejects insight references above the configured evidence count and limits returned evidence rows and UTF-8 payload bytes in SQL. Its normal page path remains unchanged.
- No API endpoint, AgentWorker host registration, Foundry call or Azure adapter is added. The required authorizer still needs a real trusted host implementation before composition.

## Local command evidence

| Command | Observed result |
|---|---|
| `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | **86 passed, 0 failed, 0 skipped** (local test runner) |
| `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | **315 passed, 0 failed, 24 skipped** across API (12), Application (86), Architecture (22), Contracts (110), Domain (67), and Infrastructure (18 passed / 24 skipped). All 24 skips are PostgreSQL-dependent integration tests. |
| `node scripts/validate_operating_model.mjs` | Passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, 33 discoverable skills, no dependency cycles, 2 approval gates. |
| `git diff --check` | Passed after documentation/code edits. |

The focused test run covers positive packet construction, scope denial before reads, canonical/projection revision mismatches, source-hash mismatch, truth-class preservation and unknown-class rejection, missing lineage, future sequence/time, malformed/oversized payloads, UTF-8 limits and final serialized packet limit. Tests use isolated in-memory test doubles; they do not exercise PostgreSQL SQL or host identity binding. The full-suite Infrastructure tests compiled the changed PostgreSQL reader but skipped the database integration cases.

## Not verified / blockers to acceptance

- PostgreSQL query execution and PostgreSQL-backed packet retrieval; all 24 integration tests requiring the database were skipped because the local Docker socket was not accessible.
- API/AgentWorker composition, authenticated identity binding, Foundry interaction, prompt-injection/adversarial claim verification, latency/token/cost limits, and deterministic model-outage response.
- EP05-S04 football/PO acceptance and EP06-S01 Product Owner/hosted integration acceptance, despite their implementation code being on current main.
- Candidate Manu–Liverpool source admission, rights/football quality, coordinate and clock semantics; this story slice does not use that candidate feed.
- Independent technical review of the restacked code head and Product Owner acceptance.

No Azure resource, pipeline, deployment, or GitHub Actions change was performed.
