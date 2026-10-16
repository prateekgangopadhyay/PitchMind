# EP03-S01 candidate event identity evidence

**Candidate code commit:** `5cf19afeef8220a0d49f8bd346a80106f47ffe35` (`fix(EP03-S01): derive event IDs from final sequence`).
**Current main checked:** `02c6c5c08635ab822ab1c57174f9681a462311d8`.
**Status:** The candidate correction is not present in current `main`. This report records a historical candidate slice and the outstanding merge gap; it does not claim the correction was merged.

## Candidate change

The candidate generator computes event IDs after sorting, using the final emitted sequence. Because this changes generated identities, it advances the fixture version to `synthetic-v3`. The candidate commit is not an ancestor of current `main`; the prior PR #135 squash merge at `3556e4d2fcb9f4ff1f3b2a45f2a7c80b7c769ab1` does not contain this change.

## Current-main inspection

At current `main`, `tests/PitchMind.TestFixtures/SyntheticFixtureGenerator.cs` derives the event ID inside `Add` from `events.Count`, then sorts the events and rewrites only `Sequence`. Therefore the current mainline IDs do not derive from the final emitted sequence. The intended correction remains outstanding.

## Historical verification claim and current run

The original candidate report states that the focused Release `PitchMind.Domain.Tests` suite passed 5/5, with the Council dependency-DAG self-test and `git diff --check` passing. This session did not independently reproduce that test result: the exact candidate worktree lacked `project.assets.json`; an offline restore did not complete and was cancelled. No current-main test was run for this correction.

## Limits

This candidate-only identity/version evidence does not prove football-rule realism, score-state domain review, canonical candidate-feed admission, PostgreSQL persistence, full-solution tests, hosted CI, Product Owner acceptance, or Azure behavior. EP03-S01 remains unaccepted and the current-main implementation gap remains open. No public API, database, or Azure resource was changed.
