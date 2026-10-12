# EP08-S03 prompt-injection regression evidence

**Scope:** test-only trust-boundary regression on the child branch `codex/ep08-s03-prompt-injection-guard`, based on PR #138 head `b57f8d60310da6b5182682cbe904ab128a2473e1` (itself stacked on PR #56 head `16533ccca4b0bed26a55fc631722c814efa73570`).

**Code commit tested:** `63fc58a906c21685cc2c13747765c9aca26a9f79`.

## Acceptance slice

- The Foundry adapter regression places an instruction to ignore policy, invent a 99-0 score, and reveal the system prompt inside a JSON evidence payload. It verifies the fixed system instruction contains the untrusted-data policy and the injected text remains inside serialized user evidence data.
- The handoff regression asserts the generator receives that adversarial evidence and supplies a separate untrusted candidate containing the same invented score and intent claims. It verifies the orchestrator returns only the verifier's evidence-only reformulation, with neither claim exposed. The stub does not establish that a model followed the injection.

These tests prove message-boundary serialization and deterministic output gating for the test fixtures. They do not prove that a live model resists an injection, establish semantic football accuracy, or substitute for an adversarial model evaluation.

## Commands and results

- Focused: `dotnet test tests/PitchMind.AgentWorker.Tests/PitchMind.AgentWorker.Tests.csproj -c Release --filter 'FullyQualifiedName~KeepsEvidencePromptInjectionInUserDataUnderFixedSystemPolicy|FullyQualifiedName~DoesNotReturnUnsafeCandidatePairedWithPromptInjectedEvidence' -m:1 --disable-build-servers -p:NuGetAudit=false` — **2 passed, 0 failed, 0 skipped**.
- Full AgentWorker suite: `dotnet test tests/PitchMind.AgentWorker.Tests/PitchMind.AgentWorker.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` — **29 passed, 0 failed, 0 skipped**.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, zero dependency cycles, two approval gates.
- `git diff --check` — passed.

VSTest initially aborted before executing tests because the sandbox denied its loopback communication socket. The exact commands above were rerun with loopback test-host permission and completed successfully. No Azure or Foundry resources were accessed; the Foundry adapter test uses a stub HTTP handler and token provider.

**Remaining:** live Foundry prompt-injection evaluation, authenticated production composition, full solution/runtime certification, independent human review, and Product Owner acceptance are not established. EP08-S03 remains unverified and unaccepted; PRs remain draft.
