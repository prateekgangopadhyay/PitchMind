# EP07-S03 Match story and Key Moments review

**Review scope:** Selected-time Match page, original browser preview on `codex/ep07-s03-match-story-moments`; local restack verification on `codex/ep07-s03-restack-current`

**Review date:** 2026-10-10

**Result:** CHANGES REQUIRED — selected-time safeguards and page states are implemented; canonical Key Moments and live story requirements are not complete.

## Evidence and data boundary

- Visual reference: [`docs/mockups/03-match.webp`](../../mockups/03-match.webp), conceptual only. Its clubs, score, tactical claims, player positions and Key Moments were not copied.
- Rendered preview: local browser at 896×1024 with a disposable loopback API fixture, match `11111111-1111-1111-1111-111111111111`, selected 55:09, revision 4. The preview fixture was fabricated only for local UI inspection; it is not a candidate-feed record or PostgreSQL result. The image was inspected during this review but not saved as a repository artifact. Mobile, 375px, 768px and 1440px screenshots remain unverified.
- Vercel Web Interface Guidelines were freshly retrieved on 2026-10-10 from the official repository; SHA-256 `d246b026f4f29b5823a9cc857f9edf3d2507002e055e32040cadeaf3b38e0234`.
- The canonical Key Moments OpenAPI operation currently specifies typed 503 only. It has no successful response schema or persisted projection. The separate synthetic demo Key Moments service is not used.

## Taste and visual review

The selected-time clock and score form the visual anchor; provenance and revision are inspectable below them. The event timeline keeps canonical time and sequence separate from insight truth class. At the inspected viewport, the Key Moments unavailable state spans the timeline width instead of appearing as an empty card. The page intentionally omits pitch positions because the canonical contract does not return frames. It is less complete than the mockup because names, validated story narrative, pitch data, and persisted Key Moments are unavailable.

## Awesome Design rubrics

### Anti-slop review (Strong / Weak)

| Dimension | Assessment | Evidence |
|---|---|---|
| Identity | Strong | Dark navy, emerald and PitchMind shell match the documented design system. |
| Hierarchy | Strong | Score/clock lead; event and insight panels are secondary. |
| Typography | Strong | Clock/score use tabular numerals; event timestamps are visually distinct. |
| Spacing and layout | Strong at inspected 896×1024 | Event, insight and Key Moments sections have separate panels and readable gaps. Other breakpoints were not visually tested. |
| Color and contrast | Weak / unverified | Truth class and event labels include text; contrast ratios were not measured. |
| Interaction polish | Strong for implemented controls | Selected-time slider is labeled and exposes clock text; evidence links are native links. No live-update interaction exists. |
| System thinking | Strong | Requests are cancelled on context change; response revision/time/sequence and row-level cutoffs are checked; unavailable Key Moments is explicit. |
| Product specificity | Weak | Canonical story/ranked moments and pitch are absent; only persisted insight summaries can be displayed. |

### AI UI quality rubric (1–5; process-oriented, not usability certification)

| Taste | Clarity | Consistency | Interaction | Reuse | Cross-tool compatibility | Audit value | Total |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 4 | 4 | 5 | 4 | 4 | 3 | 5 | 29/35 |

### Premium gate (guidance only)

| Taste | Tech | Process | Output | Total |
|---:|---:|---:|---:|---:|
| 29/40 | 24/30 | 14/20 | 5/10 | 72/100 |

No score can waive the missing Key Moments contract/projection, full mobile/contrast evidence, independent review or Product Owner acceptance.

## Vercel Web Interface Guidelines findings

- `frontend/src/CanonicalMatchPage.tsx:31-40` — query Key Moments at the exact selected time/revision; typed 503 is shown without demo fallback — pass for degraded state; successful response remains uncontracted.
- `frontend/src/CanonicalMatchPage.tsx:43-54` — validate match/revision/time/sequence and reject later event/insight rows before rendering — pass in adversarial unit test.
- `frontend/src/CanonicalMatchPage.tsx:83-88` — range input has an associated label and `aria-valuetext` clock — code inspected; keyboard and assistive-technology interaction remains unverified.
- `frontend/src/CanonicalMatchPage.tsx:95-99` and `frontend/src/styles.css:125-132` — semantic ordered timeline, explicit event labels, time/sequence separation, card/goal marker text — unit tested; preview used a shot event only.
- `frontend/src/CanonicalMatchPage.tsx:102-110` — evidence references use links with match/time/revision context — pass by DOM inspection; no full screen-reader run.
- `frontend/src/styles.css:133-140` — tablet/touch layout and slider stacking rules are defined; no 375px or touch-device screenshot was available.
- Contrast sampling, axe, WCAG 2.2 AA, screen-reader testing, and cross-browser review were not performed.

## Checks actually performed

- `npm test` — 10 passed, 0 failed.
- `npm run build` — passed.
- `npm run lint` — passed.
- Match browser preview — rendered summary, selected 55:09, revision 4, one persisted insight and explicit Key Moments unavailable state from the disposable API fixture.
- Negative-time test — a future sequence/event and an insight beyond the projection watermark cause the response to be rejected without rendering their content.
- Independent UX/Accessibility review, Product Owner acceptance, mobile/tablet/contrast/axe/screen-reader checks and live PostgreSQL verification — not performed.

## Current-parent restack verification

- The existing PR head `95083fc6a467723de5b999c08b479b767ee584b8` descended from an older EP07-S02 candidate (`3d5cc6499aaf72356653f279a9314687c8aba69c`). The current PR #54 base is `c144b4fe45f2388e6c8f78ff443967728a1cd234`; GitHub reported PR #55 `mergeable=false` / `dirty`, with dependency changes across 71 files. The story-only delta was reapplied onto the exact current parent in an isolated local clone.
- Restacked code commit: `8e3b398f24eaf490fbfde23d76867fd608238c2e`, parent `c144b4fe45f2388e6c8f78ff443967728a1cd234`.
- On that code tree, `npm ci --offline` installed 224 packages and reported 0 vulnerabilities; `npm test` passed 13/13; `npm run build`, `npm run lint`, operating-model validation, design-skill self-test, S03 copy scan, and `git diff --check` passed.
- The browser preview described above was performed before restacking and is historical; no browser or screenshot inspection was repeated on the current-parent tree. Accessibility/contrast/axe/screen-reader and mobile evidence remain unverified.

## Pending owners and blockers

- Canonical API/data owner: version a successful Key Moments response and persist a policy-reviewed projection before the UI can render ranked moments.
- Product/Football owner: approve concise narrative and event relevance semantics; frontend will not invent a leading story.
- QA: execute the selected-time golden journey at 55:08/55:09 with API-backed data and retain viewport screenshots.
- Independent Frontend/UX and Accessibility reviews: pending; Product Owner acceptance: pending.
- Story remains unaccepted; no Azure calls or changes were made.
