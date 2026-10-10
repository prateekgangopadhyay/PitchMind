# EP07-S04 local design review

**Date:** 2026-10-11
**Review type:** implementation-side visual and accessibility-tree inspection; independent review and Product Owner acceptance are pending.

## Preview setup

The match page was rendered locally at a selected time of 55:09 in period 2 using a temporary Vite-only visual fixture. The fixture used generic Home/Away labels and made-up player IDs and positions. It was not taken from the candidate JSON, PostgreSQL, or any football match. The mock middleware/configuration lived under `/private/tmp` and is not part of the application or PR. A screenshot was inspected in the browser but was not retained in the repository.

## Findings and changes

- The scoreboard, selected-time control, pitch and event list all displayed 55:09. The selected goal button exposed its time, sequence and period in the accessibility tree.
- The observed pitch included a ball marker, team-coloured player markers, dimensions and the team-direction statement. The visual review showed that full player IDs crowded the pitch, so the markers now use numbers and the visible position table maps each number to its player/team ID and coordinates.
- The position table is open by default and includes a caption, column headings and one row per position. This gives a nonvisual equivalent without relying on colour alone.
- Missing insights and canonical key moments rendered as explicit empty/unavailable states; the preview did not substitute demo claims.
- A fresh local browser inspection after the accessibility changes showed a named pitch graphic, a polite live description, the permanent captioned position table, and a native keyboard-accessible selected-time slider and event button. Pressing Tab moved focus to the skip-to-main link. This establishes a keyboard entry point and observed accessibility-tree content, not a complete keyboard-only journey or screen-reader acceptance.
- Unit coverage now checks reduced-motion behavior: when the media preference is enabled, autoplay is suppressed while manual time selection remains available. A manual browser/system preference check was not performed.
- Current local code checks: frontend tests 16/16, production build, lint, solution Release build (0 warnings/errors), solution tests 110 passed/0 failed/19 PostgreSQL tests skipped, operating-model validation, design-skill self-test, and `git diff --check` passed. The PostgreSQL integration test for exact-time tracking frames is included in the skipped group because no `PITCHMIND_TEST_POSTGRES` database was configured.

## Limits still open

- This was a desktop browser inspection only. No narrow viewport, 200% zoom, complete keyboard-only journey, actual screen reader, axe/WCAG contrast tool or manual reduced-motion browser check was executed. The current automated tests cover event selection, play/pause state, exact-time frame display, direction orientation, empty frame response, and reduced-motion autoplay suppression.
- A populated canonical PostgreSQL pitch was not demonstrated. The repository has no tracking-frame admission writer. The exact-time/watermark reader integration test passed against a temporary isolated PostgreSQL database; there is no persistent `PITCHMIND_TEST_POSTGRES` setting. The candidate dataset remains structurally profiled; coordinate units, player metrics, football realism and canonical mappings are still unapproved.
- The current canonical API limits selected time to 5,400 seconds while the candidate source reaches 5,761 seconds (96:01). The source cannot be replayed to full time until the canonical API/time contract is revised and verified by its accountable workstream.
- Independent technical review and Product Owner acceptance remain pending. This design inspection is not acceptance evidence.
