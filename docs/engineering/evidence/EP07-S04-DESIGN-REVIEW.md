# EP07-S04 local design review

**Date:** 2026-10-11
**Review type:** implementation-side visual and accessibility-tree inspection; independent review and Product Owner acceptance are pending.

## Preview setup

The match page was rendered locally at a selected time of 55:09 in period 2 using a temporary Vite-only visual fixture. The fixture used generic Home/Away labels and made-up player IDs and positions. It was not taken from the candidate JSON, PostgreSQL, or any football match. The mock middleware/configuration lived under `/private/tmp` and is not part of the application or PR. A screenshot was inspected in the browser but was not retained in the repository.

## Findings and changes

- The scoreboard, selected-time control, pitch and event list all displayed 55:09. The selected goal button exposed its time, sequence and period in the accessibility tree.
- The observed pitch included a ball marker, team-coloured player markers, dimensions and the team-direction statement. The visual review showed that full player IDs crowded the pitch, so the markers now use numbers and the visible position table maps each number to its player/team ID and coordinates.
- The position table is open by default and includes a caption, column headings and one row per position. This gives a nonvisual equivalent without relying on colour alone.
- The accessible position table now also includes the recorded ball X/Y coordinates, alongside player IDs and positions.
- Missing insights and canonical key moments rendered as explicit empty/unavailable states; the preview did not substitute demo claims.
- A fresh local browser inspection after the accessibility changes showed a named pitch graphic, a polite live description, the permanent captioned position table, and a native keyboard-accessible selected-time slider and event button. Pressing Tab moved focus to the skip-to-main link. This establishes a keyboard entry point and observed accessibility-tree content, not a complete keyboard-only journey or screen-reader acceptance.
- Unit coverage now checks reduced-motion behavior: when the media preference is enabled, autoplay is suppressed while manual time selection remains available. A manual browser/system preference check was not performed.
- Current local code checks: frontend tests 16/16, production build, lint, solution Release build (0 warnings/errors), solution tests 110 passed/0 failed/19 PostgreSQL tests skipped, operating-model validation, design-skill self-test, and `git diff --check` passed. In a separate disposable local PostgreSQL database, the focused tracking-frame lineage and v2-to-v4 migration tests passed 2/2, then that database was dropped.

## Limits still open

- This was a desktop browser inspection only. No narrow viewport, 200% zoom, complete keyboard-only journey, actual screen reader, axe/WCAG contrast tool or manual reduced-motion browser check was executed. The current automated tests cover event selection, play/pause state, exact-time frame display, direction orientation, empty frame response, and reduced-motion autoplay suppression.
- A populated candidate-feed canonical pitch was not demonstrated. The repository has no tracking-frame admission writer. Migration v4 leaves legacy rows without a recorded canonical admission revision unserved; new admission must write that lineage. The focused reader/migration test used generic fabricated test records, not source JSON. The candidate dataset remains structurally profiled; coordinate units, player metrics, football realism and canonical mappings are still unapproved.
- The current canonical API limits selected time to 5,400 seconds while the candidate source reaches 5,761 seconds (96:01). The source cannot be replayed to full time until the canonical API/time contract is revised and verified by its accountable workstream.
- Independent technical review and Product Owner acceptance remain pending. This design inspection is not acceptance evidence.

## PR #57 follow-up — selected-time slider target

**Result:** Partial verification; EP07-S04 remains unverified and unaccepted.

**Scope:** The selected-time range input in the canonical Match screen. Its UA-rendered track had no minimum hit-area size. This change sets a 44px minimum height while preserving the native range control, focus styling, `aria-valuetext`, and selected-time behavior.

**Code:** `frontend/src/styles.css` and `frontend/e2e/selected-time-accessibility.spec.ts`.

**Browser evidence:** [320px Chromium screenshot](assets/ep07-s04-mobile-slider-after.png). The page uses explicitly synthetic test-only API responses and does not demonstrate source-feed admission or canonical football semantics.

### Taste pass

- Kept the existing React/Vite/CSS design and emerald selected-time accent.
- Enlarged the control's hit area without adding visual decoration or animation. Added Playwright only as development/test tooling; no runtime dependency changed.
- The page retains its existing responsive layout and selected-time context.

### Web Design Guidelines audit

Fetched the official raw guideline document on 2026-10-12 from [Vercel Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md). The narrow change keeps the native range control keyboard-operable and visibly focused while enlarging its mobile hit area. `touch-action: pan-y` preserves page scrolling while horizontal slider gestures control the range.

No unrelated findings were audited as part of this bounded CSS change. The existing label and native keyboard semantics were exercised by Playwright; no screen-reader or WCAG conformance claim is made.

### Awesome Design critique

| Area | Assessment | Evidence or limit |
|---|---|---|
| Identity | Strong | Screenshot shows existing PitchMind wordmark and dark navy/emerald style. |
| Hierarchy | Strong | Match heading, score and selected clock stay visible above the slider. |
| Typography | Strong | Selected match clock is readable and remains explicit at 320px. |
| Layout | Strong | No horizontal overflow at 320px in Chromium. |
| Color and contrast | Weak / unmeasured | This change did not measure color contrast. |
| Interaction | Strong for tested path | Native range semantics, 44px bounding height and keyboard increment passed. Touch hardware was not sampled. |
| System | Strong for tested path | Responsive page and selected-time scoreboard stay synchronized in the mocked browser journey. |
| Product specificity | Strong | Slider label says “Selected match time”; page provenance label remains visible. |

The premium rubric is scoped to this small control change, not the full screen: Taste 31/40, Tech 27/30, Process 16/20, Output 7/10 (81/100). The lower contrast and Output scores reflect unmeasured contrast, no real touch hardware, and the absence of canonical-feed evidence. This score does not approve the overall screen or waive WCAG requirements.

AI UI quality rubric (review-process fit only): taste 4/5, clarity 4/5, consistency 4/5, interaction 4/5, reuse 3/5, cross-tool compatibility 4/5, audit value 4/5 (27/35). It is not a usability score.

### Executed tests and remaining evidence

- `npm run test:e2e`: 1/1 passed in Chromium; asserts 44px bounding height, keyboard time increment, synchronized scoreboard, and 320px no-overflow.
- `npm test -- --run`: 19/19 passed.
- `npm run build`, `npm run lint`, `node scripts/validate_operating_model.mjs`, `node scripts/test_design_skills.mjs`, and `git diff --check`: passed.
- Not run: axe, screen-reader sampling, 200% zoom, manual touch-device evaluation, measured contrast, canonical-feed journey, independent human review, Product Owner acceptance.

No architecture, API, domain, database, canonical data, or Azure behavior changed. No Azure resource or deployment action was performed.
