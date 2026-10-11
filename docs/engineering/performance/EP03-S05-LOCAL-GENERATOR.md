# EP03-S05 local generator harness

This tooling precursor measures only a rate scheduler feeding a bounded in-memory discard sink. It does not connect to Event Hubs, an event worker, PostgreSQL, the BFF, or Azure. Its output is explicitly marked `generator_only_not_ingestion_evidence`; accepted, duplicate, quarantined, consumer-lag, database-latency, and recovery values are null.

The harness reads the pinned JSON array as versioned opaque source records. It verifies the configured SHA-256 and extracts only the configured event ID, sequence, and match ID metadata. Each record's JSON value bytes are passed through without reserialization. Replays repeat the original IDs and sequence; the tool does not renumber or canonicalize them. Provenance is recorded as an external owner-confirmed label and is not asserted to exist inside the source file.

## Current profile limits

`ep03-s05-load-profile.json` pins the candidate file SHA-256 and requests 500 events per second. Duration, payload-mix approval, partition-mix approval, and acceptance budgets remain unset. The normal invocation intentionally stops until a duration is agreed:

```sh
python3 scripts/ep03_s05_load_harness.py --output /tmp/ep03-s05-report.json
```

For an explicitly generator-only diagnostic, `--duration-seconds` supplies a temporary duration. The report records that override and remains non-acceptance evidence:

```sh
python3 scripts/ep03_s05_load_harness.py \
  --duration-seconds 1 \
  --output /tmp/ep03-s05-generator-only.json
```

The source currently contains one match. Multi-match partition mixes and semantic payload shaping are rejected or unsupported. This harness does not validate source clock, coordinate, player metric, football realism, or canonical mapping semantics. It must not be used to infer ingestion throughput or zero data loss.

## Deterministic tests

```sh
python3 -m unittest scripts.test_ep03_s05_load_harness -v
```

The tests use a tiny generic JSON array and a fake clock. They cover raw record preservation, ID/sequence order across replay cycles, pinned-source drift, unsupported multi-match input, and the unset-duration gate without loading the football source into test assertions.
