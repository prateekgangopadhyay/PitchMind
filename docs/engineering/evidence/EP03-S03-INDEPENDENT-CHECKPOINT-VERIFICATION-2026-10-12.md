# EP03-S03 independent checkpoint verification — 2026-10-12

## Scope

Independent local test rerun of the exact published PR #95 source revision:

- Revision: d5ac971e9910eb2843495fe4c5e222d4b0995493
- PR: [#95 — reject linked checkpoint ancestors](https://github.com/prateekgangopadhyay/PitchMind/pull/95)
- Environment: local .NET Release test run; no Azure calls, deployment, Docker operation, or PostgreSQL connection.

## Results

1. Focused command:
   dotnet test tests/PitchMind.Infrastructure.Tests/PitchMind.Infrastructure.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false --filter FullyQualifiedName~CandidateReplayCheckpoint
   Result: **7 passed, 0 failed, 0 skipped**.
2. Full Infrastructure command:
   dotnet test tests/PitchMind.Infrastructure.Tests/PitchMind.Infrastructure.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false
   Result: **41 passed, 0 failed, 5 skipped**, 46 total. The five skipped tests are the opt-in PostgreSQL ledger integration tests; this run does not verify PostgreSQL behavior.
3. The initial sandboxed VSTest launch aborted with SocketException 13 while binding its local test-runner communication socket. The identical focused and full commands were rerun with local VSTest socket access and produced the results above.

## Limits

Both commands used --no-restore and NuGetAudit=false; no NuGet vulnerability audit result is claimed. The tested PR remains a local candidate-replay checkpoint slice. The path checks are not an atomic no-follow guarantee and do not eliminate concurrent check-to-open/replace races. This evidence does not establish Event Hubs behavior, PostgreSQL checkpointing, canonical feed admission, source semantics, football approval, human technical acceptance, or Product Owner acceptance. EP03-S03 remains partial and unaccepted.
