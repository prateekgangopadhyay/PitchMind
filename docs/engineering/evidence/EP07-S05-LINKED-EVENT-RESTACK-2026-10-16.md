# EP07-S05 linked-event pagination restack — 2026-10-16

## Scope

The current-main replay page loads the first page of canonical events. An evidence link may target an event after that page, leaving its focus anchor unavailable. This restack adds bounded cursor traversal for a linked event while retaining the current selected match, elapsed time, projection revision, canonical revision, and sequence watermark. The target event is appended to the visible page only after its continuation page passes those context and ordering checks.

The traversal stops after 512 pages, rejects missing/repeated cursors, empty continuation pages, duplicate or out-of-order sequence values, and context mismatches, and reports an incomplete search separately from an event that is absent. The existing abort controller cancels continuation requests when selection changes or the page unmounts. Backend frame pagination and exact frame-ID lookup were already present on current main, so this restack contains no API or database change.

## Verification

- Focused linked-event pagination regressions: 3 passed, covering a target beyond the first page, failed continuation, and abort on selected-time change/unmount.
- Full frontend Vitest suite: 100 passed across 9 files.
- Production build: passed with `tsc -b` and Vite; Vite emitted upstream SignalR annotation warnings that it removed.
- Lint: passed.
- Council operating-model validator: passed (12 epics, 59 stories, 28 Council skills; no cycles).
- `git diff --check`: passed.

## Status and limits

This is an implementation slice for EP07-S05, not story acceptance. Browser, keyboard/screen-reader, axe, hosted CI, approved-source comparison, and Product Owner review remain outstanding. No Azure resources or deployments were accessed or changed.
