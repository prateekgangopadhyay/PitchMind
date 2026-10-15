# EP08-S02 local verification evidence

**State:** Implementation slice locally verified on a current-main feature branch. Independent exact-head review, hosted CI and Product Owner acceptance remain pending; routing status is still `unverified`.

## Delivered behavior

- Application exposes `IExplanationCandidateGenerator` and an explicitly untrusted candidate result type, separate from the public `ValidatedExplanation` contract.
- AgentWorker uses the Foundry OpenAI-compatible chat-completions v1 endpoint with `ManagedIdentityCredential` only in the Azure runtime profile. Endpoint validation restricts HTTPS hosts to known Azure AI suffixes, and the HTTP handler disables redirects before credentials are attached.
- Requests, responses, output tokens, wall time, concurrency, context packet and individual payloads are bounded. The adapter requests strict structured JSON and rejects malformed candidates or evidence IDs absent from the selected context packet.
- Packet validation rejects invalid source/state hashes (including uppercase or non-hex values), invalid selected-time bounds, non-positive projection revisions, evidence source revisions newer than the packet revision, negative projection watermarks and evidence sequences above `ThroughSequence`. EP08-S01 must bound evidence by the persisted insight's own as-of sequence before packet construction.
- Telemetry contains configured deployment, duration, token usage and trace ID only. It excludes question, evidence, model response and token content.
- Candidate output is not public, persisted or accepted as truth. AgentWorker has no request/message consumer, and EP08-S04 semantic claim verification remains required.
- The AgentWorker package lock now includes the direct Azure.Identity dependency and its resolved transitive graph. The Application candidate contract uses the `long` canonical revision type from current main and carries projection revision, `ThroughSequence`, source version/provenance, source/state hashes and context policy version into the internal candidate result for later verifier binding.

## Local commands executed

Base revision: `4216d9c1af8a87530b6853e300e04c79b01ff667`; implementation code head: `d278f2be4043b6253023c7fecd3aa3b82968f187`; feature branch: `codex/ep08-s02-foundry-main-restack`; SDK: .NET 10.0.401. All results below were executed in the isolated local clone after rebasing to this exact main head. Later edits in the evidence/register changed documentation only.

| Command | Result |
|---|---|
| `dotnet restore tests/PitchMind.AgentWorker.Tests/PitchMind.AgentWorker.Tests.csproj --force-evaluate --ignore-failed-sources --disable-parallel -p:NuGetAudit=false` | Restored local cached packages; updated the AgentWorker lockfile for Azure.Identity. |
| `dotnet restore tests/PitchMind.AgentWorker.Tests/PitchMind.AgentWorker.Tests.csproj --locked-mode --ignore-failed-sources --disable-parallel -p:NuGetAudit=false` | Passed after lock update. |
| `dotnet test tests/PitchMind.AgentWorker.Tests/PitchMind.AgentWorker.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | 11 passed, 0 failed, 0 skipped. |
| `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | 93 passed, 0 failed, 0 skipped. |
| `dotnet build PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | Passed, 0 warnings, 0 errors. |
| `dotnet test tests/PitchMind.Architecture.Tests/PitchMind.Architecture.Tests.csproj -c Release --no-build --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | 24 passed, 0 failed, 0 skipped. |
| `dotnet test PitchMind.slnx -c Release --no-build --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | 339 passed, 0 failed, 24 skipped. Skips are opt-in PostgreSQL integration tests; no test connection was configured. |
| `node scripts/validate_operating_model.mjs --self-test` | Passed; DAG and safety-gate self-tests. |
| `git diff --check` | Passed. |

The first focused AgentWorker test execution found stale test fixture arguments after the current EP08-S01 packet added source-revision fields; those were corrected. A follow-up added a negative test for invalid projection/source revision lineage and uppercase hashes before token acquisition; all 11 adapter tests passed. The first architecture/full-suite run then found copied ignored restore assets had overwritten the AgentWorker assets in this disposable clone. After re-restoring the exact locked AgentWorker graph, the Architecture suite passed 24/24 and the complete solution suite passed 338/0/24 skipped. The test runner required local socket permission to bind its VSTest channel. NuGet audit was disabled and is not evidenced.

## Review and remaining gates

- No independent reviewer has reviewed the current-main restack. Existing review notes for earlier stacked heads are historical only and do not count as approval of this head.
- The Foundry adapter has not been invoked against a live deployment; model correctness, refusal behavior, latency and cost remain unmeasured.
- Managed identity authentication and RBAC are not verified against Azure.
- AgentWorker remains idle without a consumer; no user-facing API/UI integration or deterministic outage fallback is supplied by this story alone.
- EP08-S04 semantic claim verification and Product Owner acceptance remain pending. No Azure resources were provisioned or changed; no DEV deployment occurred.
