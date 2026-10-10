# EP07-S04 local design review

**Date:** 2026-10-10
**Review type:** implementation-side visual and accessibility-tree inspection; independent review and Product Owner acceptance are pending.

## Preview setup

The match page was rendered locally at a selected time of 55:09 in period 2 using a temporary Vite-only visual fixture. The fixture used generic Home/Away labels and made-up player IDs and positions. It was not taken from the candidate JSON, PostgreSQL, or any football match. The mock middleware/configuration lived under `/private/tmp` and is not part of the application or PR. A screenshot was inspected in the browser but was not retained in the repository.

## Findings and changes

- The scoreboard, selected-time control, pitch and event list all displayed 55:09. The selected goal button exposed its time, sequence and period in the accessibility tree.
- The observed pitch included a ball marker, team-coloured player markers, dimensions and the team-direction statement. The visual review showed that full player IDs crowded the pitch, so the markers now use numbers and the visible position table maps each number to its player/team ID and coordinates.
- The position table is open by default and includes a caption, column headings and one row per position. This gives a nonvisual equivalent without relying on colour alone.
- Missing insights and canonical key moments rendered as explicit empty/unavailable states; the preview did not substitute demo claims.

## Limits still open

- This was a desktop browser inspection only. No narrow viewport, 200% zoom, keyboard-only journey, screen reader, axe/WCAG contrast tool or reduced-motion manual check was executed. The current unit tests cover event selection, play/pause state, exact-time frame display, direction orientation and empty frame response.
- A populated canonical PostgreSQL pitch was not demonstrated. The repository has no tracking-frame admission writer. The exact-time/watermark reader integration test passed against a temporary isolated PostgreSQL database; there is no persistent `PITCHMIND_TEST_POSTGRES` setting. The candidate dataset remains structurally profiled; coordinate units, player metrics, football realism and canonical mappings are still unapproved.
- The current canonical API limits selected time to 5,400 seconds while the candidate source reaches 5,761 seconds (96:01). The source cannot be replayed to full time until the canonical API/time contract is revised and verified by its accountable workstream.
- Independent technical review and Product Owner acceptance remain pending. This design inspection is not acceptance evidence.
