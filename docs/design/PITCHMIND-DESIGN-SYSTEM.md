# PitchMind — Premium Web Design System

**Status:** Design specification for the seven committed mockups; not evidence that all seven screens have been implemented.  
**Authority:** [Mockups](../mockups/README.md) → [Product screen contracts](../engineering/FRONTEND-SCREEN-CONTRACTS.md) → [Current API](../api/API.md) → [28-role Council](../operating-model/COUNCIL-SKILL-REGISTRY.json) → [5 specialist design skills](DESIGN-SKILL-REGISTRY.json).

## Product principle

**Show what matters. Explain why it matters. Prove it with evidence. Reveal complexity progressively.**

PitchMind is a match-understanding product, not a benchmark dashboard, a decorative football poster, or an AI chat toy. Design order: **Story → Insight → Why → Evidence → Visualisation → Data → Advanced Analysis**. Each view has one primary question, an obvious next action, explainable uncertainty, and consistency with the selected clock.

## Implementation reality and scope

- Today: React 19 + Vite + TypeScript + existing `frontend/src/styles.css` with an offline match screen. No automatic switch to Next.js, Tailwind, RSC or motion libraries.
- Future: seven distinct routes: Login, Home, Match, Teams, Players, Insights and AI Analyst. Their 1672×941 WebP screenshots are conceptual **references**, never a substitute for functional controls, accessibility evidence or real-time verified data.
- Source: authorized **synthetic EPL-context** JSON → canonical backend → typed BFF; club/player names, match results, tracking and metrics **never hardcoded** in production C#/TSX. Do not present real Premier League live feed or actual player biometrics.
- Treat unavailable source fields explicitly: insufficient tracking → unavailable spatial claims, model unavailable → deterministic explanation fallback, no evidence → do not publish unsupported claims.

## Product-specific tokens (specification; implementation tracked by frontend stories)

| Token / role | Reference value | Use |
|---|---|---|
| `--pm-bg` | `#07131C` | Main application canvas; current CSS background |
| `--pm-surface` | `#0C1E28` | Analytical panels with deliberate boundaries |
| `--pm-surface-raised` | `#102733` | Interactive control and raised areas |
| `--pm-border` | `#243E4C` | Subtle structural borders |
| `--pm-text` | `#EDF6F8` | Main text and contrast authority |
| `--pm-text-secondary` | `#B3C6D1` | Supporting text; must pass contrast checks |
| `--pm-accent` | `#4AEFAC` | Main emerald action/focus/selected football context |
| `--pm-home-marker` | `#FA697B` | Example home-side point marker only; backend provides actual role |
| `--pm-away-marker` | `#6BA5FF` | Example away-side point marker only; never rely on hue alone |
| Spacing | 4/8/12/16/24/32 px | 8px rhythm with half-step for fine alignment |
| Radius | 6/10/12/16 px | Purposeful, not exaggerated, interactive vs panel hierarchy |
| Typography | System/modern neutral sans with tabular numerals for scores | No font asset redistribution; use safe fallbacks |
| Motion | 120–220ms for optional state transitions | Reduced-motion opt-out, no perpetual animation or physical-effects library by default |

These are **reference tokens**—they describe the existing direction. Actual CSS declarations and contrast checks must be implemented/tested in EP07 PRs; this file alone does not update the running UI.

## Screen-by-screen composition

| Screen | Primary question | One focal module | Necessary drill-down | State and evidence tests |
|---|---|---|---|---|
| Login | How do I sign in securely? | Identity panel + concise evidence-first promise | account and session help | labels, autocomplete, invalid auth, keyboard and focus |
| Home | What matters now? | Ranked fixtures with evidence-backed leading insight | open timestamped match | empty fixture, stale/unknown live head, simulated label |
| Match | How is this match played? | Pitch + observed timeline + annotated turning point | **Show me why** | same selected time for score/pitch/event/AI, no future leak |
| Teams | What drives team performance? | Tactical profile and supportable zones | definitions and source | insufficient coverage, nominal vs inferred shape |
| Players | What is a player contributing? | Source-attributed action/touch involvement | event references | no fabricated xT/xG/biometrics, unknown minutes |
| Insights | Why does this matter? | Mechanism → response → evidence chain | frame/sequence replay | supported vs observed language, missing frames |
| AI Analyst | What do I want to understand? | Typed conversation with cited match evidence | linked event IDs and replay | reject invented claim, timeout fallback, disabled tools |

## Hierarchy and anti-generic design

- Prefer one meaningful, asymmetric focal zone (pitch/map/evidence hero) to symmetric grids of eight identical cards.
- Large, readable match score and clock; precise typographic contrast, `font-variant-numeric: tabular-nums` and constrained explanatory prose line-length.
- Emerald signals action/selection, not every annotation. Red/blue team marker hue requires shape/label legend. Keep surfaces dark and distinct without gratuitous gradients.
- No gratuitous floating orbs, perpetual motion, decorative fake heatmaps, unexplained confidence scores, placeholder sports photography or fake interactive buttons.
- Evidence appears in inspectable sequence, not as a boast: show mechanism conditions, event IDs, selected time and explicit limitations.
- Keep high-density tactical features navigable: focus outlines, keyboard scrub, announcements throttled, reduced motion, modal focus return and nonvisual tabular pitch alternative.

## Visual quality testing

The [Taste](../../.github/skills/design-taste-frontend/SKILL.md) pass proposes layout and hierarchy. [Impeccable](../../.github/skills/impeccable/SKILL.md) audits/clarifies visual craft, and [Emil design engineering](../../.github/skills/emil-design-eng/SKILL.md) checks useful microinteraction and restrained motion. The [Vercel Web Design Guidelines](../../.github/skills/web-design-guidelines/SKILL.md) pass inspects actual TSX/CSS/HTML and records `file:line` accessibility/interaction findings. The [Awesome Design critic](../../.github/skills/awesome-design-audit/SKILL.md) applies three locally vendored rubrics with screenshot proof and scores. Score is *guidance*; factual accuracy, privacy/rights, WCAG access and release safety **cannot** be waived by a beautiful screenshot.

For every EP07/EP08-S06 PR: attach before/after screenshots at 1440px / 768px / 375px, keyboard/contrast and error-state evidence, copy score or issues, and network/mockup/selected-time reference. Reject untested claims. No auto Azure DEV deployment.
