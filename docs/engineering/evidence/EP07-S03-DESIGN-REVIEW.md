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
- Follow-up commit `3e88b77ca8a9f989d1b573bda526758689773ec6` maps the exact `CANONICAL_KEY_MOMENTS_UNAVAILABLE` 503 code to a cautious no-projection message; unrelated 503 responses use generic unavailable copy. The component test covers both codes.
- On the exact code tree `3e88b77ca8a9f989d1b573bda526758689773ec6`, `npm ci --offline` installed 224 packages and reported 0 vulnerabilities; `npm test` passed 14/14; `npm run build`, `npm run lint`, operating-model validation, design-skill self-test, S03 copy scan, and `git diff --check` passed.
- The browser preview described above was performed before restacking and is historical; no browser or screenshot inspection was repeated on the current-parent tree. Accessibility/contrast/axe/screen-reader and mobile evidence remain unverified.

## Pending owners and blockers

- Canonical API/data owner: version a successful Key Moments response and persist a policy-reviewed projection before the UI can render ranked moments.
- Product/Football owner: approve concise narrative and event relevance semantics; frontend will not invent a leading story.
- QA: execute the selected-time golden journey at 55:08/55:09 with API-backed data and retain viewport screenshots.
- Independent Frontend/UX and Accessibility reviews: pending; Product Owner acceptance: pending.
- Story remains unaccepted; no Azure calls or changes were made.

## Selected-time saved-insight panel — local follow-up

**Change scope:** local branch `codex/ep07-s03-selected-insight`, based on exact open PR #69 head `600fd5cebba490111d8d9559ce8a641b041f15ff`. The panel renders the persisted insight with the highest validated `throughSequence` from the already context-validated insights page. It preserves the API `truthClass` and `summary`, labels the selected clock, and links to the same match/time/revision. It does not rank this insight as a Key Moment, rewrite it, or claim live updates. Blank summaries stay out of the panel. The existing Key Moments unavailable state remains explicit.

### Taste / interaction decision

The page's score and clock remain first. The new compact explanation panel sits immediately after them, before source details and evidence lists, giving the match question one visible answer without adding motion, data, or a new dependency. Stored claim copy and classification remain paired; the evidence link remains a native anchor. Long source summaries wrap inside the panel.

### Vercel Web Interface Guidelines review

Official guideline snapshot retrieved from `https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md` on 2026-10-12.

- `frontend/src/CanonicalMatchPage.tsx:89-98` — heading level follows the page H1; status class and source summary remain text; empty state is explicit; evidence navigation is a native anchor with selected context — code review pass.
- `frontend/src/CanonicalMatchPage.tsx:70-74` — explanation selection is derived only from validated insights and chooses the greatest nonnegative sequence cutoff — code review pass; positive DOM regression below.
- `frontend/src/styles.css:150` — long insight summaries wrap to avoid horizontal overflow — code review pass; narrow viewport not rendered.
- Browser keyboard/focus, contrast, mobile reflow, axe and screen-reader checks — unverified. A local Vite server started, but the CUA in-app browser refuses visible tabs from this subagent thread; no screenshot was captured.

### Awesome Design review outcome

The three rubric scores and screenshot-grounded anti-slop verdict are **UNVERIFIED** because the changed page could not be rendered in a browser screenshot during this subagent run. No premium score is assigned. Current evaluation is limited to source-level hierarchy, semantic structure, text wrapping, and the DOM regression; these are not visual acceptance evidence.

### Checks actually performed on this local tree

- `cd frontend && npm test -- --run` — 34 passed, 0 failed.
- `cd frontend && npm run build` — passed.
- `cd frontend && npm run lint` — passed.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, 3 design skills, 0 cycles, 2 approval gates).
- `node scripts/test_design_skills.mjs` — passed.
- `node scripts/design-slop-scan.mjs frontend/src/CanonicalMatchPage.tsx` — passed, 0 warnings.
- `git diff --check` — passed.

Result: **CHANGES REQUIRED / UNVERIFIED**. This improves the explanation path for persisted insights, but does not complete ranked Key Moments, realtime updates, the full 55:00/55:09 golden journey, browser/accessibility evidence, human independent review, or Product Owner acceptance. No Azure actions were performed.
