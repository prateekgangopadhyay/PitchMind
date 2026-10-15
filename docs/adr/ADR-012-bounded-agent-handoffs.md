# ADR-012 — Bounded claim-gated explanation handoffs

**Status:** Accepted for the local code boundary; runtime composition, independent review and product acceptance pending. 2026-10-15.

## Context

EP08-S01 supplies an immutable, selected-time, revisioned `MatchIntelligenceContextPacket`. EP08-S02 supplies a server-side candidate-generator port and EP08-S04 supplies the deterministic verifier. The story asks whether bounded specialist stages add value without allowing generated language or citations to become canonical football facts.

## Decision

Implement a bounded in-process `ExplanationAgentOrchestrator` in AgentWorker with three typed handoff stages: (1) validate/fingerprint an already-authorized read-only evidence packet, (2) request one audience-aware untrusted candidate from `IExplanationCandidateGenerator`, and (3) pass the candidate to `ExplanationCandidateVerifier`. Only the verifier's safe output may be returned. There are no model-controlled tools, canonical writes, automatic retries, public HTTP contract or additional model calls.

The host must bind `IMatchIntelligenceReadOnlyTool` to trusted caller identity before composition. The workflow applies packet/evidence/question/token/time/concurrency budgets and linked cancellation. Failure is returned as a bounded status and code. Traces contain workflow identity, exact selected-time/revision/sequence, status, duration and token counts, but no raw packet/candidate text. Evidence identifiers are traced only after packet validation; candidate citation identifiers are never copied to trace output; successful verifier evidence IDs are the only generated evidence IDs emitted.

This boundary describes three deterministic handoff roles, not three autonomous LLM agents. The current AgentWorker does not register or invoke the orchestrator, and this decision does not claim live Foundry operation or a measured accuracy uplift. The existing selected-time request limit remains 0..5400 seconds.

## Consequences

- Domain and canonical storage are unchanged; model output cannot mutate either.
- Unit tests can compare an unverified single-generator candidate with the verifier-gated result and exercise rejection, cancellation, timeout, unavailable context and concurrency saturation.
- A single narrator call avoids extra model cost; evidence curation and claim verification are local bounded stages.
- Human security/football review, authenticated host composition, live model cost/latency evaluation, hosted CI and Product Owner acceptance remain pending. No Azure resource changes or DEV deployment are authorized.
