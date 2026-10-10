# ADR-012 — Bounded, claim-gated explanation handoffs

**Status:** Accepted for implementation boundary; authenticated request composition, live model evaluation and Product Owner acceptance remain pending. 2026-10-10.

## Context

EP08-S02 provides a server-side generator for untrusted Foundry candidates. EP08-S04 provides a deterministic verifier that rejects invalid candidate metadata or evidence references and discards model-authored prose. EP08-S03 requires meaningful role handoffs, a controlled single-agent baseline, and a fail-closed publication gate. Repeating the same model call across multiple LLM agents would multiply latency and cost without evidence of benefit.

## Decision

`ExplanationAgentOrchestrator` composes three bounded roles around one authorized `IMatchIntelligenceReadOnlyTool` request:

1. **Evidence Curator** requests the selected match/insight/time/revision packet through the read-only Application port, then validates match identity, revision, selected time, projection cutoff, sequence watermark, source/state hashes, evidence identity/type/time/sequence, trust-boundary marker, JSON payload shape and size. It does not reorder, synthesize or mutate evidence.
2. **Football Narrator** makes at most one Foundry candidate-generator call with the same immutable packet, requested audience, and bounded question. Candidate text remains untrusted.
3. **Claim Verifier** delegates to `ExplanationCandidateVerifier`. Only its evidence-only `VerifiedExplanation` is returned; candidate prose is never copied to the workflow result.

The workflow caps packet size at 32 KiB, evidence count at 12, an output budget at 256 tokens by default, concurrent workflows at four, total duration at 20 seconds by default, and handoffs at three. No retries occur. It emits trace metadata (workflow/version, selected context, evidence IDs, context fingerprint, stage outcome/duration and provider-reported token counts) without raw evidence payloads or generated text. Invalid context, denied access, timeout, cancellation, model failure or rejected candidate returns no explanation.

This design adds a deterministic authorization/context check and a safe publication gate without increasing model calls versus the one-agent baseline. The local fake-candidate comparison demonstrates this structural safety delta only; it does not prove real-model accuracy or audience preference. EP08-S04 is a route dependency because S03 cannot return any explanation without its verifier.

## Consequences

- Domain and canonical data remain unchanged; the workflow has no write tool.
- The workflow is internal to AgentWorker. The hosted Worker remains idle until an authenticated request/message consumer and real context-tool composition are implemented. No public API/UI result is implied.
- DI registration remains deferred because no real `IMatchIntelligenceReadOnlyTool` implementation or authenticated request consumer is yet composed in AgentWorker; this decision does not authorize Azure resource changes, model deployment, live Foundry calls or DEV deployment.
- Measured token/cost behavior, model performance, semantic football verification and PO acceptance remain open.
