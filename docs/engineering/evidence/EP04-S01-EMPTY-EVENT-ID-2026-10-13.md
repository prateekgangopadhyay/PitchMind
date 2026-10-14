# EP04-S01 empty event identity guard — 2026-10-13

**Code revision:** `bb9f12789391d3acfac0399c0b061c11102ed1a0`\
**Stack:** local child of draft PR #150 at `57067692c6c51442e6eb40291af40ba318262bfb`\
**Scope:** reject the empty GUID sentinel at the `MatchAggregate.Apply` boundary before duplicate lookup or state mutation.

## Change and evidence

`MatchEvent.Create` already rejects `Guid.Empty`, and the PostgreSQL event table has the same invariant. Because `MatchEvent` is a public record, callers can construct or deserialize one without using that factory. The aggregate now rejects such an event consistently. A Domain regression test asserts rejection leaves sequence, phase and score unchanged.

- `dotnet build PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` — passed, 0 warnings, 0 errors.
- Standalone local check against the exact built Domain assembly — passed: the empty-ID event was rejected with state unchanged, and the following valid kickoff applied. Output: `PASS: empty event ID rejected without state change; subsequent valid event applied.`
- `node scripts/validate_operating_model.mjs --self-test` — passed.
- `git diff --check` — passed.
- `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` — passed, **28 passed, 0 failed, 0 skipped**, after restoring this project from the machine's local NuGet package cache. This exact code revision includes the new empty-ID regression.
- Full solution test command was attempted. The exact build compiled every project; Domain passed 28/28, but Application, Architecture, Contracts and Infrastructure test hosts aborted before executing tests because the copied local assets did not provide `testhost.runtimeconfig.json`. No full-suite pass/fail count is claimed. A solution-wide local-cache restore stalled without diagnostics; no full-suite result is available.
- PostgreSQL integration — **not run**. `docker compose ... ps` was denied access to the Docker socket in this execution context; no service or volume was changed.

This narrow identity guard does not establish EP04-S01 acceptance. The aggregate remains an in-memory fixture projection; canonical admission, correction/revision history, source certification, persisted dismissal facts, full exact-head tests, independent human review and Product Owner acceptance remain open. No API, schema, C4/ADR, Azure or deployment change is included.
