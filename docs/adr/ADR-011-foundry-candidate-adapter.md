# ADR-011 — Server-side Foundry candidate generation

**Status:** Accepted for implementation boundary; real model integration and product acceptance pending. 2026-10-10.

## Context

EP08-S01 supplies bounded, revisioned, selected-time canonical evidence through a read-only application packet. The AgentWorker was an idle scaffold. The product requires natural-language explanations, while deterministic football truth must remain independent of model availability and output.

## Decision

The AgentWorker owns the Foundry transport adapter and Azure-profile composition. It supports either the Azure Foundry OpenAI-compatible chat-completions v1 endpoint using `ManagedIdentityCredential` and the `https://cognitiveservices.azure.com/.default` scope, or a project-scoped agent Responses endpoint using the project endpoint, configured agent name and `https://ai.azure.com/.default` scope. Both endpoint types are restricted to known Azure AI domain suffixes, and the HTTP handler disables redirects before a bearer token is attached. Request and response budgets, timeout, concurrency and output-token limits are validated configuration. The output is a strict JSON candidate with claims and evidence IDs. A candidate citing an ID outside its input packet is rejected. Logs contain deployment/agent identifier, duration, usage counts and trace ID, never prompt, evidence, response body or bearer token.

Candidate generation does not verify semantic truth. EP08-S04 now includes a narrow Application source-text guard that accepts only an exact restatement of the selected insight with its full evidence set and preserves its truth class. It never authorizes publication and does not prove that the persisted insight itself or arbitrary natural-language claims are semantically correct. Candidates remain internal-only: no public API, UI display, canonical write or accepted explanation. Provider failure, timeout and identity errors return a structured unavailable outcome so a caller can select deterministic fallback.

## Consequences

- The Application depends only on its candidate-generator port; HTTP and Azure SDK code remain in AgentWorker, and Domain remains untouched.
- LOCAL tests use injected fake token and HTTP ports. Model mode requires Foundry endpoint/deployment; project-agent mode requires the project endpoint and agent name (default `TacticalAnalyst`). Both require managed identity and the corresponding token audience. No local endpoint is allowed in Azure.
- The tactical profile catalog contains nine local specialist lenses plus an evidence-first default; these are prompt profiles, not claims that matching remote Foundry agents exist. The adapter does not create or update Azure agents. Real Foundry model behavior, project-agent identity/RBAC, prompt-injection resistance, semantic claims, latency/cost, and Product Owner acceptance remain unverified. This decision does not authorize Azure resource changes or DEV deployment.
