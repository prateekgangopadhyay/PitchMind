# Football knowledge RAG pipeline

**Status:** local retrieval and Azure Search query adapter are code; Azure index, vectorizer, corpus upload, live query and deployment are not configured or verified. No Azure changes were made. This design applies to curated football education knowledge, not EP12's canonical historical-match retrieval.

## Corpus boundary

The seed corpus combines the versioned tactical cards in `src/PitchMind.AgentWorker/Agents/football-tactical-knowledge-base.v1.json` with short, reviewed abstracts and links from `docs/ai/fifa-training-centre-article-catalog.v1.json`. It does not scrape or copy full third-party articles/media. Each chunk retains a stable source ID, topic, source type, version, and optional HTTPS source URL. Chunks are bounded to 4,000 characters. Rebuild the JSONL locally with:

```sh
python3 scripts/prepare-football-knowledge-corpus.py
```

The generated `data/ai-search/football-tactical-knowledge-seed.jsonl` is an upload input, not canonical football data. Changes to the source catalog require review of rights notes, article availability, abstract quality, and topic mapping before indexing.

## Search index contract

Create an Azure AI Search index with these fields (field names are consumed by `AzureAiSearchTacticalKnowledgeRetriever`):

| Field | Type | Attributes |
| --- | --- | --- |
| `id` | `Edm.String` | key, filterable |
| `topicId` | `Edm.String` | filterable, facetable |
| `title` | `Edm.String` | searchable, retrievable |
| `content` | `Edm.String` | searchable, retrievable |
| `contentVector` | `Collection(Edm.Single)` | vector field, dimension must equal the selected embedding model |
| `sourceUri` | `Edm.String` | retrievable |
| `sourceType` | `Edm.String` | filterable, retrievable |
| `version` | `Edm.String` | filterable, retrievable |

Configure a vector profile/vectorizer on `contentVector` that matches the selected embedding model and dimensions. Configure a semantic configuration that prioritizes `title` and `content`; set its name in `KnowledgeSearch:SemanticConfiguration`. The query uses lexical search plus `vectorQueries` with `kind: text` and semantic ranking, filters the result set to the selected tactical profile's allowed `topicId` values, requests only the contract fields, and returns at most three passages. API version is pinned in code to stable `2025-09-01`; upgrade only with contract and integration tests.

## Local-to-Azure runbook (approval required before any cloud action)

1. Review and version the tactical cards and article catalogue. Keep full source text out unless rights permit it; preserve the public URL and a concise, human-reviewed abstract.
2. Run the offline corpus preparation script and inspect the JSONL for duplicate IDs, oversize chunks, source links, topic routing and prohibited personal or match-specific facts.
3. Create the Search service, index, semantic configuration, vector profile and vectorizer through a separately reviewed infrastructure change. Use private networking where the workload is private. This repository change did not create any resource or apply infrastructure.
4. Upload the seed JSONL to a private Blob container through a dedicated ingestion identity. Configure an Azure AI Search indexer/skillset to vectorize each `content` chunk into `contentVector`; the corpus script intentionally does not generate embeddings. Configure query-time vectorization on the index vectorizer so the worker can send `vectorQueries.kind=text`. Pin the embedding deployment and dimensions in the index contract.
5. Keep the AgentWorker query identity read-only (`Search Index Data Reader`); never give it index write/admin credentials. Use a separate, short-lived deployment identity for index/indexer/schema updates.
6. Give the AgentWorker managed identity only the Search data-reader role and Foundry inference permission. Search and Foundry use separate endpoint/audience token providers; no key or browser credential is accepted.
7. Configure `Runtime:Profile=azure`, `KnowledgeSearch:Endpoint`, `KnowledgeSearch:Index`, and `KnowledgeSearch:SemanticConfiguration`, plus bounded `KnowledgeSearch:Top`, `KnowledgeSearch:TimeoutSeconds`, and `KnowledgeSearch:MaximumResponseBytes`. Missing Azure Search config fails startup; an Azure profile never falls back to the embedded/local adapter.
8. Run retrieval contract tests, query relevance/recall evaluation, prompt-injection tests, filtered-topic negative tests, source/version citation checks, unavailable/timeout tests, latency/token/cost measurements, and comparison against local deterministic fallback before requesting a deployment decision.
9. Record corpus hash, index schema version, source-catalogue review date, embedding deployment/version, chunking version, query API version, evaluation set revision, p50/p95 latency and cost. Rebuild the derived index from the versioned corpus; the index is disposable and never authoritative.

No step in this runbook authorizes Azure DEV provisioning, upload, role assignment, or deployment.

## Runtime trust and fallback

`ITacticalKnowledgeRetriever` is read-only. In Development the embedded adapter performs deterministic profile-allowlisted lexical ranking. In the Azure profile the Search adapter uses Entra managed identity, HTTPS, a redirect-disabled client, strict service-host validation, a 2-call concurrency cap, a 3-second default timeout, a response byte cap, result count/field bounds, and rejects results whose topic is outside the profile allowlist. Search errors return the existing untrusted-candidate `Unavailable` result so the orchestrator can retain its deterministic explanation path.

Retrieved material is passed as untrusted advisory context. It is not a source of match facts, event/player IDs, evidence IDs, scores, or causal proof. It cannot enter the verifier's canonical evidence ID set. The model must cite canonical evidence IDs for factual match claims; retrieved knowledge IDs must not be returned as those citations. Search absence or mismatch cannot broaden the query to another profile's topics.

The existing public explanation DTO has no knowledge-source citation field. Source URLs are included in model context for provenance, but they are not currently exposed as user-facing citations. Add a separately reviewed contract and verifier path before displaying those references as citations.

## Evaluation gates

Maintain queries for each profile, positive source retrieval, expected abstentions, near-topic confusion, out-of-profile articles, prompt-injection text in source abstracts, stale source versions, no-result and Search outage. Measure retrieval relevance separately from grounded match-claim correctness. A relevant article never makes an unsupported match claim acceptable. Local unit results are not live Azure retrieval/model evidence.
