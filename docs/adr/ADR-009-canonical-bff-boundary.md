# ADR-009 — Separate canonical BFF from offline demo routes

Status: Proposed in EP06-S01; independent architecture and Product Owner review pending.

Context: The current HTTP API serves an offline synthetic fixture. A PostgreSQL event ledger, revisioned projections, and persisted insight-evidence tables are also available, but they do not yet support every frontend read model. Reusing fixture endpoints or silently substituting fixture data would make canonical clients confuse demo facts with persisted canonical state.

Decision: expose PostgreSQL-backed reads through the separate `/api/v1/canonical/*` route namespace and dedicated Application reader ports. These routes return only persisted state and evidence bounded by match, selected time, revision, sequence, and stable cursors. Evidence records distinguish the insight's admission revision from each referenced event/frame's source admission revision; the v9 migration backfills legacy same-revision references and leaves unknown references unreadable. If the canonical adapter or requested persisted read model is unavailable, return a typed 503. Do not fall back to `SyntheticMatchService`. Canonical key moments remain unavailable until a persisted projection exists. Keep current unprefixed fixture routes as explicitly synthetic demo routes.

Security consequence: these routes are not yet authenticated. Cloud ingress must not expose them until EP06-S04 adds the identity boundary or the owner explicitly approves a synthetic public-read profile. This ADR is not deployment authorization.

Verification consequence (2026-10-15, current local worktree): `dotnet build PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` succeeds with 0 warnings/errors. The full .NET Release suite passes 262 tests (Application 67, Architecture 22, Contracts 88, Domain 67, Infrastructure 18), with 24 Infrastructure tests skipped because no PostgreSQL test database is configured. Frontend tests pass 6/6, and the frontend production build and lint pass. OpenAPI YAML has 8 paths; routing JSON validation, operating-model validation and `git diff --check` pass. The new v9 migration and earlier-source event/frame regression compile but were not executed against PostgreSQL. No PostgreSQL-backed behavior is claimed as verified on this worktree. Independent technical review and Product Owner acceptance remain pending.

Architecture: see [container](../architecture/02-container.md), [components](../architecture/03-components.md), and [canonical BFF OpenAPI](../api/openapi-canonical-bff-v1.yaml).
