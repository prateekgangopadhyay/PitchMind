# Baseline frontend audit using Taste, Vercel and Awesome Design

**Scope:** source inspection of `frontend/src/App.tsx`, `frontend/src/styles.css`, `frontend/index.html`, current backend/frontend contracts, and seven committed mockup descriptions; application browser renders were **not** captured. Code-review baseline only (2026-10-09); any design score is **UNVERIFIED** until actual screenshots and browser/assistive-tech tests.

## Taste Skill — implementation intent and smallest safe improvement
- Existing app uses dark navy, emerald, source-fed score/match data, observed SVG players and one selected-time replay context. Preserve these strengths and existing React/Vite/CSS rather than rewrite into Next.js/Tailwind.
- Prior source CSS is minified as a single line; its `:root` had no reusable semantic tokens. Added explicit visual token **reference variables** without altering the established palette.
- Score/clock numerals previously used proportional fonts; added `font-variant-numeric: tabular-nums`.
- Meaningful focus states and restrained motion are prioritized above animated effects. No hover physics or perpetual animations were added.

## Vercel Web Interface Guidelines — source findings

| Finding / location before change | Resolution in this PR | Verification status |
|---|---|---|
| `frontend/src/App.tsx:58–59` — no visible keyboard skip to the application main landmark | Added first-focus skip link targeting `main#main-content` and CSS reveal when focused | Source change and regression assertion added; manual keyboard test **unverified** |
| `frontend/index.html:1` and `frontend/src/styles.css:1` — dark theme lacks explicit `theme-color` and `color-scheme` | Added dark theme meta and CSS color-scheme declaration | Source inspected; native controls/browser rendering **unverified** |
| `frontend/src/styles.css:1` — large score/clock numeral presentation lacked tabular-nums | Added numeric tabular figures for score, clock, minute and bounds | Visual comparison **unverified** |
| `frontend/src/styles.css:1` — some compact mode/timeline buttons could miss mobile comfortable hit area | Added 44px minimum for button and select surfaces | Mobile touch/visual regression **unverified** |
| `frontend/src/styles.css:1` — reduced-motion rule only disabled smooth scrolling | Added blanket reduced-motion animation/transition reduction | Needs visual and actual OS setting test |
| `frontend/src/App.tsx:64` — pitch SVG has an ARIA label, but no equivalent inspectable player/ball data table | **Open:** EP07-S04 source-backed nonvisual view required | BLOCKER for full WCAG acceptance |
| `frontend/src/App.tsx:67–68` — match analyst and slider functional, but no full keyboard/screen-reader/real network E2E proof | **Open:** keyboard replay, focus return, accessible text and end-to-end testing | UNVERIFIED |
| `frontend/src/styles.css:1` — monolithic minified CSS complicates independent `file:line` reporting | **Open:** future EP07 CSS modularization/prettification with tests | Planned, not done |

**Freshness:** Reviewed current official Vercel Web Interface Guidelines retrieved on 2026-10-09 from its public `command.md` reference. This is a code-focused **partial** audit, not a full guideline or WCAG conformance report.

## Awesome Design rubric review (partial source-only)
- **Anti-slop rubric:** brand palette and football-specific content appear in source; hierarchy, composition, visual rhythm, responsive polish, interactions and consistency remain **NOT SCORED** because no browser screenshot or seven implemented routes were examined.
- **AI UI rubric:** resource-level scores **NOT SCORED**. Its seven categories are useful review prompts, not a proxy for conversion, accessibility or user satisfaction.
- **Premium generation gate:** **NOT SCORED**; missing actual screenshot inspection is a critical failure for claiming a premium-release pass. No "95/100" claim is made.
- **Copy quality instrument:** vendored source and self-test added; production UI copy needs targeted heuristic scan and human waiver review rather than blind editing of football terms.

## Follow-up acceptance by EP07/EP08
For each implemented route: render 1440, 768 and 375 widths; collect loading, empty, observed/derived, denied, stale, model-down and selected-time-before/after key event snapshots. Independently rerun latest Vercel rules with `file:line` findings, score Awesome rubrics from actual visuals, validate WCAG keyboard/AT/axe/contrast, and link real test artifact IDs to the existing story register.

**Deployment:** No Azure changes or DEV approvals are implied by design-skill use or these source modifications.
