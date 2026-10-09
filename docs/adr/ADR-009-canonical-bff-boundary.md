# ADR-009 — Separate canonical BFF from offline demo routes

Status: Proposed in EP06-S01; independent architecture and Product Owner review pending.

Context: The current HTTP API serves an offline synthetic fixture. A PostgreSQL event ledger, revisioned projections, and persisted insight-evidence tables are also available, but they do not yet support every frontend read model. Reusing fixture endpoints or silently substituting fixture data would make canonical clients confuse demo facts with persisted canonical state.

Decision: expose PostgreSQL-backed reads through the separate `/api/v1/canonical/*` route namespace and dedicated Application reader ports. These routes return only persisted state and evidence bounded by match, selected time, revision, sequence, and stable cursors. If the canonical adapter or requested persisted read model is unavailable, return a typed 503. Do not fall back to `SyntheticMatchService`. Canonical key moments remain unavailable until a persisted projection exists. Keep current unprefixed fixture routes as explicitly synthetic demo routes.

Security consequence: these routes are not yet authenticated. Cloud ingress must not expose them until EP06-S04 adds the identity boundary or the owner explicitly approves a synthetic public-read profile. This ADR is not deployment authorization.

Verification consequence: route and DTO contract tests, selected-time PostgreSQL tests, and negative no-fallback HTTP checks must accompany implementation. Current local HTTP checks cover the no-database typed 503 and missing canonical key-moment 503; PostgreSQL integration and full contract test execution remain outstanding.

Architecture: see [container](../architecture/02-container.md), [components](../architecture/03-components.md), and [canonical BFF OpenAPI](../api/openapi-canonical-bff-v1.yaml).
