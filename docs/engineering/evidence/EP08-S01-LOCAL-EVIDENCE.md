# EP08-S01 local verification evidence

**State:** Partial implementation; verification and acceptance remain pending. The local restack is based on EP06-S04 PR #43 head `902f91487586757dce7f8532c630d23337d09bb3`; existing PR #48 remains draft until the restacked candidate is published. Formal independent human review and Product Owner acceptance are pending.

## Delivered behavior

- `MatchIntelligenceContextBuilder` is a read-only Application tool backed by selected-time PostgreSQL projection, persisted insight, and evidence ports.
- The request has no caller-selected identity field. The required `IMatchIntelligenceScopeAuthorizer` must be provided by a host and bind access to its trusted identity context. The builder is not yet composed in an API or worker host.
- Projection and insight revisions must match. Evidence is filtered and checked against the selected clock, projection sequence, and the persisted insight's own as-of sequence.
- The packet propagates the persisted insight's `sourceVersion` and `provenance` separately from source/state hashes. Missing or oversized lineage metadata is refused.
- Supported truth classes map to bounded claim categories and category-specific use constraints. Evidence text is labeled untrusted; the tool has no model or canonical write port.
- Evidence item limits use UTF-8 bytes. The returned packet's `SerializedSizeBytes` matches the measured final serialized representation and the configured cap.

## Local commands executed

| Command | Result |
|---|---|
| `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` at code commit `959e37ff1b0482d95b25e2fbaa65a1273997da66` | 51 passed, 0 failed, 0 skipped |
| `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false` | Not completed on this restack; non-Application test projects lacked restored `project.assets.json` files (NETSDK1004) |
| `node scripts/validate_operating_model.mjs` | Passed: 12 epics, 59 stories, 28 Council skills, 3 design skills, 0 cycles, 2 approval gates |
| `git diff --check 902f91487586757dce7f8532c630d23337d09bb3` and `git diff --check` | Passed on the local restacked candidate and working changes |

The VSTest host required permission for a local ephemeral loopback socket. Application tests use in-memory fakes and do not establish PostgreSQL query behavior. No Foundry request, Azure resource mutation, or DEV deployment occurred.

## Independent static review and resolution

The read-only static review found: evidence could exceed the insight's own as-of sequence; `RequesterId` was caller-controlled; final serialized size was measured before setting its size field; and Application payload caps counted characters rather than UTF-8 bytes. Fixes now bind evidence reads to the insight sequence, remove requester identity from the request contract, calculate the final packet size, and enforce UTF-8 item bytes. Regression tests cover each finding. The reviewer did not execute tests; the listed test receipts were run separately after the fixes.

## Remaining gates

- No API/AgentWorker host composition or trusted identity binding exists yet; no model currently consumes the packet.
- Source-feed admission, coordinate/clock/player-metric semantics, canonical mappings, and football realism remain unverified; the supplied candidate feed is not represented as canonical by this story.
- The static review is not a formal human code-owner review. No Product Owner acceptance is recorded.
- PostgreSQL integration behavior, authenticated host binding, and Foundry prompt-injection/model evaluations remain unverified or belong to dependent stories. The EP05-S04 and EP06-S01 dependency PRs remain unaccepted.
- No Azure resources were provisioned or changed; no DEV deployment occurred.
