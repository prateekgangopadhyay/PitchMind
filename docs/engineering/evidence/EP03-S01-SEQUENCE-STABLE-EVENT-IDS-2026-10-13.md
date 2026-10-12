# EP03-S01 final-sequence event identity evidence

**Code commit:** `5cf19afeef8220a0d49f8bd346a80106f47ffe35`  
**Parent stack:** PR #204 head `87df3b2a6d6ba755294eeb63885b3ae8c90adb3d`  
**Scope:** deterministic identity in the fictional seeded fixture; candidate-source IDs and data are unchanged.

## Change

The generator previously assigned event IDs while appending events, then sorted events and replaced their sequence numbers. IDs therefore reflected pre-sort insertion order rather than the final emitted sequence. The generator now computes each event ID after ordering, from invariant-culture fixture version, seed, clock, final sequence, and action. Since this changes generated IDs, the fixture version is now `synthetic-v3`. The stable formula is asserted for every emitted event.

## Verification

On .NET SDK 10.0.401, the focused `PitchMind.Domain.Tests` Release suite passed **5/5, 0 failed, 0 skipped**. The exact generator and test files from this code commit were copied into the cached-assets test clone and verified byte-identical with `cmp`; the test command used `--no-restore` because NuGet restore/audit could not reach the configured vulnerability endpoint. VSTest's first sandboxed attempt was denied a loopback bind; the local-permission rerun passed.

The regression checks final-sequence-derived IDs for every fixture event, contiguous event sequence, same-seed repeatability, cross-seed match identity and event-GUID disjointness, per-event match identity, and repeatability across Arabic current culture. The Council dependency-DAG self-test and `git diff --check` passed. Independent read-only review confirmed that the ID and sequence use the same final index, the version bump avoids reusing `synthetic-v2` for changed IDs, and found no code blocker.

## Limits

This proves a deterministic ID/version invariant for the fictional fixture only. It does not prove football-rule realism, score-state domain review, canonical candidate-feed admission, PostgreSQL persistence, full-solution tests, hosted CI, Product Owner acceptance, or Azure behavior. The candidate feed remains source-only and blocked from canonicalization. No public API, database, or Azure resource was changed.
