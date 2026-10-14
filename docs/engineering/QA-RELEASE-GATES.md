# PitchMind MVP — QA, Security and Release Certification Matrix
**Release state:** Not certified. A checklist or test source file does NOT establish a passing test, deployed integration or accepted story.

## Mandatory acceptance matrix
| Concern | Gate and evidence | Owner |
|---|---|---|
| Source feed | SHA-256 and schema profile of actual user JSON; synthetic/rights/provenance signoff; no hardcoded facts in C#/React | Data + PO |
| Architecture | .NET Domain zero external deps; API/LLD/ADR versions, app ports, no unsupported Azure fallbacks | Principal Architect |
| Streaming | MatchId partition, stable eventId, idempotent duplicate, gap/quarantine/restart and at-least-once handling | Streaming |
| PostgreSQL | isolated migration + runtime role; restart, append-only protections, atomic outbox and deterministic replay | DBA |
| Football | positives and adversarial negatives for press, phase/scoreline, halftime direction, incomplete tracking, source-to-evidence IDs | Football PO |
| Match experience | 55:08 before 55:09 score boundary, no-lookahead in scoreboard/frames/moments/AI; responsive a11y/screen reader | Frontend + QA |
| Foundry | actual evidence-bounded model output, structured claim rejection, prompt injection, adversarial stats, timeout fallback | AI + AppSec |
| Login/Auth | Entra OIDC PKCE, denied cross-role resources, session expiry, XSS/CSRF/CORS, JWT issuer/audience/expiry | AppSec |
| Performance | define 500 event/sec burst duration/payload/hot partitions, 100–500 viewers if staffed; report p50/p95/p99/lag/duplicate loss | Performance + SRE |
| Operability | actual ACA/PG/Event Hubs/SignalR/Foundry connectivity, traces, secrets, cost alerts, PITR and rollback drills | Azure + SRE |
| Release | Azure DevOps green run, SBOM/SCA/SAST, immutable image digest and API/browser smoke on approved DEV | QA + DevOps |
| Hackathon | public sanitized repo, original/rights-cleared branding, synthetic match labeled, truthful demo video <120s | PO |

## Fixture and testing layers
- Unit xUnit: football rules, coordinate mirror, clock boundaries, deterministic fallback, evidence link invariance; dependency-direction tests.
- PostgreSQL integration: Compose-isolated database, reject skipped cases, crash after commit/before dispatch, schema backward compatibility, role denials, checkpoint and replay hash.
- Contract: OpenAPI consumer examples, pagination/revision 409, HTTP 400/401/403/404/413/422/429/503, valid and invalid JSON input.
- Frontend design: consult five registered specialists (Taste, Impeccable, Emil, Vercel guidelines and independent Awesome) against real changed UI; optional upstream Impeccable CLI is not a release gate. Maintain keyboard, focus, selected-time truth and reduced-motion evidence.
- Frontend Vitest + Playwright: seven routes, source-only fixture content, live vs selected time, one evidence drill-down, no future events, model-down, keyboard tab, focus return, screen reader alternative to pitch.
- Azure smoke: only after explicit owner approval; use deployed exact Git SHA and image digest, read-only basic metrics, synthetic ingestion/replay and live reconnect; never claim green from mock results.
- Reliability: network/PG/Redis/SignalR/Foundry failure, intentionally malformed feed, cancellation, duplicate stream and DLQ recovery. Quarantine rather than silently skip.
- Security: OIDC JWT claims, token lifetime, managed identity, KV scoped permissions, private DNS and denied network operations, dependency licensing and source media rights.
- FinOps: projection of SKU/TU/replica/model tokens and 30-day budget by env; daily alerts, expiry/cleanup.

## Evidence record per story
`{storyId,status,owner,reviewers,gitSha,testCommand,testResult,fixtureSha,algorithmVersion,apiVersion,environment,deployDigest,securityEvidence,accessibilityEvidence,performanceEvidence,unresolvedRisks,POAcceptance}`. `status=accepted` is permitted ONLY after verifiable evidence and human owner review; never infer from code existence.

## Release stop conditions
Actual data is real EPL match telemetry or unlicensed; mismatched score/time; unsupported AI claim published; source data unavailable or quietly substituted; unauthenticated mutation; any skipped mandatory DB test; no reproducible replay; corrupted outbox; unbounded Azure spend; DEV change unapproved; image digest or license untraceable; video demo implies real live Premier League feed.
