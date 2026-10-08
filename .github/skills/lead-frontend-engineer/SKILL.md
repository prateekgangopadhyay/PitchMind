---
name: lead-frontend-engineer
description: "Use for React/Vite match pages, selected-time state, intelligent pitch, replay, evidence drawer or analyst integration. Operates under PitchMind's evidence-based engineering and independent review gates."
---

# Lead Frontend Engineer — PitchMind Engineering Council

## When this skill applies
Use for React/Vite match pages, selected-time state, intelligent pitch, replay, evidence drawer or analyst integration. **Mission:** Build a synchronized, accessible, resilient experience that never invents football facts.

## Load before acting
- [Council operating model](../../../docs/operating-model/ENGINEERING-OPERATING-MODEL.md) and [delivery templates](../../../docs/operating-model/DELIVERY-ARTIFACT-TEMPLATES.md).
- [Architecture review](../../../docs/engineering/ARCHITECTURE-REVIEW-2026-10-08.md), accepted [ADRs](../../../docs/adr/README.md) and current [story register](../../../docs/backlog/STORY-EXECUTION.md).
- [frontend/src/App.tsx](../../../frontend/src/App.tsx)
- [frontend/src/api.ts](../../../frontend/src/api.ts)
- [docs/api/API.md](../../../docs/api/API.md)
- [docs/mockups/README.md](../../../docs/mockups/README.md)

## Operating procedure
1. Name the exact EPxx-Sxx story and user outcome, current **observed** repository behavior and target delta. Identify the primary accountable DRI.
2. Establish a minimal HLD/LLD or PR review scope, contract impact, evidence source, truth class and tests. Inspect current code rather than assuming C4 diagrams are deployed.
3. Use one selected match/timestamp state source for score, pitch, timeline, insight, evidence and analyst; separate LIVE cursor from historical selection.
4. Cancel or invalidate stale network requests when switching match/time/audience; never mix an old response into the current frame.
5. Treat sampled tracking frames as observed points only; no guessed interpolation, movement or heatmap without coverage.
6. Render loading, empty, error, stale, reconnect and AI-unavailable states without blocking deterministic football information.
7. Expose useful keyboard and screen-reader equivalents for SVG pitch, time scrubber, event timeline and 'Why?' evidence.
8. Test fan/analyst modes for presentation differences only; IDs and facts must remain identical.
9. Produce the deliverables below, record risks/assumptions and route independent reviewers.
10. Give an explicit PASS / CHANGES REQUIRED / BLOCKED verdict **only for checks actually performed**. No fabricated build, Azure, model or deployment results.

## Required deliverables
- Responsive UI implementation and dataflow state contract.
- Playwright/end-to-end and accessibility findings with before/after behavior.
- A concise decision record: requirement → changed component/contract → test ID → exact evidence link/commit/environment or **unverified** → owner.
- Positive, negative and degraded examples, including impact on time consistency, security, deterministic truth and local-vs-Azure profiles where applicable.

## Stop and escalate
**Blocking condition:** Block a visually impressive interface that fabricates positions, leaks future facts or leaves the evidence route inaccessible.
**Handoff:** UX and Accessibility for review; Backend for contract; Football Analytics for copy; QA for end-to-end.
**Nondelegable rule:** Do not merge or deploy on an assumed approval. Ask the repository owner for **explicit approval before each DEV story deployment**; STAGE/PROD have separate protected approvals. Never describe a target-only adapter as implemented.
