# EP04-S04 deterministic replay API evidence — 2026-10-16

**Scope:** local stacked implementation of the canonical state-only deterministic replay endpoint. This evidence does not accept EP04-S04 or certify the candidate Manu-Liv feed.

## Implemented slice

- Added `GET /api/v1/canonical/matches/{matchId}/replay/deterministic`, protected by the existing `MatchReplay` role policy (`analyst` or `admin`).
- Replays the immutable source-event prefix to the selected time and admission/projection revisions, then compares the resulting state digest with the selected persisted projection.
- Returns the canonical snapshot, a bounded event page, the replay-input SHA-256, and the replayed-state SHA-256. It does not run or return detector output.
- Bounds full replay to 100,000 events and 32 MiB of serialized source input. PostgreSQL checks event and payload lengths before materializing JSON text; page size is at most 100.
- Adds an opt-in HTTP integration test that seeds a test-only canonical match through the real PostgreSQL writer and API composition, then verifies a 55:08 replay excludes the 55:09 goal and the later revision includes it.
- Updates `docs/api/API.md` and the canonical OpenAPI document for the route, role, request parameters, response and failure behavior.

## Verification observed locally

Commands ran from the stacked checkout on 2026-10-16:

| Command | Result |
|---|---|
| `dotnet test tests/PitchMind.Contracts.Tests/PitchMind.Contracts.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers --filter FullyQualifiedName~CanonicalDeterministicReplayApiTests` | **4 passed, 0 failed**; the opt-in PostgreSQL case is skipped without a harness connection and executed in the next row. Covers selected-time state/digest, two-page bounded event pagination, absent detector output, role denial, invalid time, and faulty-adapter future-event rejection. |
| `./scripts/test-postgres.sh` | **Infrastructure 93/93** and **Contracts 159/159**, 0 failures and 0 skips. This includes the real PostgreSQL-backed deterministic replay HTTP test. Runs against LOCAL PostgreSQL using a uniquely named temporary test database; harness reported successful completion and dropped it. |
| `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers` | Project results: AgentWorker 27/27, API 12/12, Application 176/176, Architecture 30/30, Contracts 157 passed/2 skipped, Domain 71/71, Infrastructure 53 passed/40 skipped. Aggregate observed results: **526 passed, 42 skipped, 0 failed**. The database-related skipped tests were exercised by the PostgreSQL harness above; the two Contracts skips (readiness and replay HTTP) are exercised by that harness. |
| `node scripts/validate_operating_model.mjs` | Passed; 12 epics, 59 stories, 28 council skills, 0 cycles, and 2 approval gates. |
| OpenAPI YAML parse/path assertion and `git diff --check` | Passed. |

The first focused run caught an incorrect test assumption that one 100-event page contained the entire 117-event synthetic prefix. The test now verifies the bounded first page and follows its opaque cursor to the remaining events. The first PostgreSQL HTTP attempt also caught test-host configuration selecting the unavailable reader; the test factory now configures the real local PostgreSQL read adapter. The subsequent focused, PostgreSQL and Release runs above passed.

## Remaining gates and limits

- The code and tests have not received an independent human/GitHub technical review; no hosted CI result is claimed.
- Product Owner acceptance remains pending. EP04-S04 remains partial and unaccepted.
- Browser scrubbing and accessibility are not covered by this API slice.
- The source profile still lacks authoritative coordinate, clock/period, player-metric and canonical-mapping definitions. The endpoint does not use or admit the candidate feed.
- Azure PostgreSQL integration and deployment were not performed. No Azure resource or DEV revision was changed.
