#!/usr/bin/env python3
"""Produce a source-only quality audit without assigning provider semantics.

This report measures fields and relationships in the pinned candidate. It does
not normalize coordinates/time, infer player IDs, map event types, or certify
football realism or canonical truth.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from hashlib import sha256
import json
import math
from pathlib import Path
import re
import statistics
import sys
from typing import Any

METRICS = (
    "speed", "distanceCovered", "heatIndex", "passAccuracy", "touches", "stamina", "heartRate"
)
EVENT_COORDINATES = ("pitchX", "pitchY", "ballX", "ballY", "endX", "endY")
CANONICAL_ENUM = "src/PitchMind.Domain/Events/MatchEvent.cs"
ENUM_PATTERN = re.compile(r"public\s+enum\s+MatchEventType\s*\{([^}]*)\}")
SCORE_PATTERN = re.compile(r"^(\d+)-(\d+)$")


class AuditError(ValueError):
    """The pinned source or audit inputs are invalid."""


def _aliases_existing_file(left: Path, right: Path) -> bool:
    """Detect resolved-path and hard-link aliases to a protected input."""
    try:
        if left.resolve() == right.resolve():
            return True
        return left.exists() and right.exists() and left.samefile(right)
    except OSError as exc:
        raise AuditError("Cannot safely verify report output path") from exc


def _is_number(value: Any) -> bool:
    if not isinstance(value, (int, float)) or isinstance(value, bool):
        return False
    try:
        return math.isfinite(value)
    except (OverflowError, TypeError):
        return False


def _range_profile(values: list[Any], *, coordinate_interval: bool = False) -> dict[str, Any]:
    numeric = [value for value in values if _is_number(value)]
    result = {
        "numericCount": len(numeric),
        "nonNumericOrMissingCount": len(values) - len(numeric),
        "minimum": min(numeric) if numeric else None,
        "maximum": max(numeric) if numeric else None,
        "distinctNumericValueCount": len(set(numeric)),
        "unit": "unverified",
    }
    if coordinate_interval:
        result["outsideZeroToHundredCount"] = sum(value < 0 or value > 100 for value in numeric)
    return result


def _domain_event_types(source: str) -> list[str]:
    match = ENUM_PATTERN.search(source)
    if not match:
        raise AuditError("Could not find MatchEventType enum in the supplied domain source")
    names = [name.strip() for name in match.group(1).split(",") if name.strip()]
    if not names or len(set(names)) != len(names):
        raise AuditError("MatchEventType enum is empty or has duplicate names")
    return names


def audit_data(data: bytes, manifest: dict[str, Any], domain_source: str) -> dict[str, Any]:
    source_policy = manifest.get("source")
    if not isinstance(source_policy, dict):
        raise AuditError("Manifest source policy is missing")
    observed_hash = sha256(data).hexdigest()
    if len(data) != source_policy.get("byteSize") or observed_hash != source_policy.get("sha256"):
        raise AuditError("Source bytes do not match manifest size and SHA-256")
    try:
        events = json.loads(data.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AuditError("Candidate source is not UTF-8 JSON") from exc
    if not isinstance(events, list) or not events or any(not isinstance(event, dict) for event in events):
        raise AuditError("Candidate source root must be a non-empty array of event objects")
    if len(events) != source_policy.get("expectedEventCount"):
        raise AuditError("Source event count does not match manifest")

    event_counts = Counter(event.get("eventType") for event in events)
    team_event_counts = Counter((event.get("teamName"), event.get("eventType")) for event in events)
    periods = Counter(event.get("period") for event in events)
    clocks = [event.get("clockSeconds") for event in events]
    sequences = [event.get("sequence") for event in events]
    clock_deltas = [right - left for left, right in zip(clocks, clocks[1:])
                    if _is_number(left) and _is_number(right)]
    clock_regressions = sum(delta < 0 for delta in clock_deltas)
    equal_clock_pairs = sum(delta == 0 for delta in clock_deltas)

    coordinate_values: dict[str, list[Any]] = {
        key: [event.get(key) for event in events] for key in EVENT_COORDINATES
    }
    coordinate_values["playerX"] = [position.get("x") for event in events
                                     for position in event.get("playerPositions", [])
                                     if isinstance(position, dict)]
    coordinate_values["playerY"] = [position.get("y") for event in events
                                     for position in event.get("playerPositions", [])
                                     if isinstance(position, dict)]
    coordinates = {key: _range_profile(values, coordinate_interval=True)
                   for key, values in coordinate_values.items()}

    source_players: dict[tuple[Any, Any, Any], dict[str, list[Any]]] = defaultdict(
        lambda: {metric: [] for metric in METRICS}
    )
    source_identities_by_team: dict[str, set[tuple[Any, Any, Any]]] = defaultdict(set)
    frame_sizes = Counter()
    team_frame_sizes: dict[str, Counter[int]] = defaultdict(Counter)
    position_count = 0
    for event in events:
        positions = event.get("playerPositions", [])
        if not isinstance(positions, list):
            raise AuditError("Candidate has a non-array playerPositions value")
        frame_sizes[len(positions)] += 1
        by_team = Counter()
        for position in positions:
            if not isinstance(position, dict):
                raise AuditError("Candidate has a non-object position row")
            team_id = position.get("teamId")
            team_name = position.get("teamName")
            key = (team_id, position.get("playerName"), position.get("jerseyNumber"))
            source_players[key]
            source_identities_by_team[str(team_name)].add(key)
            by_team[str(team_name)] += 1
            position_count += 1
            for metric in METRICS:
                source_players[key][metric].append(position.get(metric))
        for team_name, count in by_team.items():
            team_frame_sizes[team_name][count] += 1

    metrics: dict[str, Any] = {}
    for metric in METRICS:
        values = [position.get(metric) for event in events
                  for position in event.get("playerPositions", []) if isinstance(position, dict)]
        per_identity = [record[metric] for record in source_players.values()]
        numeric_series = [series for series in per_identity if all(_is_number(value) for value in series)]
        varying = sum(len(set(series)) > 1 for series in numeric_series)
        adjacent_decreases = sum(
            sum(right < left for left, right in zip(series, series[1:]))
            for series in numeric_series
        )
        profile = _range_profile(values)
        profile.update({
            "sourceCompositeIdentityCount": len(source_players),
            "identitiesWithChangingValues": varying,
            "identitiesWithConstantValues": len(numeric_series) - varying,
            "identitiesWithNonNumericObservations": len(source_players) - len(numeric_series),
            "adjacentDecreasesInSourceOrder": adjacent_decreases,
        })
        metrics[metric] = profile

    marker_rows = []
    for index, event in enumerate(events):
        if event.get("eventType") in {"KickOff", "HalfTime", "FullTime", "Substitution", "RedCard"}:
            marker_rows.append({
                "sequence": event.get("sequence"),
                "period": event.get("period"),
                "clockSeconds": event.get("clockSeconds"),
                "eventType": event.get("eventType"),
                "teamName": event.get("teamName"),
                "index": index,
            })

    goals = []
    goal_score_values: list[tuple[int, int] | None] = []
    for event in events:
        if event.get("eventType") != "Goal":
            continue
        metadata = event.get("metadata") if isinstance(event.get("metadata"), dict) else {}
        score_text = metadata.get("score")
        score_match = SCORE_PATTERN.fullmatch(score_text) if isinstance(score_text, str) else None
        score_pair = tuple(map(int, score_match.groups())) if score_match else None
        goal_score_values.append(score_pair)
        goals.append({
            "sequence": event.get("sequence"),
            "clockSeconds": event.get("clockSeconds"),
            "eventTeamMatchesMetadataTeam": event.get("teamName") == metadata.get("teamName"),
            "eventPlayerMatchesMetadataPlayer": event.get("playerName") == metadata.get("playerName"),
            "eventOutcomeMatchesMetadataOutcome": event.get("outcome") == metadata.get("outcome"),
            "metadataScore": score_text if score_match else None,
        })
    one_step_score_transitions = sum(
        left is not None and right is not None
        and right[0] >= left[0] and right[1] >= left[1]
        and (right[0] - left[0]) + (right[1] - left[1]) == 1
        for left, right in zip(goal_score_values, goal_score_values[1:])
    )
    full_time = next((event for event in reversed(events) if event.get("eventType") == "FullTime"), None)
    full_time_meta = full_time.get("metadata") if full_time and isinstance(full_time.get("metadata"), dict) else {}

    substitutions = []
    red_cards = []
    for index, event in enumerate(events):
        event_type = event.get("eventType")
        if event_type not in {"Substitution", "RedCard"}:
            continue
        metadata = event.get("metadata") if isinstance(event.get("metadata"), dict) else {}
        team_id = event.get("teamId")
        incoming, outgoing = metadata.get("player_in"), metadata.get("player_out")
        participant = event.get("playerName") or metadata.get("playerName")
        snapshots = {}
        for label, snapshot_index in (("before", index - 1), ("atEvent", index), ("after", index + 1)):
            snapshot = events[snapshot_index].get("playerPositions", []) if 0 <= snapshot_index < len(events) else []
            team_rows = [position for position in snapshot if position.get("teamId") == team_id]
            names = {position.get("playerName") for position in team_rows}
            if event_type == "Substitution":
                snapshots[label] = {
                    "teamPositionCount": len(team_rows),
                    "incomingLabelPresent": incoming in names if isinstance(incoming, str) else None,
                    "outgoingLabelPresent": outgoing in names if isinstance(outgoing, str) else None,
                }
            else:
                snapshots[label] = {
                    "teamPositionCount": len(team_rows),
                    "eventParticipantLabelPresent": participant in names if isinstance(participant, str) else None,
                }
        record = {"sequence": event.get("sequence"), "teamName": event.get("teamName"), "snapshots": snapshots}
        if event_type == "Substitution":
            substitutions.append(record)
        else:
            red_cards.append(record)

    domain_types = _domain_event_types(domain_source)
    source_types = sorted(event_counts)
    source_set, domain_set = set(source_types), set(domain_types)
    canonicalization_policy = manifest.get("canonicalization", {})
    coordinates_policy = manifest.get("coordinates", {})
    metrics_policy = manifest.get("playerMetrics", {})
    team_names = sorted({event.get("teamName") for event in events})
    clock_value_span = clocks[-1] - clocks[0] if len(clocks) > 1 else 0

    return {
        "auditVersion": "1.0.0",
        "source": {
            "path": source_policy.get("path"),
            "manifestVersion": manifest.get("manifestVersion"),
            "sha256": observed_hash,
            "byteSize": len(data),
            "status": manifest.get("status"),
        },
        "coordinateAudit": {
            "manifestUnit": coordinates_policy.get("unit"),
            "manifestAxisConvention": coordinates_policy.get("axisConvention"),
            "manifestPitchDimensionsMetres": coordinates_policy.get("pitchDimensionsMetres"),
            "manifestStatus": coordinates_policy.get("status"),
            "rawFieldRanges": coordinates,
            "interpretation": "Numeric ranges only; units, axis, origin, orientation, and pitch dimensions are unverified.",
        },
        "playerMetricAudit": {
            "manifestStatus": metrics_policy.get("status"),
            "manifestUnitsAndValidity": metrics_policy.get("unitsAndValidity"),
            "mayBePresentedAsObservedPerformance": metrics_policy.get("mayBePresentedAsObservedPerformance"),
            "sourceCompositeIdentitiesByTeam": {
                team: len(identities) for team, identities in sorted(source_identities_by_team.items())
            },
            "positionObservationCount": position_count,
            "fields": metrics,
            "interpretation": "Per-source-key variation and source-order changes are descriptive only; metric meaning, units, window, sampling, and validity are unknown.",
        },
        "chronology": {
            "eventCount": len(events),
            "checksAreCandidateStructuralOnly": manifest.get("match", {}).get("clockAndPeriodChecks") == "candidate-file-structural-regression-only",
            "sequenceStart": sequences[0],
            "sequenceEnd": sequences[-1],
            "sequenceContiguousFromManifestStart": sequences == list(range(
                manifest.get("source", {}).get("sequenceStart", sequences[0]),
                manifest.get("source", {}).get("sequenceStart", sequences[0]) + len(sequences))),
            "periodCounts": {str(period): periods[period] for period in sorted(periods)},
            "clockUnitDeclaredByManifest": manifest.get("match", {}).get("sourceClockUnit"),
            "clockUnitStatusByManifest": manifest.get("match", {}).get("sourceClockUnitStatus"),
            "clockStart": clocks[0],
            "clockEnd": clocks[-1],
            "clockRegressions": clock_regressions,
            "adjacentEqualClockPairs": equal_clock_pairs,
            "medianAdjacentClockDelta": statistics.median(clock_deltas) if clock_deltas else None,
            "clockValueSpan": clock_value_span,
            "markers": [{key: value for key, value in row.items() if key != "index"} for row in marker_rows],
            "interpretation": "Source-order and clock observations only; provider clock policy, tie semantics, and period meaning are not established.",
        },
        "footballEventAudit": {
            "sourceEventCounts": {key: event_counts[key] for key in sorted(event_counts)},
            "teamEventCounts": {
                f"{team}|{event_type}": count
                for (team, event_type), count in sorted(team_event_counts.items())
            },
            "frameSizeCounts": {str(key): frame_sizes[key] for key in sorted(frame_sizes)},
            "teamPositionCountsPerFrame": {
                team: {str(count): distribution[count] for count in sorted(distribution)}
                for team, distribution in sorted(team_frame_sizes.items())
            },
            "goalRows": goals,
            "goalScoreSnapshotsParseable": sum(value is not None for value in goal_score_values),
            "goalScoreSnapshotsUnparseable": sum(value is None for value in goal_score_values),
            "adjacentGoalScoreTransitionsWithExactlyOneNondecreasingComponentIncrement": one_step_score_transitions,
            "fullTimeMetadataScore": full_time_meta.get("score"),
            "fullTimeMetadataResult": full_time_meta.get("result"),
            "substitutionFrameSnapshots": substitutions,
            "redCardFrameSnapshots": red_cards,
            "interpretation": "Counts and cross-field comparisons only; event definitions, score-side order, adjudication, and football realism require source documentation and football review.",
        },
        "eventTypeComparison": {
            "sourceTypes": source_types,
            "currentDomainEnumTypes": domain_types,
            "exactNameOverlapOnly": sorted(source_set & domain_set),
            "sourceTypesWithoutExactNameMatch": sorted(source_set - domain_set),
            "domainTypesWithoutExactSourceName": sorted(domain_set - source_set),
            "manifestMappingStatus": canonicalization_policy.get("mappingStatus"),
            "interpretation": "No semantic event mappings are inferred by this audit.",
        },
        "unresolvedQuestions": [
            "Which physical unit, pitch dimensions, coordinate origin, axis direction, and attack orientation apply to every coordinate field? Does orientation change by period?",
            "Are coordinates positions at event time, event start/end locations, or another sample? What are frame cadence and missing-value semantics?",
            "What are the units, formulas, observation windows, and valid ranges for speed, distanceCovered, heatIndex, passAccuracy, touches, stamina, and heartRate? Which are cumulative, per-event, or simulated labels?",
            "What does clockSeconds represent across periods, added time, repeated timestamps, half-time, and the 5761 clock-value full-time marker?",
            "What are the provider definitions and qualifiers for each of the 19 event types, including shot, foul, pressure, progressive/line-breaking actions, recovery, loss, cards, and substitutions?",
            "How are score components ordered by team, and how are own goals, disallowed goals, corrections, or scorer changes represented?",
            "Are the source name/jersey pairs stable player identities, and what roster/substitution/card rules produced the 12 source labels per team?",
            "Which source event types, if any, have approved versioned mappings to the current domain enum?",
        ],
        "canonicalizationStatus": "blocked_pending_source_semantics_and_football_review",
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("data/simulations/manu-liv-match-dataset.json"))
    parser.add_argument("--manifest", type=Path, default=Path("data/simulations/manu-liv-candidate-manifest.json"))
    parser.add_argument("--domain-source", type=Path, default=Path(CANONICAL_ENUM))
    parser.add_argument("--output", type=Path, help="Write JSON report here; stdout when omitted")
    args = parser.parse_args(argv)
    try:
        if args.output and (
            _aliases_existing_file(args.output, args.source)
            or _aliases_existing_file(args.output, args.manifest)
        ):
            raise AuditError("Report output must not overwrite the source or manifest")
        manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
        result = audit_data(args.source.read_bytes(), manifest, args.domain_source.read_text(encoding="utf-8"))
        rendered = json.dumps(result, indent=2, ensure_ascii=False, sort_keys=True) + "\n"
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(rendered, encoding="utf-8")
        else:
            sys.stdout.write(rendered)
    except (OSError, json.JSONDecodeError, AuditError) as exc:
        print(f"Candidate quality audit failed: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
