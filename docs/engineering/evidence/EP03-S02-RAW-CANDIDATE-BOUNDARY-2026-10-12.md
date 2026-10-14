# EP03-S02 raw candidate boundary — local evidence

**Status:** implementation slice only; EP03-S02 remains unverified and unaccepted.
**Base revision:** `3551356dd4719c54557993cb77dfa4726c6c1266` (GitHub `main`).
**Environment:** LOCAL; no Azure operation, Docker service change, database write, or API enablement.

## Change

`ManuLivCandidateFeedReader` reads the candidate JSON (maximum 64 MiB) from a readable stream, calculates SHA-256 over the exact bytes consumed by the parser in one pass, checks required raw fields, rejects duplicate JSON keys, duplicate source event IDs, and non-increasing/negative sequences, and retains every complete source record as cloned JSON. The byte limit is enforced against actual reads rather than stream metadata. Its contract name is `manu-liv-raw-v1`. The parser does not map event labels, reinterpret clock or period values, normalize coordinates, assign canonical player/team IDs, interpret metrics, persist records, or connect to API read routes.

The candidate file's observed SHA-256 is `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; its Git blob is `9f53613d10dcf2e117e708c1b3e9706a49e1eb7c`, also cited by the source profile. The local test parsed all 2,199 records in input order and observed 48,048 event-attached player-position records without converting source fields.

## Verification actually run

Command:

```text
dotnet test tests/PitchMind.Infrastructure.Tests/PitchMind.Infrastructure.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
```

Result after the final parser change: **7 passed, 0 failed, 5 skipped**. The 5 skipped tests are PostgreSQL integration cases without a configured test database; they are unrelated to this raw parser. NuGet audit was disabled for local restore. The VSTest process required loopback socket permission, then completed successfully. The source fixture test pinned SHA-256, record count, order, attached-position count, and the absence of a fabricated `pitchXMetres` field. Negative tests cover malformed/non-array/empty JSON, missing fields, duplicate JSON keys/IDs, an oversized generated non-seekable stream that lies about its length, and non-increasing sequence.

Additional checks on the same worktree after the one-pass hardening:

- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false`: **60 passed, 0 failed, 5 skipped** (all skipped tests are opt-in PostgreSQL integration checks without a configured test database).
- `python3 -m unittest scripts.test_validate_manu_liv_candidate -v`: **20 passed**.
- `node scripts/validate_operating_model.mjs --self-test`: passed.
- `git diff --check`: passed.
- Independent read-only exact-diff review found no blocker for the raw candidate boundary. It confirmed that the 64 MiB limit is enforced against actual bytes and the SHA covers the same one-pass byte stream parsed by the reader.

## Remaining gates

- Source dictionary and canonical policy for clock/period semantics, coordinate units/axes/orientation, event mapping, player identity, and player metrics.
- Football review of event frequencies, roster/disciplinary semantics, and the proposed demo scenario.
- Canonical `EventEnvelope` / `TrackingFrame` contracts, normalization, typed quarantine, PostgreSQL persistence/replay, and independent review.
- Product Owner acceptance and broader integration evidence.

This evidence does not authorize canonical admission or API/UI match reads.
