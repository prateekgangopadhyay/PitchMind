# PitchMind Brand Identity System

**Version:** 1.0 concept system  
**Scope:** PitchMind football analytics application  
**Status:** Proposed identity; legal clearance and commercial launch approval are pending.  
**Decision:** Recommend Concept A, “Evidence Path P,” for product use and further clearance.

## 1. Brand strategy

### Positioning

PitchMind helps people understand football through evidence: match events, player and team movement, tactical context, and carefully bounded explanations. It makes complex match information legible without presenting inference as fact.

### Audience

- **Analyst or coach:** needs to inspect a tactical mechanism, trace it to time-stamped events, and communicate it clearly.
- **Dedicated supporter:** wants to understand why a match changed, with plain language and a replayable evidence trail.
- **Product or data operator:** needs provenance, source limitations, canonical identifiers, and honest unavailable states.

### Values and personality

Evidence before certainty; football fluency without jargon; clarity over spectacle; disciplined, curious, precise, calm, and confident. Explanations should distinguish observed facts, derived measures, and model interpretation.

### Visual principles

1. **A path with evidence points:** use a short sequence to show how observations support an explanation.
2. **One focal football question:** keep the match or tactical insight visually primary.
3. **Restrained signal color:** emerald identifies a selected path or action; team colors and values remain data-driven.
4. **Readable at working size:** marks, charts, and labels must survive compact UI placement.
5. **Clear provenance:** visual polish never implies greater data certainty than the source supports.

## 2. Three concepts and decision

Scores use the requested 1-10 scale. Weighted total is the sum of `score × weight`, divided by 100. These are design judgments, not trademark or user research results.

| Criterion | Weight | A: Evidence Path P | B: PM Relay | C: Tactical Index |
|---|---:|---:|---:|---:|
| Distinctiveness and originality | 20% | 7 | 8 | 6 |
| Premium perception | 15% | 8 | 8 | 7 |
| Memorability | 15% | 8 | 7 | 6 |
| Football relevance | 10% | 8 | 6 | 9 |
| Scalability and production | 15% | 9 | 7 | 8 |
| Product/channel versatility | 10% | 9 | 8 | 7 |
| Timelessness | 10% | 8 | 8 | 7 |
| International adaptability | 5% | 9 | 9 | 9 |
| **Weighted result / 10** | **100%** | **8.10** | **7.55** | **7.10** |

### A — Evidence Path P (recommended)

**Idea:** A custom P-shaped frame contains a three-point route and a directional terminal. It joins PitchMind's initial with the product's core behavior: follow an explanation through evidence. The intentionally open P shape leaves visual space for forward reading.

**Geometry:** compact monoline P with rounded ends; three circular evidence nodes; a short linked path and arrow. The delivered SVG is editable and uses one flat color. At very small sizes, use the simplified symbol or wordmark alone.

**Type and color:** restrained geometric sans wordmark, emerald signal on midnight navy; dark-ink mark for light surfaces.

**Strengths:** connects product name and evidence trail; legible in the current header; one-color friendly; works as an app icon. **Limitations:** P plus connected nodes is a familiar analytics vocabulary; registry and marketplace review remain necessary.

**Best uses:** app header, favicon/app tile, analyst reports, insight cards, social avatar.

### B — PM Relay

**Idea:** A bespoke P/M ligature where the P counter hands off into a rising M stroke, suggesting a sequence moving from observation to interpretation.

**Geometry:** two shared vertical stems, a compact bowl, and one ascending diagonal. The relationship must be designed as one drawing, not overlapping initials.

**Type and color:** custom-drawn initials with a neutral wordmark; graphite and warm white, with emerald only for active evidence.

**Strengths:** potentially ownable and premium; good embroidered or stamped monogram. **Limitations:** “PM” is ambiguous outside the full name; more refinement is needed for small screens.

**Best uses:** report cover, merchandise concept, account avatar, product badge.

### C — Tactical Index

**Idea:** An abstract pitch-map tile with a single highlighted cell and a deliberate route interruption, reflecting spatial analysis.

**Geometry:** asymmetrical 3×3 modular grid; one active cell; one connecting cut. No literal pitch outline or ball illustration.

**Type and color:** compact grotesk wordmark; ink and bone with a deep-green analytical accent.

**Strengths:** immediate spatial-analysis cue; easy to extend into charts and patterns. **Limitations:** risks resembling generic dashboard or field-grid iconography; weakest name linkage and recognition at tiny sizes.

**Best uses:** analytical section dividers, report graphics, heatmap legend containers.

**Selection:** A leads because it balances memorability, product semantics, tiny-size behavior, and broad UI/report application. The score is provisional; no external distinctiveness study or legal clearance has been completed.

## 3. Logo family and placement

The production-oriented files are in `frontend/public/brand/`:

- `pitchmind-symbol.svg`: emerald symbol for dark backgrounds.
- `pitchmind-symbol-mono.svg`: monochrome symbol using `currentColor`.
- `pitchmind-lockup-dark.svg`: symbol + wordmark for dark surfaces.
- `pitchmind-lockup-light.svg`: ink wordmark and dark-green symbol for light surfaces.

The `P` is the monogram. The compact symbol is the app mark. The lockups are compositions of a vector mark and editable SVG text (font substitution applies where Inter is unavailable).

### Clear space and minimum size

- Reserve clear space on all sides equal to the height of one evidence node (`x`).
- Digital symbol: 20 px minimum; recommended 28-40 px.
- Digital lockup: 120 px wide minimum; recommended 160 px or wider.
- Print symbol: 6 mm minimum, subject to the reproduction test of the selected process.
- For embroidery, use the supplied simplified single-color P outline; test stitch width and node separation with the manufacturer before production.

### Color variants and incorrect use

Use the emerald mark on midnight or sufficiently dark surfaces, and dark green on bone or white. Use one-color black, white, or currentColor where color production is unavailable. Do not recolor individual nodes to suggest teams, stretch the mark, add shadows or gradients, enclose it in a generic shield, rotate it, or add an unapproved tagline.

## 4. Color system

| Color | HEX | RGB | Approx. CMYK* | Role |
|---|---|---|---|---|
| Midnight | `#07131C` | 7, 19, 28 | 75, 32, 0, 89 | Primary canvas, dark wordmark |
| Signal Emerald | `#4AEFAC` | 74, 239, 172 | 69, 0, 28, 6 | Selected evidence, primary mark on dark |
| Warm Ice | `#EDF6F8` | 237, 246, 248 | 4, 1, 0, 3 | Main text and reversed wordmark |
| Deep Surface | `#0C1E28` | 12, 30, 40 | 70, 25, 0, 84 | Panels and raised content |
| Structure Graphite | `#243E4C` | 36, 62, 76 | 53, 18, 0, 70 | Borders, secondary structure |
| Deep Green (light surface) | `#147A56` | 20, 122, 86 | 84, 0, 30, 52 | Mark on light backgrounds |

**Alternative palette concept:** Ink `#111B22`, Bone `#F2F0E8`, Pine `#145A45`, and chartreuse `#B9F26B`. Treat it as an exploration option; product token changes require a separate UI review.

*CMYK values are formula conversions for initial discussion, not press profiles. Confirm with the printer and paper/garment substrate. Do not rely on metallic ink or gradients.

### Digital contrast

Calculated WCAG contrast: Signal Emerald on Midnight is approximately **12.72:1**; Warm Ice on Midnight approximately **17.10:1**; Deep Green on Warm Ice approximately **4.85:1**. Emerald on white is approximately **1.48:1**, so do not use it for small text or a thin mark on a light background. Team colors should remain distinct from brand colors and should never carry meaning by hue alone.

## 5. Typography

- **Primary/digital:** platform system sans stack (`Inter`, `Avenir Next`, `Helvetica Neue`, `Arial`, sans-serif). Use 600-700 for wordmark/UI headings and 400-500 for reading copy.
- **Data/utility:** same family with tabular numerals; use 500-600 for clocks, event IDs, and metric labels.
- **Display:** use the primary family at 700-800, tight tracking (about -0.02em), sentence case. Do not set entire product screens in all caps.
- **Wordmark:** editable text in SVG with 700 weight and modest negative tracking; convert to outlines only in a vendor handoff if the chosen font license permits and the delivery pipeline is recorded.

No font files are bundled. System fonts are used under each platform's own license; verify the selected font and redistribution rights before embedding or distributing outlined artwork. No paid font license has been purchased or checked in this work.

## 6. Supporting graphic language

- Derive graphic routes from three evidence nodes and short connecting segments; use only when they map to real content.
- Use a 4/8/12/16/24/32 px spacing scale, 8 px alignment rhythm, and 1 px structural rules.
- Use generous negative space, precise data labels, and one highlighted sequence per visual.
- Charts must retain axis labels, source/time context, uncertainty, and a non-color distinction.
- Photography direction, if used, should favor honest training or analysis environments, natural material, and purposeful cropped movement; avoid fake club badges, invented match scenes, and unsupported athlete stories.
- Iconography should use simple 1.5-2 px strokes, rounded joins, consistent viewboxes, and semantic labels.
- UI motion should be short and interruptible; respect reduced-motion settings. The brand symbol itself is static.

## 7. Application system

All examples are conceptual placement guidance except the installed header/login/favicon asset use. They are not claims that garment or physical products exist.

| Touchpoint | Guidance |
|---|---|
| Header and login | Small emerald symbol, stable wordmark, visible focus and sufficient clear space. |
| Match analysis | Keep symbol in navigation; use the evidence path motif only for a real event sequence. |
| AI Analyst | Brand the explanation surface, but show cited facts, sources, clock and confidence/limitations separately. |
| Report/PDF | Lockup on cover; metadata and provenance in footer; do not use the mark as a data-source badge. |
| Social avatar | Symbol alone on Midnight; maintain the one-color version for small crop. |
| App icon | Center symbol in a square safe area; no invented surrounding app-store badge. |
| Optional apparel/merch | Concept only: small tonal symbol at chest/hem; no club marks or manufacturer claim. |

## 8. Voice and messaging

Use concise, evidence-aware language: “The right-side overload created a free receiver” only when event/position data supports it. Prefer “The sequence suggests…” for interpretation. Explain what is observed, what is inferred, the time window, and what is unavailable. Avoid “AI knows,” “guaranteed,” “real-time” or performance claims unless product evidence supports them. No slogan is proposed in this identity concept.

## 9. Technical production and handoff

SVGs are editable vector assets. The wordmark is SVG text, not outlined lettering. Assets are RGB and single-color variants are included; CMYK/spot-color/embroidery production masters have not been supplied. PNG exports at multiple resolutions and PDF vector master export remain planned handoff items, not claimed deliverables in this version.

### Naming convention

`pitchmind-{symbol|lockup}-{dark|light|mono}.{svg|png|pdf}`. Keep presentation mockups in a separate `mockups/` folder and label them conceptually.

### Clearance checklist

- Search trademark registries in target regions and relevant software/data/education classes with qualified counsel.
- Compare visual, phonetic, and conceptual similarity; document potentially conflicting marks.
- Check product name, domain, app-store, and social-handle availability.
- Confirm rights to all fonts and external images before production use.
- This guide does not claim legal clearance, registration, domain availability, user research, or distinctiveness certification.

## 10. Decisions and unresolved items

- PitchMind name and current product palette are treated as supplied product identity inputs.
- No slogan is proposed in this concept.
- Concept A is the proposed direction; owner approval and independent design review are pending.
- No trademark search, domain/social check, accessibility test of all application states, or customer research has been performed as part of this identity proposal.
- The existing product uses synthetic EPL-context data; the identity must not imply official Premier League affiliation or live licensed data.
