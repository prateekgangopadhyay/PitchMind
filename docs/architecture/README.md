# C4 architecture index

- [C4 Level 1 — System Context](01-system-context.md)
- [C4 Level 2 — Containers](02-container.md)
- [C4 Level 3 — Components](03-components.md)
- [Deployment and trust boundaries](04-deployment.md)

**Status:** Proposed target architecture. Advanced optional capabilities are not MVP prerequisites.

### Invariants
1. Domain project has zero external package/framework dependencies.
2. Canonical football truth is computed deterministically from versioned events and tracking.
3. Event Hubs partition by match ID; consumers must still enforce sequence and idempotency.
4. Canonical write + outbox record occur in one PostgreSQL transaction.
5. Agent output is untrusted until evidence and policy checks pass.
6. Local Docker and Azure deployment adapters implement the same application ports.

- [Football LLD and reference decisions](05-football-lld.md)
- [Azure implementation delta and release contract](../engineering/AZURE-DELIVERY-GAPS.md)
- [Consolidated API contracts](../api/API.md)
- [Story execution register](../backlog/STORY-EXECUTION.md)

## Engineering Council execution architecture
- [Detailed Azure-native HLD (proposed)](06-target-azure-hld.md)
- [Football intelligence target LLD (proposed)](07-football-analytics-detailed-lld.md)
- [Simulation feed import/source contract (blocked on JSON)](../data/SIMULATION-FEED-CONTRACT.md)
- [Seven-screen UX contracts](../engineering/FRONTEND-SCREEN-CONTRACTS.md)
- [59-story work packets](../backlog/STORY-WORKPACK-INDEX.md)
These are **target designs**, not evidence that managed PostgreSQL, Event Hubs, Foundry, SignalR or production identity are wired. Any material changes to containers, trust boundaries, event/data contracts or deployment flow require diagrams, API and ADR updated in the same PR. DEV changes remain owner-approval gated.
