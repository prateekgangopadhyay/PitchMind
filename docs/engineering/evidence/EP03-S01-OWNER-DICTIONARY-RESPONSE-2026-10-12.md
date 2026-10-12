# EP03-S01 owner data-dictionary response — 2026-10-12

## Observed input

In the Engineering Council execution conversation on 2026-10-12, the repository owner stated that no authoritative data dictionary for the candidate feed is available and asked that one be generated and documentation updated if required.

## Resulting documentation

The repository contains [MANU-LIV-OBSERVED-DATA-DICTIONARY.md](../../data/MANU-LIV-OBSERVED-DATA-DICTIONARY.md), a generated structural inventory bound to the source SHA-256 in the candidate manifest. It reports observed field paths, JSON shapes, missing/null counts, value ranges/cardinalities, categorical values, and selected ordering checks.

This structural inventory does not invent source semantics. The following remain unspecified or unverified:

- coordinate units, axes, origin, orientation, attacking direction, and pitch dimensions;
- clock units and whether clock values are elapsed, period-local, or include stoppage time;
- period marker semantics;
- player metric definitions, units, calculation windows, sampling cadence, identity stability, and validity;
- event label meanings, source-to-domain mappings, score-side order, and canonical score authority;
- football realism and independent rights/privacy review.

## Decision and status

Keep the source manifest candidate_not_canonical. Canonical admission remains blocked pending evidence for semantic definitions, football review, and independent rights/privacy review. Do not normalize its coordinates/time, expose its values as measured player performance, create canonical football facts from its labels, or admit it to PostgreSQL until those gates have evidence.

This clarification is documentation evidence only. No application behavior or API contract changed. EP03-S01 remains unverified and unaccepted; human technical review and Product Owner acceptance remain pending.
