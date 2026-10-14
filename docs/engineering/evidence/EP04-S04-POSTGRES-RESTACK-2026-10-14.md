# EP04-S04 PostgreSQL replay restack — local verification

## Scope and revision

The code under verification is commit `d1a0bfdc099168f876861128f4df29406ced2c09` (tree `4c5ddb1b2040355fdb4f83a927655002e3963f1f`) on `codex/ep04-s04-canonical-post-merge`, based on `origin/main` at `55f90e6f0018772c2bb48c947d598c1d1c082dbe`. The restack merge commit `cf4b3b74afafbf29acbbda8b0b95b070a2c08ffd` has parents `55f90e6` and PR #29 head `83e3543c7ad648bfeb20ab88e1a8d520f9cfa395`; d1a0bfd adds the replay seed correction.

The restack preserves main's schema v6 competition-rules migration and adds replay seed/source-event columns in v7 and replay context in v8. The internal PostgreSQL point-in-time query remains unregistered in the API and does not change the fail-closed public routes. Independent review identified and we fixed the replay seed's missing competition rules by adding a v2 seed and a regression assertion for ID, rules version and zero re-entry limit.

## Executed checks

- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false` completed successfully on code tree `4c5ddb1`: 194 passed, 0 failed, 23 skipped. The skipped tests are PostgreSQL integration tests because no test connection was configured. NuGet audit was disabled.
- The test run required local execution permission because the sandbox denied VSTest's loopback socket bind. The initial run exposed compile errors; those were fixed before the successful run.
- `git diff --check` completed with no reported whitespace errors.
- Conflict-marker scan reported no remaining merge markers.
- Independent read-only review found one P1 in the first restack (competition context was absent from replay seed). Commit d1a0bfd adds replay seed v2, restores competition rules in `FromReplaySeed`, checks seed/source/projection agreement, and asserts replayed competition ID, version, and a zero re-entry limit. The reviewer rechecked the fix and reported the P1 resolved with no further P1 in the reviewed areas; the reviewer did not run tests.

## Not verified / limits

The PostgreSQL integration suite did **not** execute. The Docker daemon is unreachable in this environment. The repository Compose file names the persisted volume `pitchmind_postgres_data`, but Docker Desktop could not be started: `/Applications/Docker.app` reports its executable is missing, and neither configured Docker socket is available. Therefore migration execution, PostgreSQL snapshot semantics, replay persistence, and the new database integration tests remain unverified on this restack.

No hosted CI, GitHub-submitted review/approval, Product Owner acceptance, candidate-feed certification, API route, frontend scrub, Azure action, or deployment is claimed. No Azure resource was accessed or changed.
