# EP06-S03 browser client integration slice — 2026-10-15

**Story:** EP06-S03 — Reconnect, backfill and historical context (P0).
**Accountable skill:** `lead-frontend-engineer`.
**Consult skills:** `real-time-streaming-engineer`, `accessibility-engineer`, `application-security-engineer`, `qa-architect`.
**Pull request:** [#268](https://github.com/prateekgangopadhyay/PitchMind/pull/268), draft, restacked after parent PR #267 merged.
**Base revision:** `main` at `f000e561fe32a0b26f34b09ffe6b6cea98e75cc4`.
**Code revision after restack:** `64cf6646ac71d3576aec5a411838d0197d28d0ae`.

## Scope

The canonical React match page now mounts the SignalR client behind a host authentication provider. The client negotiates using a short-lived bearer, validates the same-origin exact hub URL, joins a single match group, and treats SignalR messages as cursor wakeups only. It retrieves canonical backfill through the protected BFF after connect, reconnect and notifications. A live revision/sequence advance refreshes the rendered canonical page. A historical view retains its selected timestamp and exposes a polite new-updates status with a return-to-live action. The fixture demo does not mount this client. The host owns identity and token storage; an absent host identity leaves live updates unavailable. API request auth uses the same token provider. The package lock includes the SignalR client dependency.

This is a local implementation slice. It does not provide a production sign-in integration or establish a connected-browser journey.

Playback now switches a live view to a pinned historical projection before advancing. The match page retains playback state as the URL revision changes between seconds. A regression test advances playback across multiple selected seconds. Historical status text no longer changes with every replay tick, avoiding repeated live-region announcements.

Historical projection refreshes also retain the separately observed LIVE cursor, its update notice, and the last known live time. Replay ticks therefore do not clear a pending update notice or change the later return-to-live target.

## Verification on the implementation tree

- `npm test` on corrected code revision `64cf664`: 5 files passed, 83 tests passed, 0 failed.
- `npm run build`: passed; Vite emitted two vendor annotation warnings from SignalR's `Utils.js`, which Rollup removed.
- `npm run lint`: passed.
- `npm ls --package-lock-only --depth=0`: passed; lock resolves SignalR 9.0.19 and existing project dependencies.
- `node scripts/validate_operating_model.mjs`: passed (12 epics, 59 stories, 28 Council skills, 5 design skills, no cycles).
- `node scripts/test_design_skills.mjs`: passed (five design skills registered and Awesome copy instrument self-test).
- `node scripts/design-slop-scan.mjs frontend/src/RealtimeMatchUpdates.tsx`: passed with no warnings.
- `git diff --check`: passed.
- `npm run test:e2e`: could not start because the available linked `node_modules` has no `playwright` executable (`command not found`). Browser automation was not run.

Commands ran in the local worktree, with its available linked dependencies. A clean `npm ci` was not available offline in this environment. No API/PostgreSQL integration, connected-client reconnect, real Entra tenant, manual screen-reader test, hosted checks, or Product Owner/football acceptance was performed for this slice. Independent re-review found two issues in prior code heads: replay started from Live could not advance reliably, and a historical refresh could clear a pending live-update notice. Both corrections have regression coverage; review of the exact current head is pending. Playwright remains unrun because its executable is missing from the linked dependencies. No Azure resources or deployments changed.

## Status and remaining risks

PR #267's backend browser-auth implementation was merged to main at `f000e561fe32a0b26f34b09ffe6b6cea98e75cc4`. PR #268 targets main. Corrected code is pushed at `64cf6646ac71d3576aec5a411838d0197d28d0ae`; exact-head independent review and hosted checks are pending. The host must supply a real signed-in identity provider before the client can connect in an integrated deployment; the opt-in local unsigned token remains Development-only. The API currently caps selected time at 5400 seconds while the candidate source reaches 5761 seconds. EP06-S03 remains unaccepted.
