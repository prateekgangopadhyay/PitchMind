# PitchMind — Living architecture index

Architecture documents distinguish **implemented in source/configuration**, **resource observed in Azure**, **runtime path verified**, **TARGET**, and **OPTIONAL**. These are different evidence levels: a resource inventory does not prove the app is using a resource, and a UI screenshot does not prove the service chain. DEV diagrams use Azure services only; LOCAL Docker topology is in the local development guide. Current DEV inventory and known runtime limitations are maintained in [Azure DEV integration status](../engineering/AZURE-DEV-LIVE-INTEGRATION-STATUS.md).

- [C4 Level 1 — System Context](01-system-context.md)
- [C4 Level 2 — Containers, Azure DEV and local/target views](02-container.md)
- [C4 Level 3 — Components and ports/adapters](03-components.md)
- [C4 Level 4 — Deployment/trust and environments](04-deployment.md)
- [Football analytics baseline LLD](05-football-lld.md) and [detailed football LLD](07-football-analytics-detailed-lld.md)
- [Azure DEV HLD and canonical target](06-target-azure-hld.md)
- [Azure and Microsoft Foundry technologies](AZURE-AND-FOUNDRY-TECHNOLOGIES.md)
- [Football knowledge RAG design and local-to-Azure runbook](../ai/FOOTBALL-KNOWLEDGE-RAG-PIPELINE.md)
- [Architecture review + findings](../engineering/ARCHITECTURE-REVIEW-2026-10-08.md) and [ADR index](../adr/README.md)
- [Observed source profile](../data/MANU-LIV-SOURCE-PROFILE.md) and [source feed contract](../data/SIMULATION-FEED-CONTRACT.md)
- [Consolidated API current/target registry](../api/API.md)
- [Engineering Council operating model](../operating-model/ENGINEERING-OPERATING-MODEL.md), [Hackathon execution roadmap](../operating-model/ALL-STORIES-EXECUTION-ROADMAP.md) and [existing story status register](../backlog/STORY-EXECUTION.md)

## Invariants and architecture drift policy
1. Domain project has zero external package/framework dependencies; adapters only in Infrastructure.
2. Canonical football truth is deterministic from reviewed, rights-cleared synthetic source and verifiable spatial/time semantics. Claims always cite event IDs, source SHA and detector versions.
3. Event Hubs partitions by matchId; consumers enforce unique EventId/sequence, at-least-once + idempotency and explicit gap/quarantine. No global exactly-once claim.
4. PostgreSQL commits canonical facts+outbox transactionally; projections/search/Redis/SignalR never become football truth.
5. Candidate AI text is an untrusted hypothesis. The direct Foundry project-agent API validates shape and evidence-ID membership; that is not semantic verification. The browser tactical-discussion helper is locally computed illustrative text, not an agent response.
6. DEV/STAGE/PROD use Azure profiles; LOCAL uses separate Docker adapters. Deployment requires the reviewed Azure DevOps release controls. The current pipeline YAML defaults `enableDevDelivery` to true; external service connections and approval checks are not proven by repository files.
7. Every PR that changes an architecture boundary must update the relevant C4/ADR, source contract, LLD, API, tests, threat model and runbook with proof and decision-maker. Never invent a "deployed" arrow because Bicep contains a resource.
