# EP07-S06 design review evidence

**Review date:** 2026-10-10  
**Scope:** Canonical match explanation depth, degraded-service states, retry, responsive layout, and focus continuity.  
**External design source:** [Vercel Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md), retrieved 2026-10-10.

## Static review

- Fan and Analyst controls form a named group with pressed-state semantics. Both modes keep the same persisted truth class and summary; Analyst adds stored detector, conditions, and limitations. No AI call is added. Source: `frontend/src/CanonicalMatchPage.tsx:223-228,277-280`.
- Events and insights are optional projections. If either fails, the canonical summary and remaining sections remain visible with a per-section status. Required summary failure is typed, offers retry, and does not substitute demo data. Source: `frontend/src/CanonicalMatchPage.tsx:51-103,200-202,263-280`.
- A citation navigation records its target and restores focus to the matching timeline event, falling back to the selected-time control; on a failed navigation the retry action receives focus. Source: `frontend/src/CanonicalMatchPage.tsx:157-176,264-267,310-318`.
- The audience control has 44px targets and a two-column narrow-screen layout; the evidence list stacks on narrow screens. Source: `frontend/src/styles.css:18,136-145,163-180`.
- The implementation uses existing React/CSS and design tokens. The copy scan reported PASS; no new package or animation was added.

## Verification state

- `npm test`: 18 passed. `npm run build` and `npm run lint`: passed.
- Rendered screenshot/Awesome scoring, Playwright, axe, manual keyboard journey, screen-reader, 200% zoom, and color-contrast tooling are **UNVERIFIED**. No screenshot or visual score is claimed.
- Human independent review and Product Owner acceptance remain pending. The user asked that PRs stay pending.
