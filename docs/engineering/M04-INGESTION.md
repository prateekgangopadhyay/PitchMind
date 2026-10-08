# M04 — Canonical event contracts, synthetic fixtures, ingestion ordering and deduplication

**Status:** In progress. This initial change is an application-layer sequencing slice, **not** a production Event Hubs ingestion adapter.

## Scope and design decisions

- Domain remains free of external dependencies. Existing `MatchEvent` is the initial canonical in-process event representation; wire contract versioning is **not yet implemented**.
- `OrderedMatchEventBuffer` is a bounded, single-writer, per-match in-memory reorder/deduplication component. It accepts a configured initial sequence and publishes only contiguous sequences.
- Duplicate event IDs are ignored within the lifetime of the buffer; conflicting sequence IDs, stale sequences and wrong-match events are quarantined with machine-readable reasons.
- Out-of-order events are buffered up to `maxPending`. Overflow quarantines new out-of-order events rather than dropping or silently skipping sequence gaps.
- The buffer is **not thread-safe**. Partition routing must serialize processing per match; concurrency and cross-partition ordering need explicit design and tests.
- In-memory identity tracking is unbounded over a match lifetime. Production implementation must use durable dedupe/checkpoints and bounded lifecycle management.
- A restart loses volatile state. Do not checkpoint Event Hubs offsets until events are durably committed; persistence, transactional replay and outbox are M05 responsibilities.

## Remaining M04 acceptance work

- [ ] Define versioned wire contracts (JSON schema or equivalent), compatibility policy and rejection/quarantine envelope.
- [ ] Add realistic, deterministic football match fixtures including malformed, duplicate, late, missing and out-of-order events.
- [ ] Implement LOCAL ingestion adapter and Azure Event Hubs adapter behind ports with checkpoint/retry/backpressure semantics.
- [ ] Add structured diagnostics, redacted quarantine records and poison-message handling.
- [ ] Run reproducible 500-events/sec burst benchmark with latency, memory, throughput and duplicate invariant evidence.
- [ ] Validate concurrency, lifecycle bounds and event ID conflicts across different sequence numbers.
- [ ] Execute local and Azure DEV smoke tests; document cost and operational controls.

## Verification

Run `dotnet test PitchMind.slnx -c Release` on a machine with .NET 10 SDK. This change has not been executed in an independent build runner at commit time.
