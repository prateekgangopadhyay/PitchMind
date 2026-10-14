# EP05-S01 — Mainline football concept proposal evidence

**Story:** EP05-S01, Formal football concept dictionary (P0). **Status:** Partial implementation; not accepted.
**Base:** `main` at `673ed64d2032484ff3568c2b1571a457b7547f5b`.
**Code commit/tree:** `4facc812994f8c24f4e68f25a60e3f9fd0e0b03a` / `e3f6a07974eaa747c3319fc2d156fb83608c0b03a`.
**Candidate source SHA-256:** `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`.

## Change

Adds a proposal JSON and strict internal Domain loader for the eight required concepts: press, trap, response, danger, overload, line-break, counterpress and rest-defence. Each term includes a definition, non-definition, phases, minimum evidence, insufficient-evidence conditions, prohibited claims, and an explicitly unapproved threshold status. The loader rejects approval status/references and source-profile values that claim unresolved candidate feed semantics have been certified.

This slice adds no detector behavior, public API contract, candidate-event mapping or runtime consumer. The dictionary does not approve tactical definitions or source fields. In particular, it does not interpret the candidate feed's coordinates, clock, player metrics or provider event labels.

## Verification

- `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false`: **47 passed, 0 failed, 0 skipped** on code commit `4facc812994f8c24f4e68f25a60e3f9fd0e0b03a`.
- Tests validate complete taxonomy, required evidence fields, malformed proposals, no self-approval, retained candidate source digest and manifest/feed byte match. Digest equality proves file identity only, not meaning or realism.
- `git diff --check` passed after the documentation update.
- NuGet vulnerability audit was disabled. Independent exact-head review and hosted CI have not run for this restack.

## Open gates

Product Owner and football-analyst review of definitions/examples, accepted fixtures, source coordinate/clock/player-metric semantics, canonical mappings, football realism and end-to-end concept-to-signal trace remain outstanding. The current proposal has no trusted approval verifier and cannot be promoted to `approved` by editing JSON alone. EP05-S01 remains partial and unaccepted. No Azure resource or deployment change was made.
