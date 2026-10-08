#!/usr/bin/env python3
"""Validate source conservation and structural quality for the candidate fixture.

This is not a canonical football importer. It never maps source event kinds,
normalizes match time or coordinates, or treats player metrics as observed
performance. The emitted report contains aggregate metadata only.
"""
from __future__ import annotations

import argparse
from collections import Counter
from hashlib import sha256
import json
import math
from pathlib import Path
import sys
from typing import Any
import uuid

MAX_SOURCE_BYTES = 50_000_000
MAX_EVENTS = 100_000
MAX_POSITIONS_PER_EVENT = 64
PLAYER_METRIC_FIELDS = (
    "speed", "distanceCovered", "heatIndex", "passAccuracy", "stamina", "heartRate", "touches"
)
EVENT_REQUIRED = (
    "eventId", "matchId", "sequence", "clockSeconds", "period", "eventType", "teamId", "teamName",
    "outcome", "metadata", "playerPositions", "pitchX", "pitchY", "ballX", "ballY", "endX", "endY",
    "playerName", "receiverName"
)
POSITION_REQUIRED = (
    "playerName", "teamName", "teamId", "jerseyNumber", "position", "x", "y", *PLAYER_METRIC_FIELDS
)


class CandidateValidationError(ValueError):
    """A source-integrity or structural check failed."""


def _reject_constant(value: str) -> None:
    raise CandidateValidationError(f"Non-finite JSON constant {value} is not accepted")


def _number(value: Any) -> bool:
    if not isinstance(value, (int, float)) or isinstance(value, bool):
        return False
    try:
        return math.isfinite(value)
    except (OverflowError, TypeError):
        return False


def _stable_json(value: Any) -> bytes:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")


def validate_source(data: bytes, manifest: dict[str, Any]) -> dict[str, Any]:
    """Validate a manifest-pinned source without altering or canonicalizing records."""
    source = manifest.get("source", {})
    match_policy = manifest.get("match", {})
    if not isinstance(source, dict) or not isinstance(match_policy, dict):
        raise CandidateValidationError("Manifest source and match policies must be objects")
    required_manifest_values = (
        source.get("byteSize"), source.get("sha256"), source.get("expectedEventCount"),
        source.get("sequenceStart"), source.get("sequenceEnd"), match_policy.get("expectedMatchCount"),
        match_policy.get("expectedTeamCount"), match_policy.get("sourceClockUnit"),
        match_policy.get("expectedPeriods"), match_policy.get("sourceClockStart"),
        match_policy.get("sourceClockEnd"),
    )
    if any(value is None for value in required_manifest_values):
        raise CandidateValidationError("Manifest is missing required source or match integrity fields")
    expected_bytes = source.get("byteSize")
    if len(data) == 0 or len(data) > MAX_SOURCE_BYTES:
        raise CandidateValidationError("Source is empty or exceeds the configured size limit")
    source_hash = sha256(data).hexdigest()
    if len(data) != expected_bytes or source_hash != source.get("sha256"):
        raise CandidateValidationError("Source byte size or SHA-256 does not match the pinned manifest")
    if source.get("synthetic") is not True or source.get("provenance") != "repository-owner-confirmed":
        raise CandidateValidationError("Manifest must retain the owner-confirmed synthetic provenance declaration")
    if source.get("redistributionPermission") != "repository-owner-confirmed":
        raise CandidateValidationError("Manifest must retain the owner-confirmed redistribution declaration")
    if manifest.get("status") != "candidate_not_canonical":
        raise CandidateValidationError("Candidate manifest must not claim canonical approval")
    coordinate_policy = manifest.get("coordinates", {})
    if (not isinstance(coordinate_policy, dict) or coordinate_policy.get("status") != "unverified"
            or coordinate_policy.get("unit") is not None or coordinate_policy.get("axisConvention") is not None
            or coordinate_policy.get("pitchDimensionsMetres") is not None):
        raise CandidateValidationError("Coordinate semantics must remain explicitly unverified")
    metric_policy = manifest.get("playerMetrics", {})
    if (not isinstance(metric_policy, dict) or metric_policy.get("status") != "unvalidated"
            or metric_policy.get("unitsAndValidity") != "unverified"
            or metric_policy.get("mayBePresentedAsObservedPerformance") is not False):
        raise CandidateValidationError("Player metrics must remain explicitly unvalidated")
    canonicalization = manifest.get("canonicalization", {})
    if (not isinstance(canonicalization, dict) or canonicalization.get("mappingStatus") != "not_approved"
            or canonicalization.get("mayCreateCanonicalEvents") is not False):
        raise CandidateValidationError("Canonical event mappings must remain unapproved")

    try:
        events = json.loads(data.decode("utf-8-sig"), parse_constant=_reject_constant)
    except (UnicodeDecodeError, json.JSONDecodeError, RecursionError) as exc:
        raise CandidateValidationError("Source must be a UTF-8 JSON array of event records") from exc
    if not isinstance(events, list) or not events or len(events) > MAX_EVENTS:
        raise CandidateValidationError("Source root must be a non-empty event array within the event limit")
    if len(events) != source.get("expectedEventCount"):
        raise CandidateValidationError("Source event count differs from the pinned manifest")

    seen_event_ids: set[str] = set()
    match_ids: set[str] = set()
    team_names_by_id: dict[str, str] = {}
    team_ids_by_name: dict[str, str] = {}
    position_team_names_by_id: dict[str, str] = {}
    position_team_ids_by_name: dict[str, str] = {}
    event_counts: Counter[str] = Counter()
    periods: Counter[int] = Counter()
    coordinate_ranges = {key: [math.inf, -math.inf, 0] for key in ("pitchX", "pitchY", "ballX", "ballY", "endX", "endY", "playerX", "playerY")}
    metric_counts: Counter[str] = Counter()
    position_count = 0
    previous_clock = -1
    previous_period = 0
    canonical_ledger = bytearray()
    source_ids_ledger = bytearray()

    def record_coordinate(name: str, value: Any) -> None:
        if value is None:
            return
        if not _number(value):
            raise CandidateValidationError(f"Non-numeric source coordinate: {name}")
        current = coordinate_ranges[name]
        current[0] = min(current[0], float(value))
        current[1] = max(current[1], float(value))
        current[2] += 1

    for index, event in enumerate(events):
        if not isinstance(event, dict):
            raise CandidateValidationError(f"Event at zero-based source index {index} is not an object")
        missing = [key for key in EVENT_REQUIRED if key not in event]
        if missing:
            raise CandidateValidationError(f"Event at zero-based source index {index} lacks required fields")
        event_id = event["eventId"]
        match_id = event["matchId"]
        if not isinstance(event_id, str) or not isinstance(match_id, str):
            raise CandidateValidationError(f"Event identifier is not a string at source index {index}")
        try:
            uuid.UUID(event_id)
            uuid.UUID(match_id)
        except ValueError as exc:
            raise CandidateValidationError(f"Invalid source UUID at source index {index}") from exc
        if event_id in seen_event_ids:
            raise CandidateValidationError(f"Duplicate source event ID at source index {index}")
        seen_event_ids.add(event_id)
        match_ids.add(match_id)

        sequence = event["sequence"]
        if isinstance(sequence, bool) or sequence != index + source.get("sequenceStart", 1):
            raise CandidateValidationError(f"Source sequence is not contiguous at zero-based index {index}")
        clock = event["clockSeconds"]
        period = event["period"]
        if isinstance(clock, bool) or not isinstance(clock, int) or clock < previous_clock:
            raise CandidateValidationError(f"Source clock is not an ordered integer at source index {index}")
        if isinstance(period, bool) or not isinstance(period, int) or period not in match_policy.get("expectedPeriods", []):
            raise CandidateValidationError(f"Unsupported source period at source index {index}")
        if period < previous_period:
            raise CandidateValidationError(f"Source period regressed at source index {index}")
        previous_clock = clock
        previous_period = period
        event_type = event["eventType"]
        if not isinstance(event_type, str) or not event_type.strip():
            raise CandidateValidationError(f"Missing source event kind at source index {index}")
        event_counts[event_type] += 1
        periods[period] += 1
        canonical_ledger.extend(_stable_json({"eventId": event_id, "sequence": sequence, "clockSeconds": clock,
                                              "period": period, "eventType": event_type}))
        source_ids_ledger.extend(event_id.encode("utf-8") + b"\n")

        team_id, team_name = event["teamId"], event["teamName"]
        if not isinstance(team_id, str) or not team_id.strip() or not isinstance(team_name, str) or not team_name.strip():
            raise CandidateValidationError(f"Missing source team identity at source index {index}")
        if team_id in team_names_by_id and team_names_by_id[team_id] != team_name:
            raise CandidateValidationError(f"Source team ID maps to multiple names at source index {index}")
        if team_name in team_ids_by_name and team_ids_by_name[team_name] != team_id:
            raise CandidateValidationError(f"Source team name maps to multiple IDs at source index {index}")
        team_names_by_id[team_id] = team_name
        team_ids_by_name[team_name] = team_id

        if not isinstance(event["metadata"], dict) or not isinstance(event["playerPositions"], list):
            raise CandidateValidationError(f"Malformed metadata or position frame at source index {index}")
        if not isinstance(event["outcome"], str) or any(
            event[key] is not None and not isinstance(event[key], str) for key in ("playerName", "receiverName")
        ):
            raise CandidateValidationError(f"Malformed source event attributes at source index {index}")
        if len(event["playerPositions"]) > MAX_POSITIONS_PER_EVENT:
            raise CandidateValidationError(f"Position frame exceeds the configured limit at source index {index}")
        for name in ("pitchX", "pitchY", "ballX", "ballY", "endX", "endY"):
            record_coordinate(name, event.get(name))

        position_names: set[tuple[str, int]] = set()
        for position_index, position in enumerate(event["playerPositions"]):
            if not isinstance(position, dict) or any(key not in position for key in POSITION_REQUIRED):
                raise CandidateValidationError(f"Malformed position record at event {index}, position {position_index}")
            position_team_id, position_team_name = position["teamId"], position["teamName"]
            if not isinstance(position_team_id, str) or not position_team_id.strip() or not isinstance(position_team_name, str) or not position_team_name.strip():
                raise CandidateValidationError(f"Missing position team identity at event {index}, position {position_index}")
            if position_team_id in position_team_names_by_id and position_team_names_by_id[position_team_id] != position_team_name:
                raise CandidateValidationError(f"Position team ID maps to multiple names at event {index}, position {position_index}")
            if position_team_name in position_team_ids_by_name and position_team_ids_by_name[position_team_name] != position_team_id:
                raise CandidateValidationError(f"Position team name maps to multiple IDs at event {index}, position {position_index}")
            position_team_names_by_id[position_team_id] = position_team_name
            position_team_ids_by_name[position_team_name] = position_team_id
            if not isinstance(position["playerName"], str) or not position["playerName"].strip():
                raise CandidateValidationError(f"Missing source player label at event {index}, position {position_index}")
            if not isinstance(position["position"], str) or not position["position"].strip():
                raise CandidateValidationError(f"Missing source position label at event {index}, position {position_index}")
            jersey = position["jerseyNumber"]
            if isinstance(jersey, bool) or not isinstance(jersey, int):
                raise CandidateValidationError(f"Invalid jersey attribute at event {index}, position {position_index}")
            player_key = (position["playerName"], jersey)
            if player_key in position_names:
                raise CandidateValidationError(f"Duplicate player row in frame at event {index}")
            position_names.add(player_key)
            for axis in ("x", "y"):
                value = position[axis]
                if not _number(value):
                    raise CandidateValidationError(f"Invalid raw player coordinate at event {index}, position {position_index}")
                record_coordinate("playerX" if axis == "x" else "playerY", value)
            for metric in PLAYER_METRIC_FIELDS:
                if not _number(position[metric]):
                    raise CandidateValidationError(f"Invalid raw player metric type at event {index}, position {position_index}")
                metric_counts[metric] += 1
            position_count += 1

    if len(match_ids) != match_policy.get("expectedMatchCount"):
        raise CandidateValidationError("Distinct match count differs from manifest")
    if len(team_names_by_id) != match_policy.get("expectedTeamCount"):
        raise CandidateValidationError("Distinct team count differs from manifest")
    if position_team_names_by_id != team_names_by_id:
        raise CandidateValidationError("Position frame team identities do not reconcile to event team identities")
    if sorted(periods) != sorted(match_policy.get("expectedPeriods", [])):
        raise CandidateValidationError("Observed period set differs from manifest")
    expected_markers = manifest.get("match", {}).get("requiredEventCounts", {})
    if any(event_counts.get(event_type, 0) != count for event_type, count in expected_markers.items()):
        raise CandidateValidationError("Required period-end source markers differ from manifest")
    if events[-1]["eventType"] != "FullTime":
        raise CandidateValidationError("Final source record must retain the full-time marker")
    if events[0]["clockSeconds"] != match_policy.get("sourceClockStart") or events[-1]["clockSeconds"] != match_policy.get("sourceClockEnd"):
        raise CandidateValidationError("Source clock boundaries differ from manifest")
    if events[0]["sequence"] != source.get("sequenceStart") or events[-1]["sequence"] != source.get("sequenceEnd"):
        raise CandidateValidationError("Source sequence boundaries differ from manifest")
    declared_types = set(manifest.get("canonicalization", {}).get("sourceEventTypes", []))
    if set(event_counts) != declared_types:
        raise CandidateValidationError("Observed source event vocabulary differs from manifest")

    def compact_ranges() -> dict[str, dict[str, float | int | None]]:
        result: dict[str, dict[str, float | int | None]] = {}
        for key, (minimum, maximum, count) in coordinate_ranges.items():
            result[key] = {"min": minimum if count else None, "max": maximum if count else None, "count": count}
        return result

    return {
        "status": "structurally_valid_candidate_blocked_for_canonicalization",
        "fixtureId": manifest.get("fixtureId"),
        "manifestVersion": manifest.get("manifestVersion"),
        "sourceSha256": source_hash,
        "sourceByteSize": len(data),
        "eventCount": len(events),
        "matchCount": len(match_ids),
        "teamCount": len(team_names_by_id),
        "sourceSequence": {"first": events[0]["sequence"], "last": events[-1]["sequence"], "preserved": True},
        "sourceClock": {"unit": match_policy["sourceClockUnit"], "first": events[0]["clockSeconds"],
                        "last": events[-1]["clockSeconds"], "preservedWithoutNormalization": True},
        "sourceEventIdLedgerSha256": sha256(bytes(source_ids_ledger)).hexdigest(),
        "orderedEventSemanticsSha256": sha256(bytes(canonical_ledger)).hexdigest(),
        "eventCounts": dict(sorted(event_counts.items())),
        "periodCounts": {str(key): periods[key] for key in sorted(periods)},
        "attachedPositionCount": position_count,
        "playerMetricObservationCounts": dict(sorted(metric_counts.items())),
        "rawCoordinateRanges": compact_ranges(),
        "coordinateUnitsVerified": False,
        "playerMetricsValidated": False,
        "canonicalMappingsApproved": False,
        "footballReviewRequired": True,
        "independentRightsPrivacyReview": source.get("independentRightsPrivacyReview"),
        "warnings": [
            "Structural checks do not validate football realism or canonical event semantics.",
            "Coordinate ranges are raw source values; units, pitch dimensions and orientation remain unverified.",
            "Player metric values and units are unvalidated and must not be presented as observed performance.",
            "Event IDs, sequence, period and source clock are reported without canonical remapping.",
            "Candidate data is not admitted to canonical PostgreSQL or production match APIs."
        ]
    }


def validate_file(source_path: Path, manifest_path: Path) -> dict[str, Any]:
    if source_path.is_symlink() or not source_path.is_file():
        raise CandidateValidationError("Source must be an existing regular file, not a symlink")
    if manifest_path.is_symlink() or not manifest_path.is_file():
        raise CandidateValidationError("Manifest must be an existing regular file, not a symlink")
    if source_path.stat().st_size > MAX_SOURCE_BYTES:
        raise CandidateValidationError("Source exceeds the configured size limit")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"), parse_constant=_reject_constant)
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise CandidateValidationError("Manifest must be valid UTF-8 JSON") from exc
    if not isinstance(manifest, dict):
        raise CandidateValidationError("Manifest root must be an object")
    result = validate_source(source_path.read_bytes(), manifest)
    result["sourceFileName"] = source_path.name
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate EP03-S01 candidate source structure without canonical import")
    parser.add_argument("source", type=Path, nargs="?", default=Path("data/simulations/manu-liv-match-dataset.json"))
    parser.add_argument("--manifest", type=Path, default=Path("data/simulations/manu-liv-candidate-manifest.json"))
    parser.add_argument("--output", type=Path, help="Optional aggregate JSON report path")
    args = parser.parse_args(argv)
    try:
        report = validate_file(args.source, args.manifest)
        serialized = json.dumps(report, ensure_ascii=True, sort_keys=True, indent=2) + "\n"
        if args.output:
            if args.output.resolve() in {args.source.resolve(), args.manifest.resolve()}:
                raise CandidateValidationError("Report output must not overwrite the source or manifest")
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(serialized, encoding="utf-8")
        else:
            sys.stdout.write(serialized)
    except (CandidateValidationError, OSError) as exc:
        print(f"Candidate source validation failed: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
