---
name: impeccable
description: "Use the Impeccable frontend design methodology to shape, critique, audit, polish and distill PitchMind React/Vite interfaces while preserving source truth and accessibility."
---

# Impeccable for PitchMind — reviewed advisory adapter

**This is a project-authored, self-contained adapter**, **not** a copy or installation of Paul Bakaus's full Impeccable CLI, binary, hooks, generated skills, command references or design detector. Upstream source: `pbakaus/impeccable` at commit `c63b8dd7dd5a5f3c32eaabe1af76d0694f863ed2` (Apache-2.0). See `docs/design/VENDOR-SOURCES.md` and [official Impeccable](https://github.com/pbakaus/impeccable).

## Triggers
Use when EP07 / EP08-S06 or related UI work needs deliberate information hierarchy, layout, visual identity, typography, interaction audit, purposeful motion, responsive polish, a calmer/simpler design or removal of generic AI-generated UI patterns. Select a **specific component/screen** and an exact existing story ID; avoid redesigning the whole app to satisfy a generic checklist.

## Working modes (apply without external tooling)

- **Shape / critique**: describe the target user's single question, current TSX/CSS and screenshot/viewport, existing mockup, and one memorable evidence-led focal region. Identify the top three verifiable usability or visual defects before any changes.
- **Audit**: inspect real code and browser results for hierarchy, legibility, spacing, alignment, responsive behavior, keyboard focus, contrast, loading/stale/error/empty states, truthful data labeling and reduced motion. Produce `file:line — issue — impact — proposed fix` and explicitly mark unavailable screenshots/browser checks `UNVERIFIED`.
- **Polish / distill**: make the smallest edits that improve clarity or reduce nested cards, generic marketing language, arbitrary gradients, excessive shadows, misleading controls and visual clutter. Preserve native semantic controls, network data contracts and design tokens.
- **Animate only when needed**: use calm, interruptible CSS opacity/transform transitions for state clarification, including reduced-motion override and nonanimated keyboard equivalence; coordinate with Emil.
- **Verify**: retest React functionality and selected-time/revision consistency, responsive widths, keyboard/touch, `npm test`, `npm run build`, `npm run lint`, screenshots and actual accessibility tools **when available**. Never report an unrun step as passing.

## Existing product overrides

PitchMind's `docs/design/PITCHMIND-DESIGN-SYSTEM.md`, seven screen contracts, current API and football provenance supersede upstream generic design recommendations. Keep **React 19 / Vite / TypeScript / existing CSS**, dark navy + emerald, meaningful hierarchy, one selected match time and canonical revision, sparse-tracking honesty, WCAG 2.2 AA, synthetic labels, evidence drill-down and lawful branding. Never add unapproved third-party scripts, fonts, icons, CSS frameworks or animation runtime. No Azure/DEV mutation or deployment without exact owner approval.

## Optional full upstream skill / CLI

For advanced official `/impeccable` commands or deterministic detector rules, an operator may independently **review and approve** the upstream installer and its native executable/hook behavior before invoking `npx impeccable install --providers=github` in a disposable *trusted local checkout*. **Do not run it automatically**, overwrite this adapter or add hooks to this repository implicitly; it may generate `.github/skills/impeccable` and provider hooks, so review the diff and treat such installation as a separate PR. The official project documents `/impeccable audit`, `polish`, `critique` and `distill` once the full tool is installed.

## Handoff

Report the target screen/story, baseline screenshot and commit, specific before/after findings, actual code changes, tests executed, relevant Emil motion/a11y critique, reviewer, unresolved concerns and whether the *official upstream CLI* was installed or **not installed**. This skill alone never confers review, football approval, acceptance or merge authorization.
