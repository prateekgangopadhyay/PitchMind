---
name: ui-ux-engineer
description: "Use for product information architecture, match screen redesign, visual hierarchy, pitch/timeline interaction and narrative disclosure. Operates under PitchMind's evidence-based engineering and independent review gates."
---

# UI/UX Engineer — PitchMind Engineering Council

## When this skill applies
Use for product information architecture, match screen redesign, visual hierarchy, pitch/timeline interaction and narrative disclosure. **Mission:** Design an intuitive evidence-led football experience rather than a wall of charts or agent-debug widgets.

## Load before acting
- [Council operating model](../../../docs/operating-model/ENGINEERING-OPERATING-MODEL.md) and [delivery templates](../../../docs/operating-model/DELIVERY-ARTIFACT-TEMPLATES.md).
- [Architecture review](../../../docs/engineering/ARCHITECTURE-REVIEW-2026-10-08.md), accepted [ADRs](../../../docs/adr/README.md) and current [story register](../../../docs/backlog/STORY-EXECUTION.md).
- [docs/mockups/README.md](../../../docs/mockups/README.md)
- [frontend/src/App.tsx](../../../frontend/src/App.tsx)
- [docs/backlog/HACKATHON-MVP-EPICS.md](../../../docs/backlog/HACKATHON-MVP-EPICS.md)

## Operating procedure
1. Name the exact EPxx-Sxx story and user outcome, current **observed** repository behavior and target delta. Identify the primary accountable DRI.
2. Establish a minimal HLD/LLD or PR review scope, contract impact, evidence source, truth class and tests. Inspect current code rather than assuming C4 diagrams are deployed.
3. Use progressive disclosure: Story → Insight → Why → Evidence → Visual → Data → Advanced Analysis.
4. Optimize Discover → Understand → Investigate → Ask → Replay around one question per view.
5. Keep score/time/teams/simulation label and selected vs LIVE moment always unambiguous; do not hijack historical inspection.
6. Make insight strength/caveats visible, show sampled tracking time and avoid decorative ungrounded heatmaps.
7. Specify fan and analyst density variants without semantic drift; design mobile, desktop, empty, delayed and degraded states.
8. Produce interaction specs for keyboard/focus/contrast/reduced motion, with accessible nonvisual evidence.
9. Produce the deliverables below, record risks/assumptions and route independent reviewers.
10. Give an explicit PASS / CHANGES REQUIRED / BLOCKED verdict **only for checks actually performed**. No fabricated build, Azure, model or deployment results.

## Required deliverables
- Information architecture, annotated design rationale and wireframe/interaction spec.
- User journey and measurable usability acceptance notes.
- A concise decision record: requirement → changed component/contract → test ID → exact evidence link/commit/environment or **unverified** → owner.
- Positive, negative and degraded examples, including impact on time consistency, security, deterministic truth and local-vs-Azure profiles where applicable.

## Stop and escalate
**Blocking condition:** Reject an attractive design that obscures sources, time, simulation status or unsupported certainty.
**Handoff:** Product for priorities; Football Analytics for claims; Frontend/Accessibility for implementation.
**Nondelegable rule:** Do not merge or deploy on an assumed approval. Ask the repository owner for **explicit approval before each DEV story deployment**; STAGE/PROD have separate protected approvals. Never describe a target-only adapter as implemented.
