# ADR-011 — Server-side Foundry candidate generation

**Status:** Accepted for implementation boundary; real model integration and product acceptance pending. 2026-10-10.

## Context

EP08-S01 supplies bounded, revisioned, selected-time canonical evidence through a read-only application packet. The AgentWorker was an idle scaffold. The product requires natural-language explanations, while deterministic football truth must remain independent of model availability and output.

## Decision

The AgentWorker owns the Foundry transport adapter and Azure-profile composition. It sends only the bounded application packet and a length-capped question to the Azure Foundry OpenAI-compatible chat-completions v1 endpoint, using `ManagedIdentityCredential` and the `https://cognitiveservices.azure.com/.default` scope. Endpoint hosts are restricted to known Azure AI domain suffixes, and the HTTP handler disables redirects before a bearer token is attached. Request and response budgets, timeout, concurrency and output-token limits are validated configuration. The output is a strict JSON candidate with claims and evidence IDs. A candidate citing an ID outside its input packet is rejected. Logs contain deployment, duration, usage counts and trace ID, never prompt, evidence, response body or bearer token.

Candidate generation does not verify semantic truth. EP08-S04 adds a verifier that requires exact match/revision/selected-time metadata and packet-local evidence IDs, then discards model-authored prose and produces only deterministic wording from the selected insight classification, time, revision and evidence count/types. Exact evidence IDs remain in the structured citation field. This evidence-only reformulation is deliberately narrower than a semantic claim verifier; it does not prove the insight's football meaning and does not authorize publishing model prose. There is still no public API, UI display, canonical write or runtime caller. Provider failure, timeout and identity errors return a structured unavailable outcome so a caller can select deterministic fallback.

EP08-S05 adds an asynchronous Application resilience policy around the generator/verifier. It has a request-size limit, configured output-token ceiling, a pre-call UTF-8 serialized request-byte ceiling used as a conservative input-token proxy, and post-call provider-reported input/output token validation; it also has a process-local sliding provider-call budget, immediate concurrency refusal, call timeout, no automatic retry, a threshold circuit breaker with one half-open probe, and a bounded short-TTL cache keyed by authorization scope, question, audience, match/revision/selected time and complete packet hash. Only verified candidate results are cached. Degraded results use deterministic text built from the exact selected canonical score, insight classification, and evidence metadata; candidate prose and evidence payload text are excluded. The policy is not called by an API or worker request handler yet, and event ingestion does not depend on it. Limits and cache state are process-local, so a multi-replica service still needs a shared budget ledger before the configured limits can serve as an aggregate spend cap.

## Consequences

- The Application depends only on its candidate-generator and governance components; HTTP and Azure SDK code remain in AgentWorker, and Domain remains untouched.
- LOCAL tests use injected fake token and HTTP ports. Azure profile requires Foundry endpoint/deployment configuration and managed identity; no local endpoint is allowed in Azure.
- Real Foundry model behavior, identity RBAC, prompt-injection resistance, semantic claims, latency/cost, and Product Owner acceptance remain unverified. This decision does not authorize Azure resource changes or DEV deployment.
