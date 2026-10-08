# PitchMind — Premier League-inspired MVP UI mockups

**Status:** Visual concepts only; not functional, not implemented, and not proof of live data or a deployed product.  
**Created:** 2026-10-08  
**Format:** High-quality WebP, 1672 × 941 pixels, optimized for source-control size.

These seven screens illustrate the proposed PitchMind hackathon MVP. All football scenarios depict **synthetic match data in an English Premier League club context**, not actual EPL results, real-time broadcasts, or licensed EPL telemetry. Any depicted scores, standings, fixtures, statistics, tactical events, player assessments, timestamps and AI answers are **illustrative placeholder content** and may not be consistent across screens. All must be backed by canonical synthetic data before implementation.

| # | Screen | Product question | Mockup |
|---|---|---|---|
| 01 | Login / Welcome | How do I securely access PitchMind? | [View](01-login.webp) |
| 02 | Home / EPL Match Hub | What matters across EPL matches right now? | [View](02-home.webp) |
| 03 | Match / Intelligent Pitch | How is this match being played? | [View](03-match.webp) |
| 04 | Teams / Tactical Profile | How does this team play and what drives its performance? | [View](04-teams.webp) |
| 05 | Players / Player Intelligence | What is this player contributing? | [View](05-players.webp) |
| 06 | Insights / Tactical Explanations | Why is the match developing this way? | [View](06-insights.webp) |
| 07 | AI Analyst / Evidence-Grounded Chat | What do I want to understand, and why should I trust the explanation? | [View](07-ai-analyst.webp) |

## Design and implementation guidance

- **Football first:** Story → Insight → Why → Evidence → Visualisation → Data → Deep Analysis.
- **Shared match context:** The score, clock, possession, player positions, pitch overlay, events, and explanations must represent the same selected timestamp.
- **Trust hierarchy:** Clearly distinguish observed fact, deterministic derivation, evidence-supported interpretation, hypothesis, and simulation. Do not label a tactical interpretation as a directly observed fact.
- **Evidence:** Every meaningful insight needs inspectable supporting event IDs, timestamps, player/ball positions, metrics, and/or sequence replay.
- **AI boundaries:** Microsoft Foundry can explain verified MatchIntelligence but cannot manufacture football events, causality, statistics, or tactical intent. Preserve a deterministic fallback if AI is unavailable.
- **Access and usability:** Implement accessible forms, keyboard navigation, responsive layouts, and secure authentication/authorization according to the architecture. Mockup controls are not yet functional.
- **Environment policy:** Local Docker for development; DEV/STAGE/PROD Azure services only.
- **Hackathon rule:** Use synthetic football-realistic data, not real Premier League match telemetry.
- **Visual rights:** Generic visual branding/illustrative depictions are not substitutes for licensed club crests, broadcast footage, official league marks, or player photography. Review any public-facing assets before launch.

The mockups are **design references, not pixel-perfect specifications**. Engineering and product review must validate tactical definitions, metric labels, data provenance, accessibility, and design consistency prior to acceptance.

See [MVP Backlog](../backlog/MVP-BACKLOG.md) and [Detailed Epic and Development Stories](../backlog/HACKATHON-MVP-EPICS.md) for delivery scope.

## From visual concepts to tested web pages
Frontend stories must follow [Taste → Vercel Web Design Guidelines → Awesome Design review](../design/FRONTEND-DESIGN-DELIVERY.md), comparing actual desktop/tablet/mobile browser render to the concept and recording observed positive, negative and degraded states. The seven designs are **not** source-data records. [Design system](../design/PITCHMIND-DESIGN-SYSTEM.md) and [upstream provenance](../design/VENDOR-SOURCES.md) guide implementation without arbitrary framework swaps or unsupported football claims.
