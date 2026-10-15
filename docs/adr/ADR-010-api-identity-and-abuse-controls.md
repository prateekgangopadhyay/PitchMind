# ADR-010 — API identity, role boundaries and local synthetic access

Status: Proposed with EP06-S04 implementation; independent security review and Product Owner acceptance pending.

## Context

The API has a fail-closed legacy match surface and PostgreSQL-backed canonical match reads. Replay and raw evidence require a narrower audience than ordinary match reads. No production issuer, audience, or public-synthetic approval is committed to the repository, and no client may carry an identity secret.

## Decision

- The Azure runtime profile requires an explicit Entra authority and audience. ASP.NET JWT bearer validation requires HTTPS metadata, a valid issuer, the configured audience, a signed token, a valid lifetime, and the `roles` claim; supported roles are `fan`, `analyst`, and `admin`.
- Canonical summary, event, insight, fixture and key-moment reads require `fan`, `analyst`, or `admin`. Canonical replay, evidence payloads and analyst actions require `analyst` or `admin`; the admin policy is reserved for future administrative/export endpoints.
- The local profile may run only with `ASPNETCORE_ENVIRONMENT=Development` and `Runtime:Profile=local`. No fixture-backed demo route is served by this runtime. Legacy match routes and canonical routes require roles; if no local identity adapter or Entra configuration exists they fail closed with a typed identity-unavailable response. The optional `Identity:DevelopmentHeaderAuthEnabled` scheme is a local test adapter, accepts only known roles, and cannot start outside the local Development profile. For browser SignalR, the adapter also accepts an unsigned `pmdev.v1.` base64url JSON bearer (`{"sub":"...","roles":["fan"]}`) on HTTP requests and the `access_token` query parameter on `/hubs/matches` only; this is equivalent to the spoofable development headers and is strictly local-only, not an identity provider or cloud credential.
- Runtime profile must be explicitly `local` or `azure`; a local profile cannot start outside Development, and an Azure profile cannot start without Entra configuration. No cloud request or resource provisioning is part of this decision.
- Per-IP API reads are limited to 120/minute, analyst actions to 30/minute, and request bodies to 4096 bytes. Rejections use typed 413/429 problems. CORS is same-origin by default; configured origins must be explicit, with HTTPS required outside local Development. CSP and browser hardening headers are emitted on API and static responses.
- Protected request outcomes are audited with endpoint name, status, trace ID and a pseudonymous subject hash; bearer tokens and raw request bodies are never logged. Azure authority configuration is restricted to HTTPS `login.microsoftonline.com`.

## Consequences and remaining risks

Production identity settings and role assignments must be supplied through protected runtime configuration. The application does not provision Entra applications, configure tenant roles, or prove Azure token issuance. SignalR's JWT query-token fallback is scoped to the exact hub path; query tokens are not accepted on ordinary API routes. The configured proxy chain and real client-IP propagation for per-IP rate limits need a deployment review before relying on their production partitioning. CORS does not enable cross-origin credentials. Local development headers and unsigned local browser tokens are not identity providers and are not acceptable as cloud evidence.

## Verification

EP06-S04 API tests exercise anonymous 401, fan/analyst 403 boundaries, configured Azure bearer challenge, fail-closed synthetic routes, oversize 413, rate-limit 429, and refusal to start an Azure profile without a valid Entra authority/audience. These are local tests, not Entra tenant validation or DEV deployment evidence.
