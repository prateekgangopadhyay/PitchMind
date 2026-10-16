# ADR-014 — Separate tactical-knowledge RAG from canonical match evidence

**Status:** Local implementation and focused tests completed; independent review, Product Owner acceptance and Azure verification pending. No Azure resource or query has been verified. 2026-10-17.

## Context

Tactical profiles currently use a curated football knowledge corpus. More detailed source-backed retrieval can improve the relevance of general football explanations, but it must not broaden the selected-time match-evidence boundary or imply that an article proves what happened in a match. EP12-S01 is a different future capability: provenance-aware search over canonical historical match evidence with authorization and rebuild semantics.

## Decision

Introduce `ITacticalKnowledgeRetriever` in AgentWorker. The Development adapter ranks the embedded, profile-allowlisted cards locally. The Azure adapter queries a separately provisioned Azure AI Search index using Entra managed identity, text-vector hybrid retrieval plus semantic ranking, a `topicId` allowlist filter, and a maximum of three bounded results. A local script prepares one JSONL corpus from curated tactical cards and reviewed article-catalogue abstracts/links; it never crawls or uploads content. Azure worker configuration is required in the Azure profile; the worker does not silently fall back to local retrieval.

Retrieved passages are untrusted advisory knowledge. They may explain general tactical concepts but cannot add match facts, evidence IDs, canonical citations, score, intent, or causal claims. Match claims continue to cite packet evidence and pass the existing verifier. Search is read-only for AgentWorker (`Search Index Data Reader`); corpus ingestion uses a separate controlled identity. The index is derived and rebuildable; PostgreSQL remains canonical.

## Consequences

- Tactical knowledge retrieval and canonical historical evidence search remain separate indexes, contracts, and authorization decisions.
- Retrieval errors use the existing unavailable-candidate path; deterministic explanations remain available.
- Public explanation contracts do not expose knowledge-source citations yet. User-facing source citation requires a separate contract/verifier review.
- Azure index, vectorizer, private networking, identity assignment, relevance, cost, and live model behavior remain unverified. No Azure deployment is implied or permitted by this ADR.

## Review and acceptance gates

Validate corpus provenance and rights, index contract/vector dimensions, profile-filter enforcement, retrieval relevance, prompt injection, stale sources, no-result/outage behavior, latency/cost, and groundedness. Obtain independent AI/AppSec/Football Analytics review and Product Owner acceptance. See [RAG pipeline runbook](../ai/FOOTBALL-KNOWLEDGE-RAG-PIPELINE.md).
