# ADR-010 — Configurable JWT authentication and match entitlements

**Status:** Proposed with the EP06-S04 draft implementation, 2026-10-11. Human security and Product Owner review remain pending.

## Context

Canonical match reads and realtime subscriptions expose persisted match state. The previous API registered an empty authentication service and let any configured realtime read role access any existing match. The repository has no authoritative Entra issuer, audience, role names, claim mappings, or user-to-match assignment data.

## Decision

- Validate bearer JWTs through an operator-configured HTTPS OIDC authority, exact audience, signing keys, and token lifetime. Preserve configured claim names without inbound remapping.
- Require the operator to configure the role claim type, match-entitlement claim type, and canonical read roles. Canonical route access additionally requires an entitlement claim whose value is the requested match GUID.
- Require a separately configured catalog role for cross-match fixture listing. With no catalog roles configured, listing is denied.
- Keep the anonymous offline synthetic demo routes mapped only in Development + `Runtime:Profile=local`; return 404 outside that profile and mark demo data explicitly synthetic.
- Use a 4 KiB request/inbound SignalR message limit, bounded canonical/realtime request rates, and an explicit concurrent SignalR connection cap. These limits are configurable or fail closed and do not claim measured SLOs.
- Do not put issuer, audience, role names, claim type values, user identities, or match assignments into repository defaults. Missing or invalid authentication configuration rejects access.

## Consequences and limits

The default branch configuration cannot authenticate real callers until deployment configuration is supplied. The current integration tests use a deterministic test-only authentication handler; they exercise route authorization and failure behavior but do not verify an actual Entra tenant, token signature, role assignment, or entitlement issuance. Security review must validate the intended claim mappings and whether Entra can issue the required match entitlement shape. The current application has no export or administrative routes; future routes need specific policies and auditable behavior before they are mapped. Deployment remains separately gated and unauthorized by this ADR.

## Evidence

EP06-S04 API changes, OpenAPI contract, C4 container diagram, and `AuthorizationAbuseControlTests` provide the implementation/test scope. Exact commands and results are recorded in `docs/backlog/STORY-EXECUTION.md` after execution.
