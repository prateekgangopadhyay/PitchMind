# EP03-S05 five-second diagnostic — 2026-10-16

**Status:** Generator-only diagnostic; not ingestion verification or story acceptance.
**Code/profile commit:** `403a315f1e08589a6a11500cec9d138fa7877c75`.
**Profile:** owner-approved for diagnostic use: one source match/partition, 5 seconds at 500 offered records/s, raw source event JSON with attached `playerPositions` unchanged, no standalone frame records, and stop at source exhaustion without repeating IDs/sequences.

## Observed result

The pinned source (`data/simulations/manu-liv-match-dataset.json`, SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`) contains 2,199 records. The requested schedule was 2,500 records; the harness scheduled and delivered only the available 2,199, preserving source order and bytes. It stopped at source exhaustion and reported an incomplete run rather than cycling the source.

- Full measurement window: 5.001331875 seconds.
- Scheduled: 2,199 records (439.8/s); local in-memory discard sink received 2,199 (439.6829/s).
- Queue high-water/capacity: 1/1,024.
- Ordered ID/sequence digest: `1573be4a52ddd73f6f73e221980858a633443d722426eff8d1ed142dd738c260`.
- Payload digest: `34e5d5c7d9cf7b1169ee53743a631d701a34473afd5713d13a2a4de49aaf6a05`.
- Attached positions preserved: 48,048 objects across 2,199 source records; separate frame records: 0.
- Complete: **false**; source limit reached: **true**.

The full run is recorded in [machine-readable evidence](EP03-S05-FIVE-SECOND-DIAGNOSTIC-2026-10-16.json). The harness command was `PYTHONDONTWRITEBYTECODE=1 python3 scripts/ep03_s05_load_harness.py --output /private/tmp/ep03-s05-five-second-diagnostic.json`; exit code 1 is the harness's expected incomplete-result signal for this source-limited diagnostic.

The harness unit suite passed 13/13 before this run. This is generator throughput to a local discard sink only. Event Hubs, EventWorker, PostgreSQL, canonical admission, accepted/duplicate/quarantine counts, consumer lag, end-to-end latency, and recovery were not measured. Source clock/coordinate/player metric semantics and football realism remain unvalidated. Story acceptance budgets remain unset and the story remains unaccepted.

## Independent rerun — 2026-10-16

The approved profile and pinned candidate source were rerun independently on the evidence branch. The 13 focused harness tests passed again (13/13). The source contained 2,199 records against a 2,500-record request; the run again stopped at source exhaustion and returned the expected incomplete-result exit code 1.

- Run ID: `e2272906-6553-4d76-a45b-7797c2ca5557`.
- Full window: `5.000742624979466` seconds; scheduled/sink rates: 439.8 / `439.73468840721023` records/s.
- Scheduled and received: 2,199 / 2,199; queue high-water/capacity: `3` / 1,024.
- Ordered ID/sequence and payload hashes matched the first run exactly.
- Manual assertions against `ep03-s05-load-report.schema.json` passed, including explicit null ingestion measurements and incomplete source-limited status.

The independent machine report is [`EP03-S05-FIVE-SECOND-INDEPENDENT-RERUN-2026-10-16.json`](EP03-S05-FIVE-SECOND-INDEPENDENT-RERUN-2026-10-16.json). It remains generator-only local evidence and does not establish ingestion acceptance.
