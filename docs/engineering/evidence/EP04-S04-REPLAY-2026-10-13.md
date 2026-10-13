# EP04-S04 selected-time replay verification — 2026-10-13

**Code under test:** `d09450098a394dde06cfc1338de4cd3eb8d7fc91`

**Tree:** `dba9ecc60926f8c9bc0f98bcae06ec2750436a94`

**Review PR:** [#29](https://github.com/prateekgangopadhyay/PitchMind/pull/29), draft stacked on the current EP04-S03 base branch `codex/ep04-s03-restacked-on-208` at `39c9058eda728da3fa489fd90ff8313c0f2df31e`. PR #212's replay implementation is part of this PR branch.

**Environment:** isolated local worktree; .NET 10 Release; local PostgreSQL 16 restored against the retained `pitchmind_postgres_data`; `scripts/test-postgres.sh` created and dropped its unique disposable database. No Azure services or resources were contacted.

## Change verified

- Schema v6 stores a nullable initial replay roster and source-event envelopes; schema v7 stores a nullable, versioned replay context. Legacy rows are not backfilled with inferred context.
- `PostgresMatchProjectionReader.ReadReplaySnapshotAtAsync` returns the selected projection, event prefix, replay seed/context and bounded frames in one SQL statement, so all replay inputs come from one PostgreSQL MVCC snapshot.
- Replay verifies the selected canonical state digest and runs the exact requested detector version against the source event prefix and tracking frames selected by canonical revision and elapsed clock. It emits separate replay-input and derived-output SHA-256 digests and preserves evidence event/frame IDs.
- Replay rejects unsupported detector or coordinate versions, missing inputs, invalid source hashes, frame payload IDs that differ from indexed row IDs, context fields that differ from registered fixture/provenance/pitch metadata, out-of-bounds coordinates, incorrect period at halftime, unmapped/inactive frame players and future frames. Same-clock frames with distinct ordered frame sequence are permitted; an ambiguous press-arrival frame fails closed without throwing, while extra samples at other pattern times remain valid evidence.
- PostgreSQL integration reads the approved first-party fictional fixture from persisted event envelopes and frames and verifies the seven-event pressing insight at 3309 seconds with an additional same-second evidence sample. Replaying at 3308 seconds excludes later evidence; repeated outputs are deterministic. Corrupted replay context and frame payload identity each fail closed.
- Restacking also includes the persisted predecessor guard from the EP04-S03 base. Its first PostgreSQL run exposed an overly strict sequence-zero check that rejected registered bench players; validation now checks 11 active players for each team while allowing inactive roster members. A divergent prior-state regression remains covered.
- The candidate Manu-Liv JSON remains unused. No authoritative data dictionary exists for its coordinate, time and player-metric semantics.

## Executed evidence

| Command | Result |
|---|---|
| `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false -v:q` | Passed, 93 passed, 0 failed; 21 PostgreSQL integration tests skipped because this invocation had no database connection configured. |
| Isolated PostgreSQL 16 harness, `PitchMind.Infrastructure.Tests` Release assembly from this worktree | 21 passed, 0 failed, 0 skipped. The harness created and dropped a uniquely named disposable database against the preserved local PostgreSQL 16 volume. |
| Focused Application suite | 34 passed, 0 failed, 0 skipped. |
| `node scripts/validate_operating_model.mjs` | Passed; 12 epics, 59 stories, 28 Council skills, 3 design skills, no dependency cycles, 2 approval gates. |
| `git diff --check` | Passed on the final implementation and evidence/documentation changes. |

Independent code reviews identified mixed-snapshot reads, inactive-player evidence risk, ambiguous halftime period, missing positive PostgreSQL insight coverage, missing frame payload-to-row ID verification, unbound replay-context metadata and a detector restriction on extra same-second samples. The implementation and tests were updated for each finding. The full Release and PostgreSQL suites passed on this code revision. GitHub exact-head review and Product Owner acceptance remain pending.

## Remaining scope and acceptance

- The replay service is internal and is not wired to an HTTP route; authentication, public API contract and frontend scrubbing remain separate work.
- Derived output digests are computed on read, not stored as canonical insight/evidence facts.
- EP03-S03 frame ingestion and production source admission are not implemented by this slice. Candidate synthetic-feed semantics remain uncertified.
- Independent technical review and Product Owner acceptance are pending. This evidence does not mark EP04-S04 accepted.
- No Azure deployment, resource mutation or DEV change was performed.
