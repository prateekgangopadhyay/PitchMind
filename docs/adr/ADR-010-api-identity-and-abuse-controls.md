# ADR-010 — API identity, role boundaries and local synthetic access

Status: Proposed with EP06-S04 implementation; independent security review and Product Owner acceptance pending.

## Context

The API has two read surfaces: a local synthetic fixture demonstration and PostgreSQL-backed canonical match reads. Replay and raw evidence require a narrower audience than ordinary match reads. No production issuer, audience, or public-synthetic approval is committed to the repository, and no client may carry an identity secret.

## Decision

- The Azure runtime profile requires an explicit Entra authority and audience. ASP.NET JWT bearer validation requires HTTPS metadata, a valid issuer, the configured audience, a signed token, a valid lifetime, and the `roles` claim; supported roles are `fan`, `analyst`, and `admin`.
- Canonical summary, event, insight, fixture and key-moment reads require `fan`, `analyst`, or `admin`. Canonical replay, evidence payloads and analyst actions require `analyst` or `admin`; the admin policy is reserved for future administrative/export endpoints.
- The local profile may run only with `ASPNETCORE_ENVIRONMENT=Development` and `Runtime:Profile=local`. In that profile only the fixture-backed GET demo reads are anonymous and every response is labeled `X-PitchMind-Data-Class: synthetic-demo`. Canonical routes still require a role; if no local identity adapter or Entra configuration exists they fail closed with 503. The optional `Identity:DevelopmentHeaderAuthEnabled` scheme is test-only, accepts only known roles, and cannot start outside the local Development profile.
- Runtime profile must be explicitly `local` or `azure`; a local profile cannot start outside Development, and an Azure profile cannot start without Entra configuration. No cloud request or resource provisioning is part of this decision.
- Per-IP API reads are limited to 120/minute, analyst actions to 30/minute, and request bodies to 4096 bytes. Rejections use typed 413/429 problems. CORS is same-origin by default; configured origins must be explicit, with HTTPS required outside local Development. CSP and browser hardening headers are emitted on API and static responses.
- Protected request outcomes are audited with endpoint name, status, trace ID and a pseudonymous subject hash; bearer tokens and raw request bodies are never logged.

## Consequences and remaining risks

Production identity settings and role assignments must be supplied through protected runtime configuration. The application does not provision Entra applications, configure tenant roles, or prove Azure token issuance. The configured proxy chain and real client-IP propagation for per-IP rate limits need a deployment review before relying on their production partitioning. CORS does not enable cross-origin credentials. The local development header scheme is not an identity provider and is not acceptable as cloud evidence.

## Verification

EP06-S04 API tests exercise anonymous 401, fan/analyst 403 boundaries, configured Azure bearer challenge, local synthetic classification, oversize 413, rate-limit 429, and refusal to start an Azure profile without issuer/audience. These are local tests, not Entra tenant validation or DEV deployment evidence.
