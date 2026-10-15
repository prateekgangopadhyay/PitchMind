# EP06-S03 browser client integration slice — 2026-10-15

**Story:** EP06-S03 — Reconnect, backfill and historical context (P0).
**Accountable skill:** `lead-frontend-engineer`.
**Consult skills:** `real-time-streaming-engineer`, `accessibility-engineer`, `application-security-engineer`, `qa-architect`.
**Pull request:** [#268](https://github.com/prateekgangopadhyay/PitchMind/pull/268), draft and stacked on parent PR #267 (`codex/ep06-s03-hub-auth`).
**Base revision:** parent implementation head `acfa8d328fe9572afb3e8ccd87738a79d1a222eb`.

## Scope

The canonical React match page now mounts the SignalR client behind a host authentication provider. The client negotiates using a short-lived bearer, validates the same-origin exact hub URL, joins a single match group, and treats SignalR messages as cursor wakeups only. It retrieves canonical backfill through the protected BFF after connect, reconnect and notifications. A live revision/sequence advance refreshes the rendered canonical page. A historical view retains its selected timestamp and exposes a polite new-updates status with a return-to-live action. The fixture demo does not mount this client. The host owns identity and token storage; an absent host identity leaves live updates unavailable. API request auth uses the same token provider. The package lock includes the SignalR client dependency.

This is a local implementation slice. It does not provide a production sign-in integration or establish a connected-browser journey.

## Verification on the implementation tree

- `npm test`: 5 files passed, 76 tests passed, 0 failed.
- `npm run build`: passed; Vite emitted two vendor annotation warnings from SignalR's `Utils.js`, which Rollup removed.
- `npm run lint`: passed.
- `npm ls --package-lock-only --depth=0`: passed; lock resolves SignalR 9.0.19 and existing project dependencies.
- `node scripts/validate_operating_model.mjs`: passed (12 epics, 59 stories, 28 Council skills, 5 design skills, no cycles).
- `node scripts/test_design_skills.mjs`: passed.
- `node scripts/design-slop-scan.mjs frontend/src/RealtimeMatchUpdates.tsx`: passed with no warnings.
- `git diff --check`: passed.
- `npm run test:e2e -- --list`: could not start because the available linked `node_modules` has no `playwright` executable (`command not found`). Browser automation was not run.

Commands ran in the local worktree, with its available linked dependencies. A clean `npm ci` was not available offline in this environment. No API/PostgreSQL integration, connected-client reconnect, real Entra tenant, manual screen-reader test, hosted checks, independent review or Product Owner/football acceptance was performed for this slice. No Azure resources or deployments changed.

## Status and remaining risks

PR #267's backend browser-auth implementation is the parent of this stack and remains draft. PR #268 is submitted as a draft against that feature branch. Exact-head independent review and hosted checks are pending. The host must supply a real signed-in identity provider before the client can connect in an integrated deployment; the opt-in local unsigned token remains Development-only. The API currently caps selected time at 5400 seconds while the candidate source reaches 5761 seconds. EP06-S03 remains unaccepted.
