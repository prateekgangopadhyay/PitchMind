# ADR-011 — Server-side Foundry candidate generation

**Status:** Accepted for implementation boundary; real model integration and product acceptance pending. 2026-10-10.

## Context

EP08-S01 supplies bounded, revisioned, selected-time canonical evidence through a read-only application packet. The AgentWorker was an idle scaffold. The product requires natural-language explanations, while deterministic football truth must remain independent of model availability and output.

## Decision

The AgentWorker owns the Foundry transport adapter and Azure-profile composition. It sends only the bounded application packet and a length-capped question to the Azure Foundry OpenAI-compatible chat-completions v1 endpoint, using `ManagedIdentityCredential` and the `https://cognitiveservices.azure.com/.default` scope. Endpoint hosts are restricted to known Azure AI domain suffixes, and the HTTP handler disables redirects before a bearer token is attached. Request and response budgets, timeout, concurrency and output-token limits are validated configuration. The output is a strict JSON candidate with claims and evidence IDs. A candidate citing an ID outside its input packet is rejected. Logs contain deployment, duration, usage counts and trace ID, never prompt, evidence, response body or bearer token.

Candidate generation does not verify semantic truth. Until EP08-S04 supplies an independent claim verifier, candidates are internal-only: no public API, UI display, canonical write or accepted explanation. Provider failure, timeout and identity errors return a structured unavailable outcome so a caller can select deterministic fallback.

## Consequences

- The Application depends only on its candidate-generator port; HTTP and Azure SDK code remain in AgentWorker, and Domain remains untouched.
- LOCAL tests use injected fake token and HTTP ports. Azure profile requires Foundry endpoint/deployment configuration and managed identity; no local endpoint is allowed in Azure.
- Real Foundry model behavior, identity RBAC, prompt-injection resistance, semantic claims, latency/cost, and Product Owner acceptance remain unverified. This decision does not authorize Azure resource changes or DEV deployment.
