# EP04-S05 match integrity — local candidate golden evidence

## Scope and revisions

Test commit `e75e31a119f4c67d22283aa65f9177736571c394` (tree `d1c32def6eff471ca771a47d58a4bac664a9510c`) on `codex/ep04-s05-match-integrity-goldens`, stacked on draft PR #214. The golden snapshot outputs were captured from the local offline replay route on PR #214 base code at `fd8018c4e13e9b8d20c09760c0c83b7d24635015` and are now asserted against `SyntheticMatchService.ReplayAt`.

The fixture file is `tests/PitchMind.Application.Tests/Fixtures/ep04-s05-synthetic-v2-replay-goldens.json`. It pins fixture version `synthetic-v2`, seed 42, and serialized fixture content SHA-256 `ce5a5a99a056053626758f7e3d92e47fad89215a6509c65475b60936153e793b`. Snapshot coverage is selected elapsed time 0, 2700, 3308, 3309, 3600 and 5400 seconds. Expected output SHA-256 values:

| Elapsed seconds | Boundary | Output SHA-256 | State summary |
| ---: | --- | --- | --- |
| 0 | Kickoff | `4433fec588fdcda5d161d6d02e4f2bfa2bafa976ff37868bbc8ed0c393080e06` | 0–0, first half |
| 2700 | Second-half kickoff | `4481f9e3671a4f3af7bfbae29292ef81a765c57560e19e417793d82f25a7c5f1` | 0–0, second half |
| 3308 | 55:08 before goal | `2b98dd6acfb4128f56b76d85fd0ab3f25ea4e40be5399ac67f70a697c3f3ddad` | 0–0, no insight |
| 3309 | 55:09 goal | `3dab0eaddc254b208739407438be670f2495cc9e9289f88c8cd65454a8084628` | 1–0, one insight |
| 3600 | Home substitution | `011430ac7e01513da74a83d6fa8b10e427c8dd8b403aceb0b993200f4d03b6e4` | 1–0, active roster 11/11 |
| 5400 | Full time | `55d582d93ac0d5f5bdfbd3f033f6b9086976ab290ca77a3e4c8956b6c5823abe` | 1–1, full time |

These are **candidate regression goldens** derived from the repository's fictional synthetic fixture. The fixture explicitly says Product Owner review is pending. They are not approved football truth, and the Manchester United–Liverpool candidate feed is not involved.

## Executed checks

- `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` — **27 passed, 0 failed, 0 skipped**.
- The run includes the six golden selected-time state/output checks, the correction-input version test, selected-time leakage assertions, malformed fixture/frame/coordinate rejection, and missing-tracking suppression.
- `git diff --check` passed before the test commit.
- Local API response hashes at the listed selected times were fetched from `127.0.0.1:5089`; the process was stopped after capture.

The correction test changes the scoring team and actor in a new offline fixture version while preserving that event's ID and sequence. It verifies a new input/output hash and that the original fixture continues to reproduce its prior output. This does not prove a persisted correction workflow.

## Limits and outstanding review

No PostgreSQL replay, canonical event-envelope rebuild, persisted roster seed, source-feed admission, candidate-feed football certification, CI pipeline, independent formal review, or Product Owner approval is claimed. The parent EP04-S04 PR remains a draft, and these goldens do not satisfy the program's approval gate. No Azure resources were changed.

## Combined PR #214 head verification

The goldens were rerun in the full Release solution suite on exact combined PR #214 head `d8fd5dab7afdb2d60aa6abe4c1d408aa2fd378a9` after PR #215 merged. Application tests passed **27/27**, including all six checkpoints and corrected-input isolation; Contracts 27/27, Domain 28/28, and Architecture 5/5 also passed. The solution aggregate was 87 passed, 0 failed; 18 Infrastructure PostgreSQL tests were skipped because the suite invocation had no test connection. A separate local HTTP smoke on this same head verified the 3308/3309 boundary, stable repeated output hash with version pins, event-prefix cutoff, and 400 `INVALID_REQUEST` for an unsupported fixture version. Full command and response evidence is recorded in [EP04-S04 replay verification](EP04-S04-REPLAY-2026-10-13.md).

`node scripts/validate_operating_model.mjs --self-test` and `git diff --check` passed after this evidence update. The candidate goldens still await Product Owner review; this rerun does not establish canonical PostgreSQL replay, candidate-feed approval, or story acceptance. PR #214's prior approval predates its current aggregate head. No Azure resources or deployments were touched.
