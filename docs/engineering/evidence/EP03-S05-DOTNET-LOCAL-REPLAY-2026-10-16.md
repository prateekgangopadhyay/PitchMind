# EP03-S05 .NET local replay diagnostic — 2026-10-16

**Verdict:** Diagnostic evidence only; EP03-S05 remains unaccepted and unverified for canonical ingestion.

## Profile and scope

- Source: `data/simulations/manu-liv-match-dataset.json`
- Source SHA-256: `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`
- Owner-directed partition mix: one source match / one partition
- Observed source shape: 2,199 event-shaped records; 48,048 attached `playerPositions`; no separate frame-record stream
- Configured local replay ceiling: 500 records/second
- Burst duration: not approved; diagnostic ran to completion of the candidate file
- Callback: in-process discard/measurement callback
- Persistence: `FileCandidateFeedReplayCheckpointStore`, saved after each successful callback
- Environment: local macOS, .NET SDK 10.0.401 / runtime 10.0.12

## Execution and results

Command:

```sh
dotnet test tests/PitchMind.Infrastructure.Tests/PitchMind.Infrastructure.Tests.csproj --filter 'Category=LoadDiagnostic' --logger 'console;verbosity=detailed' --disable-build-servers -m:1 --no-restore
```

Observed result:

- Test: `ManuLivCandidateFeedReplayLoadDiagnosticTests.Replays_the_single_match_candidate_with_local_durable_checkpoints`
- Passed: 1; failed: 0
- Delivered and locally checkpointed: 2,199 / 2,199
- Observed match partitions: 1
- Duration including local per-record file checkpoint writes: 18.886 seconds
- Effective delivered rate: 116.44 records/second
- Ordered event ID / sequence SHA-256: `7ec2e21b291e11238ad5b7e0005daf071538fbf2da1c29289e0cd7831d6366ae`
- Replayed raw payload SHA-256 (one JSON object per line): `34e5d5c7d9cf7b1169ee53743a631d701a34473afd5713d13a2a4de49aaf6a05`
- The test asserted full source count, exact event ID and sequence order, single-match cardinality, completed session state, and final acknowledged cursor index.

## Interpretation and limits

This measurement includes one durable local file checkpoint replacement per record. The replay producer is configured with a 500-record/s ceiling; this run does not show that the local file-checkpoint session sustains that rate. It also does not isolate checkpoint write latency, so the observed difference cannot be attributed to one component without further profiling.

No Event Hubs, EventWorker, PostgreSQL, canonical admission, duplicate/quarantine path, consumer lag metric, Azure service, or API was exercised. The run therefore does not measure accepted, duplicate, quarantined, lost, or recovered canonical events and does not satisfy the story's no-unaccounted-loss gate. The source-native payload mix, burst duration, latency and backpressure budgets remain unapproved. Coordinates, clock/period semantics, player metrics, event mappings, and football realism remain unvalidated. This result must not be described as 500-eps ingestion acceptance.

The test writes its temporary checkpoint beneath the test worktree and removes it after execution. No Azure resources or deployment configuration were touched.
