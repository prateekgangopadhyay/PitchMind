# ADR-008 — Bounded offline synthetic vertical slice

Status: Accepted for LOCAL and explicitly labeled DEV prototype only, 2026-10-08.

Problem: existing application scaffold has no durable canonical Azure implementation. Frontend/domain/API integration needs executable shared evidence without falsely claiming that infrastructure placeholders complete the product.

Decision: preserve the existing scaffold and extend it with an immutable seeded synthetic fixture, pure selected-time projections, bounded deterministic pressing candidate and evidence-linked fallback. LOCAL and the separately tagged DEV demonstration may use this read-only adapter. Current coordinates are stadium metres (105 × 68), not the old repository's normalized percentages. Public UI labels simulation. A dedicated DEV workload template makes this limitation visible in resource tags and delivery evidence.

Consequences: no match state is written by AI; read-only snapshots can exercise contracts and UX without cloud credentials. This adapter is not durable canonical storage and cannot satisfy EP04-S02/S03, Azure Event Hubs, SignalR or Foundry story gates. Azure-backed implementation must use the existing application ports and preserve tested selected-time/score/evidence behavior. Never change this prototype into a production profile through configuration aliases. Full P0 acceptance still requires PostgreSQL and actual Azure integration evidence.

Evidence: current `SyntheticMatchService` explicitly identifies the offline read model, immutable fixture generator and detector; root validation report records unit/HTTP/browser results. No legacy implementation was deleted. The dedicated DEV Bicep compiles and what-if previews only app, identity and scoped registry role creates. This ADR establishes intent, not deployment success.
