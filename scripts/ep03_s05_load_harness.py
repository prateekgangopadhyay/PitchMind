#!/usr/bin/env python3
"""Bounded, local generator-only harness for EP03-S05.

This tool replays source JSON records into an in-memory bounded queue. It does not
connect to a broker, worker, database, API, or Azure service, and therefore cannot
measure ingestion acceptance, duplicates, quarantine, consumer lag, or recovery.
"""
from __future__ import annotations

import argparse
import asyncio
from dataclasses import dataclass
from datetime import datetime, timezone
from hashlib import sha256
import json
import math
from pathlib import Path
import sys
import time
from typing import Any, Callable
import uuid

MAX_PROFILE_BYTES = 1_000_000
SUPPORTED_PROFILE_VERSION = "1.0"


class HarnessError(ValueError):
    """Invalid or unsupported load harness input."""


@dataclass(frozen=True)
class SourceRecord:
    """Metadata plus the unchanged JSON object bytes from one source record."""

    ordinal: int
    event_id: str
    sequence: int
    match_id: str
    payload: bytes


def _skip_ws(text: str, index: int) -> int:
    while index < len(text) and text[index] in " \t\r\n":
        index += 1
    return index


def parse_json_array_records(
    data: bytes,
    *,
    event_id_key: str = "eventId",
    sequence_key: str = "sequence",
    match_id_key: str = "matchId",
    max_records: int = 100_000,
) -> list[SourceRecord]:
    """Read array entries without reserializing, normalizing, or rewriting them."""
    if not data:
        raise HarnessError("Source is empty")
    try:
        text = data.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        raise HarnessError("Source must be UTF-8 JSON") from exc

    def reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise HarnessError("Source JSON objects must not contain duplicate keys")
            result[key] = value
        return result

    decoder = json.JSONDecoder(parse_constant=lambda value: (_ for _ in ()).throw(
        HarnessError(f"Non-finite JSON number is not supported: {value}")
    ), object_pairs_hook=reject_duplicate_keys)
    index = _skip_ws(text, 0)
    if index >= len(text) or text[index] != "[":
        raise HarnessError("Only a top-level JSON array is supported")
    index += 1
    records: list[SourceRecord] = []
    index = _skip_ws(text, index)
    if index < len(text) and text[index] == "]":
        raise HarnessError("Source array contains no records")

    while index < len(text):
        index = _skip_ws(text, index)
        start = index
        try:
            value, end = decoder.raw_decode(text, index)
        except (json.JSONDecodeError, RecursionError) as exc:
            raise HarnessError("Malformed JSON source array") from exc
        if not isinstance(value, dict):
            raise HarnessError("Every source array entry must be a JSON object")
        event_id = value.get(event_id_key)
        sequence = value.get(sequence_key)
        match_id = value.get(match_id_key)
        if not isinstance(event_id, str) or not event_id:
            raise HarnessError(f"Source record {len(records) + 1} has no string event ID")
        if isinstance(sequence, bool) or not isinstance(sequence, int) or sequence < 0:
            raise HarnessError(f"Source record {len(records) + 1} has no non-negative integer sequence")
        if not isinstance(match_id, str) or not match_id:
            raise HarnessError(f"Source record {len(records) + 1} has no string match ID")
        payload = text[start:end].encode("utf-8")
        records.append(SourceRecord(len(records) + 1, event_id, sequence, match_id, payload))
        if len(records) > max_records:
            raise HarnessError(f"Source exceeds the configured record limit ({max_records})")
        index = _skip_ws(text, end)
        if index < len(text) and text[index] == ",":
            index += 1
            continue
        if index < len(text) and text[index] == "]":
            index = _skip_ws(text, index + 1)
            if index != len(text):
                raise HarnessError("Unexpected data after the top-level JSON array")
            return records
        raise HarnessError("Expected a comma or closing bracket in the source array")
    raise HarnessError("Source array is not closed")


def load_profile(path: Path) -> dict[str, Any]:
    try:
        raw = path.read_bytes()
    except OSError as exc:
        raise HarnessError(f"Cannot read profile: {exc}") from exc
    if len(raw) > MAX_PROFILE_BYTES:
        raise HarnessError("Profile exceeds the 1 MB limit")
    try:
        profile = json.loads(raw)
    except (json.JSONDecodeError, UnicodeDecodeError) as exc:
        raise HarnessError("Profile must be valid UTF-8 JSON") from exc
    if not isinstance(profile, dict) or profile.get("profileSchemaVersion") != SUPPORTED_PROFILE_VERSION:
        raise HarnessError(f"profileSchemaVersion must be {SUPPORTED_PROFILE_VERSION}")
    if profile.get("storyId") != "EP03-S05":
        raise HarnessError("Profile storyId must be EP03-S05")
    source = profile.get("source")
    workload = profile.get("workload")
    if not isinstance(source, dict) or not isinstance(workload, dict):
        raise HarnessError("Profile requires source and workload objects")
    if source.get("format") != "json-array":
        raise HarnessError("Only source format json-array is supported")
    for key in ("eventIdKey", "sequenceKey", "matchIdKey"):
        if not isinstance(source.get(key), str) or not source[key]:
            raise HarnessError(f"source.{key} must be a non-empty string")
    expected_hash = source.get("expectedSha256")
    if not isinstance(expected_hash, str) or len(expected_hash) != 64:
        raise HarnessError("source.expectedSha256 must be a 64-character SHA-256")
    try:
        int(expected_hash, 16)
    except ValueError as exc:
        raise HarnessError("source.expectedSha256 must be hexadecimal") from exc
    if workload.get("mode") != "generator-only-local-in-memory":
        raise HarnessError("Only generator-only-local-in-memory mode is supported")
    if workload.get("partitionMode") != "single-source-match":
        raise HarnessError("Multi-match and custom partition mixes are unsupported")
    if workload.get("payloadMode") != "opaque-source-record-order":
        raise HarnessError("Payload shaping or semantic payload mixes are unsupported")
    if workload.get("replayMode") not in ("cycle-source-records", "stop-at-source-end"):
        raise HarnessError("replayMode must be cycle-source-records or stop-at-source-end")
    rate = workload.get("targetEventsPerSecond")
    if isinstance(rate, bool) or not isinstance(rate, int) or rate < 1 or rate > 100_000:
        raise HarnessError("targetEventsPerSecond must be an integer from 1 to 100000")
    queue_capacity = workload.get("queueCapacityRecords")
    if isinstance(queue_capacity, bool) or not isinstance(queue_capacity, int) or not 1 <= queue_capacity <= 100_000:
        raise HarnessError("queueCapacityRecords must be an integer from 1 to 100000")
    duration = workload.get("durationSeconds")
    if duration is not None:
        if isinstance(duration, bool) or not isinstance(duration, (int, float)):
            raise HarnessError("durationSeconds must be null or a positive number")
        if duration <= 0 or duration > 3600 or (isinstance(duration, float) and not math.isfinite(duration)):
            raise HarnessError("durationSeconds must be null or a finite number from greater than 0 through 3600")
    return profile


def _ordered_identity_digest(records: list[SourceRecord]) -> str:
    digest = sha256()
    for record in records:
        digest.update(record.event_id.encode("utf-8"))
        digest.update(b"\0")
        digest.update(str(record.sequence).encode("ascii"))
        digest.update(b"\n")
    return digest.hexdigest()


def _source_records(profile: dict[str, Any], repo_root: Path) -> tuple[Path, bytes, list[SourceRecord]]:
    source_config = profile["source"]
    repo_root = repo_root.resolve()
    relative_path = Path(source_config["path"])
    if relative_path.is_absolute():
        raise HarnessError("Source path must be repository-relative")
    unresolved_path = repo_root
    for part in relative_path.parts:
        unresolved_path = unresolved_path / part
        if unresolved_path.is_symlink():
            raise HarnessError("Source path and its repository ancestors must not be symbolic links")
    path = unresolved_path.resolve()
    try:
        path.relative_to(repo_root)
    except ValueError as exc:
        raise HarnessError("Source path must remain inside the repository") from exc
    if not path.is_file() or path.is_symlink():
        raise HarnessError("Source must be a regular, non-symlink file")
    max_bytes = source_config.get("maxBytes", 50_000_000)
    max_records = source_config.get("maxRecords", 100_000)
    if isinstance(max_bytes, bool) or not isinstance(max_bytes, int) or not 1 <= max_bytes <= 50_000_000:
        raise HarnessError("source.maxBytes must be from 1 to 50000000")
    if isinstance(max_records, bool) or not isinstance(max_records, int) or not 1 <= max_records <= 100_000:
        raise HarnessError("source.maxRecords must be from 1 to 100000")
    data = path.read_bytes()
    if len(data) > max_bytes:
        raise HarnessError("Source exceeds source.maxBytes")
    digest = sha256(data).hexdigest()
    if digest != source_config["expectedSha256"].lower():
        raise HarnessError("Source SHA-256 does not match the pinned profile")
    records = parse_json_array_records(
        data,
        event_id_key=source_config["eventIdKey"],
        sequence_key=source_config["sequenceKey"],
        match_id_key=source_config["matchIdKey"],
        max_records=max_records,
    )
    if len({record.match_id for record in records}) != 1:
        raise HarnessError("This harness supports only one source match; multi-match partition mixes are unsupported")
    return path, data, records


def _planned_records(rate: int, duration_seconds: float) -> int:
    # Events are scheduled at t=0, 1/rate, ...; ceil preserves the requested
    # target count for fractional rate-duration products.
    return max(1, math.ceil(rate * duration_seconds))


async def run_generator(
    records: list[SourceRecord],
    *,
    rate: int,
    duration_seconds: float,
    queue_capacity: int,
    stop_at_source_end: bool = False,
    clock: Callable[[], float] = time.perf_counter,
    sleep: Callable[[float], Any] = asyncio.sleep,
) -> dict[str, Any]:
    """Rate-schedule unchanged record bytes into a bounded in-memory discard sink."""
    requested = _planned_records(rate, duration_seconds)
    planned = min(requested, len(records)) if stop_at_source_end else requested
    queue: asyncio.Queue[tuple[SourceRecord, int] | None] = asyncio.Queue(maxsize=queue_capacity)
    sink_count = 0
    max_queue_depth = 0
    replayed_identity_digest = sha256()
    replayed_payload_digest = sha256()

    async def sink() -> None:
        nonlocal sink_count
        while True:
            item = await queue.get()
            if item is None:
                queue.task_done()
                return
            record, _cycle = item
            replayed_identity_digest.update(record.event_id.encode("utf-8"))
            replayed_identity_digest.update(b"\0")
            replayed_identity_digest.update(str(record.sequence).encode("ascii"))
            replayed_identity_digest.update(b"\n")
            replayed_payload_digest.update(record.payload)
            replayed_payload_digest.update(b"\n")
            sink_count += 1
            queue.task_done()

    consumer = asyncio.create_task(sink())
    start = clock()
    scheduled = 0
    try:
        for index in range(planned):
            deadline = start + index / rate
            delay = deadline - clock()
            if delay > 0:
                await sleep(delay)
            source_record = records[index % len(records)]
            cycle = index // len(records) + 1
            await queue.put((source_record, cycle))
            scheduled += 1
            max_queue_depth = max(max_queue_depth, queue.qsize())
        scheduled_end = clock()
        await queue.join()
        if stop_at_source_end and scheduled < requested:
            # Honor the requested diagnostic window while making source exhaustion explicit.
            remaining = start + duration_seconds - clock()
            if remaining > 0:
                await sleep(remaining)
    finally:
        await queue.put(None)
        await consumer
    finished = clock()
    source_limited = scheduled < requested
    scheduled_elapsed = duration_seconds if source_limited else max(scheduled_end - start, planned / rate)
    full_elapsed = max(0.0, finished - start)
    return {
        "requestedRecords": requested,
        "plannedRecords": planned,
        "scheduledRecords": scheduled,
        "localSinkReceivedRecords": sink_count,
        "scheduleWindowSeconds": scheduled_elapsed,
        "fullRunSeconds": full_elapsed,
        "scheduledRatePerSecond": (scheduled / scheduled_elapsed) if scheduled_elapsed > 0 else None,
        "localSinkRatePerSecond": (sink_count / full_elapsed) if full_elapsed > 0 else None,
        "maxInMemoryQueueDepth": max_queue_depth,
        "queueCapacityRecords": queue_capacity,
        "sourceRecordLimitReached": source_limited,
        "replayedEventIdSequenceSha256": replayed_identity_digest.hexdigest(),
        "replayedPayloadSha256": replayed_payload_digest.hexdigest(),
        "complete": scheduled == requested and sink_count == requested,
    }


def build_report(
    profile: dict[str, Any],
    source_path: Path,
    source_bytes: bytes,
    records: list[SourceRecord],
    run_metrics: dict[str, Any],
    *,
    duration_override: float | None,
    run_id: str,
    started_at: str,
) -> dict[str, Any]:
    duration = duration_override if duration_override is not None else profile["workload"]["durationSeconds"]
    source_matches = sorted({record.match_id for record in records})
    records_with_positions = 0
    attached_position_objects = 0
    for record in records:
        parsed_record = json.loads(record.payload)
        positions = parsed_record.get("playerPositions")
        if isinstance(positions, list):
            records_with_positions += 1
            attached_position_objects += len(positions)
    return {
        "reportSchemaVersion": "1.0",
        "storyId": "EP03-S05",
        "runId": run_id,
        "startedAtUtc": started_at,
        "status": "generator_only_not_ingestion_evidence",
        "measurementScope": "local_rate_scheduler_and_in_memory_discard_sink",
        "profile": {
            "profileId": profile.get("profileId"),
            "profileSchemaVersion": profile["profileSchemaVersion"],
            "profileAgreement": profile.get("agreement", {}),
            "targetEventsPerSecond": profile["workload"]["targetEventsPerSecond"],
            "durationSeconds": duration,
            "durationOverrideUsed": duration_override is not None,
            "payloadMode": profile["workload"]["payloadMode"],
            "payloadMixApproval": profile["workload"].get("payloadMixApproval"),
            "replayMode": profile["workload"]["replayMode"],
            "partitionMode": profile["workload"]["partitionMode"],
            "partitionMixApproval": profile["workload"].get("partitionMixApproval"),
        },
        "source": {
            "repositoryPath": profile["source"]["path"],
            "sourceVersion": profile["source"].get("sourceVersion"),
            "fixtureVersion": profile["source"].get("fixtureVersion"),
            "sha256": sha256(source_bytes).hexdigest(),
            "byteSize": len(source_bytes),
            "recordCount": len(records),
            "matchCount": len(source_matches),
            "matchIds": source_matches,
            "provenanceLabel": profile["source"].get("provenanceLabel"),
            "provenanceIsEmbeddedInSource": False,
            "orderedEventIdSequenceSha256": _ordered_identity_digest(records),
            "payloadBytesPreservedWithoutReserialization": True,
            "recordsAreReplayedInSourceOrder": True,
            "replayCyclesRepeatOriginalEventIdsAndSequences": profile["workload"]["replayMode"] == "cycle-source-records",
            "sourceExhaustionStopsWithoutRepeating": profile["workload"]["replayMode"] == "stop-at-source-end",
            "attachedPlayerPositionsArePreserved": True,
            "eventRecordsWithPlayerPositionsArray": records_with_positions,
            "attachedPlayerPositionObjectCount": attached_position_objects,
            "standaloneFrameRecords": 0,
        },
        "generatorMeasurements": run_metrics,
        "ingestionMeasurements": {
            "status": "not_measured",
            "acceptedRecords": None,
            "duplicateRecords": None,
            "quarantinedRecords": None,
            "consumerLag": None,
            "databaseLatencyPercentilesMs": None,
            "backpressureRecoverySeconds": None,
        },
        "unsupported": [
            "No broker, event worker, database, API, or Azure endpoint was exercised.",
            "Generator throughput is not ingestion throughput and does not prove zero data loss.",
            "The source contains a single match; multi-match partition mixes are unsupported.",
            "Payloads are opaque source records; semantic payload mixes and payload shaping are unsupported.",
            "Consumer lag, accepted/duplicate/quarantine counts, end-to-end latency, and recovery are unmeasured.",
            "The source clock, coordinates, player metrics, canonical event mapping, and football realism are not validated by this harness.",
        ],
    }


async def execute(profile_path: Path, output_path: Path, duration_override: float | None) -> dict[str, Any]:
    profile = load_profile(profile_path)
    repo_root = Path(__file__).resolve().parents[1]
    duration = duration_override if duration_override is not None else profile["workload"]["durationSeconds"]
    if duration is None:
        raise HarnessError("workload.durationSeconds is unset; obtain an agreed duration or pass --duration-seconds for explicitly generator-only evidence")
    if not math.isfinite(duration) or duration <= 0 or duration > 3600:
        raise HarnessError("Run duration must be greater than 0 and at most 3600 seconds")
    source_path, source_bytes, records = _source_records(profile, repo_root)
    workload = profile["workload"]
    started_at = datetime.now(timezone.utc).isoformat()
    run_id = str(uuid.uuid4())
    metrics = await run_generator(
        records,
        rate=workload["targetEventsPerSecond"],
        duration_seconds=float(duration),
        queue_capacity=workload["queueCapacityRecords"],
        stop_at_source_end=workload["replayMode"] == "stop-at-source-end",
    )
    report = build_report(
        profile,
        source_path,
        source_bytes,
        records,
        metrics,
        duration_override=duration_override,
        run_id=run_id,
        started_at=started_at,
    )
    output_path = output_path.resolve()
    if output_path in (source_path.resolve(), profile_path.resolve()):
        raise HarnessError("Report output must not overwrite the candidate source or load profile")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(report, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", type=Path, default=Path("docs/engineering/performance/ep03-s05-load-profile.json"))
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--duration-seconds", type=float, help="Unapproved override; output remains generator-only evidence")
    args = parser.parse_args(argv)
    try:
        report = asyncio.run(execute(args.profile, args.output, args.duration_seconds))
    except (HarnessError, OSError) as exc:
        print(f"EP03-S05 local generator failed: {exc}", file=sys.stderr)
        return 2
    metrics = report["generatorMeasurements"]
    print(
        f"Generator-only run: scheduled={metrics['scheduledRecords']} "
        f"sinkReceived={metrics['localSinkReceivedRecords']} "
        f"scheduledRatePerSecond={metrics['scheduledRatePerSecond']} "
        f"report={args.output}"
    )
    print("Ingestion acceptance, consumer lag, duplicates, quarantine, and recovery: NOT MEASURED")
    return 0 if metrics["complete"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
