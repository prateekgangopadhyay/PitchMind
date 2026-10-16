# EP03-S05 one-match diagnostic — 2026-10-16

**Status:** Generator-only diagnostic. It does not verify story acceptance or the ingestion path.

**Verified code/profile commit:** `613964182e50294de7764f2e2f7d7f3d33fe9938`. This evidence note is committed in a documentation-only follow-up.

## Owner-directed profile

The repository owner specified one source match / one partition. The candidate file is pinned at SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`. Structural inspection observed 2,199 event-shaped JSON records with a `playerPositions` array on each record and 48,048 total attached position objects. It has no separate frame-record stream. The load harness treats each original event-shaped object as an opaque source record and preserves its original JSON bytes. That source-native shape has not been separately approved as an event/frame workload mix.

Burst duration remains unapproved. The one-second override below is only a diagnostic. Latency/loss/backpressure acceptance budgets remain unset.

## Execution

Command:

```sh
python3 scripts/ep03_s05_load_harness.py --duration-seconds 1 --output docs/engineering/evidence/EP03-S05-ONE-MATCH-DIAGNOSTIC-2026-10-16.json
```

Observed on local macOS against the pinned candidate JSON and an in-memory discard sink:

- Planned/scheduled/sink-received: 500 / 500 / 500 opaque records.
- Target/scheduled rate: 500 / 500 records per second.
- Full run: 0.998516 seconds; sink rate: 500.7432 records/second.
- Queue high-water mark/capacity: 1 / 1,024.
- Ordered event-ID/sequence digest: `995b1f10c90815034a0bec709bf316a5f21e116062f8f047b29bf08f2e96f174`.
- Replayed-payload digest: `939f357039563c3f3b096802c700cc869cfda620dc84ec748d533e332f77985a`.
- Full machine-readable result: [JSON report](EP03-S05-ONE-MATCH-DIAGNOSTIC-2026-10-16.json).

Harness suite: `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_ep03_s05_load_harness -v` — 13 passed, 0 failed. Python compilation, `node scripts/validate_operating_model.mjs`, and `git diff --check` passed.

## Limitations

The harness scheduled source records into a local in-memory discard sink only. It did not exercise Event Hubs, EventWorker, PostgreSQL, API, Azure, or the canonical admission path. It therefore reports no accepted/duplicate/quarantined counts, consumer lag, database/API latency, backpressure recovery, or end-to-end data-loss result. Replayed source records are not validated canonical football facts; source clock, coordinates, player metrics, event mappings and football realism remain unverified. The story remains unaccepted.

## Application replay diagnostic follow-up

The same single-match source was also run through the C# `ManuLivCandidateFeedReader` → `LocalCandidateFeedReplaySession` path with a local file checkpoint saved after every successful callback. That diagnostic delivered and checkpointed 2,199/2,199 records in order. The configured producer ceiling was 500 records/s; observed wall duration including those checkpoint writes was 18.886 seconds (116.44 delivered records/s). This is local replay/checkpoint evidence only. It did not exercise Event Hubs, EventWorker, PostgreSQL, canonical admission, or an approved event/frame payload mix. See [the detailed .NET report](EP03-S05-DOTNET-LOCAL-REPLAY-2026-10-16.md).
