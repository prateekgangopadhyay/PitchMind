#!/usr/bin/env python3
"""Generate a structural, non-authoritative dictionary for the pinned candidate feed."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from collections import Counter
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = ROOT / "data/simulations/manu-liv-match-dataset.json"
DEFAULT_MANIFEST = ROOT / "data/simulations/manu-liv-candidate-manifest.json"
DEFAULT_OUTPUT = ROOT / "docs/data/MANU-LIV-CANDIDATE-FIELD-DICTIONARY.md"


class DictionaryError(ValueError):
    """Raised when the source does not match the pinned structural manifest."""


def _unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DictionaryError(f"Duplicate JSON object key: {key}")
        result[key] = value
    return result


def _json_type(value: Any) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "boolean"
    if isinstance(value, int):
        return "integer"
    if isinstance(value, float):
        return "number"
    if isinstance(value, str):
        return "string"
    if isinstance(value, list):
        return "array"
    if isinstance(value, dict):
        return "object"
    return type(value).__name__


def _safe_range(values: list[Any]) -> str:
    numbers = [value for value in values if isinstance(value, (int, float)) and not isinstance(value, bool)]
    if not numbers:
        return "—"
    if not all(math.isfinite(value) for value in numbers):
        return "contains non-finite value"
    return f"{min(numbers)}..{max(numbers)}"


def _field_rows(objects: list[dict[str, Any]], prefix: str = "") -> list[str]:
    keys = sorted({key for obj in objects for key in obj})
    rows: list[str] = []
    for key in keys:
        values = [obj[key] for obj in objects if key in obj]
        type_counts = Counter(_json_type(value) for value in values)
        types = ", ".join(f"{name} × {count}" for name, count in sorted(type_counts.items()))
        null_count = type_counts.get("null", 0)
        scalar_values = [value for value in values if isinstance(value, (str, int, float, bool)) or value is None]
        distinct = len(set(scalar_values)) if len(scalar_values) == len(values) else None
        observed = _safe_range(values)
        if distinct is not None:
            observed = f"numeric range {observed}; distinct values {distinct}" if observed != "—" else f"distinct values {distinct}"
        rows.append(
            f"| `{prefix}{key}` | {types} | {len(values)}/{len(objects)} | {len(objects) - len(values)} | {null_count} | {observed} |"
        )
    return rows


def _table(rows: list[str]) -> str:
    return "\n".join([
        "| Field path | Observed JSON types | Present / objects | Missing | Null | Structural summary |",
        "|---|---|---:|---:|---:|---|",
        *rows,
    ])


def render_dictionary(source_path: Path, manifest_path: Path) -> str:
    raw = source_path.read_bytes()
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"), object_pairs_hook=_unique_object)
    source_policy = manifest.get("source", {})
    actual_hash = hashlib.sha256(raw).hexdigest()
    if actual_hash != source_policy.get("sha256") or len(raw) != source_policy.get("byteSize"):
        raise DictionaryError("Candidate bytes do not match the source hash and size pinned by the manifest")

    events = json.loads(raw, object_pairs_hook=_unique_object)
    if not isinstance(events, list) or not events or not all(isinstance(event, dict) for event in events):
        raise DictionaryError("Source must be a non-empty JSON array of event objects")
    players = [
        player
        for event in events
        for player in (event.get("playerPositions") if isinstance(event.get("playerPositions"), list) else [])
        if isinstance(player, dict)
    ]
    metadata = [event.get("metadata") for event in events if isinstance(event.get("metadata"), dict)]
    if not players or len(metadata) != len(events):
        raise DictionaryError("Expected event-attached playerPositions arrays and metadata objects")

    event_types = Counter(event.get("eventType") for event in events)
    periods = Counter(event.get("period") for event in events)
    clock_values = [event.get("clockSeconds") for event in events]
    clocks = [value for value in clock_values if isinstance(value, int) and not isinstance(value, bool)]
    clock_order = (
        "nondecreasing"
        if len(clocks) == len(events) and all(left <= right for left, right in zip(clocks, clocks[1:]))
        else "not established as nondecreasing"
    )
    sequences = [event.get("sequence") for event in events if isinstance(event.get("sequence"), int) and not isinstance(event.get("sequence"), bool)]
    frame_sizes = Counter(len(event.get("playerPositions", [])) for event in events if isinstance(event.get("playerPositions"), list))

    def numeric_field(objects: list[dict[str, Any]], key: str) -> list[Any]:
        return [obj[key] for obj in objects if key in obj]

    def summary(objects: list[dict[str, Any]], key: str) -> str:
        values = numeric_field(objects, key)
        return f"{_safe_range(values)} ({sum(value is None for value in values)} null)"

    match_ids = {event.get("matchId") for event in events if event.get("matchId") is not None}
    team_pairs = {
        (item.get("teamId"), item.get("teamName"))
        for item in [*events, *players]
        if item.get("teamId") is not None or item.get("teamName") is not None
    }
    jersey_values = numeric_field(players, "jerseyNumber")

    lines = [
        "# Manchester United–Liverpool candidate feed: structural field dictionary",
        "",
        "**Authority:** repository-derived structural observations only. No provider-authoritative data dictionary was supplied. This document records the exact JSON representation and observed value distributions; it does not certify field meaning, football validity, rights/privacy, or source-to-canonical mapping.",
        "",
        f"- Source: [`{source_path.relative_to(ROOT) if source_path.is_relative_to(ROOT) else source_path.name}`]({source_path.relative_to(ROOT).as_posix() if source_path.is_relative_to(ROOT) else '../data/simulations/manu-liv-match-dataset.json'})",
        f"- Source SHA-256: `{actual_hash}`; bytes: {len(raw):,}; event objects: {len(events):,}.",
        f"- Pinned manifest: [`{manifest_path.relative_to(ROOT) if manifest_path.is_relative_to(ROOT) else manifest_path.name}`]({manifest_path.relative_to(ROOT).as_posix() if manifest_path.is_relative_to(ROOT) else '../../data/simulations/manu-liv-candidate-manifest.json'}); manifest must match source bytes before generation.",
        "- Reproduce: `python3 scripts/generate_manu_liv_field_dictionary.py`.",
        "- The repository owner confirmed synthetic provenance and redistribution permission on 2026-10-08; independent rights/privacy review and football approval remain pending.",
        "",
        "## Event object fields",
        "",
        _table(_field_rows(events)),
        "",
        "## `playerPositions[]` object fields",
        "",
        f"Observed event-attached position objects: {len(players):,}. Event-level array lengths: "
        + ", ".join(f"{size} players × {count} events" for size, count in sorted(frame_sizes.items()))
        + ". These records do not establish a continuous or fixed-rate tracking stream.",
        "",
        _table(_field_rows(players, "playerPositions[].")),
        "",
        "## `metadata` object fields",
        "",
        "Metadata keys are sparse and their values are JSON strings or null in this source, including fields whose names look numeric. Numeric-looking strings are not parsed or assigned units here.",
        "",
        _table(_field_rows(metadata, "metadata.")),
        "",
        "## Observed labels and unresolved meanings",
        "",
        "### Event type counts (source labels; no canonical mapping approved)",
        "",
        "| Source `eventType` | Count |",
        "|---|---:|",
        *[f"| `{name}` | {count} |" for name, count in sorted(event_types.items())],
        "",
        "### Period and chronology observations",
        "",
        "| Field | Structural observation | Semantic status |",
        "|---|---|---|",
        f"| `sequence` | integer values {min(sequences)}..{max(sequences)}; {len(set(sequences)):,} distinct in source order | Preserve the original 1-based value; canonical offset/mapping is not approved |",
        f"| `clockSeconds` | integer values {min(clocks)}..{max(clocks)}; {len(set(clocks)):,} distinct; source order {clock_order} | Unit, origin, elapsed-vs-period basis, stoppage-time policy and marker semantics are unverified; the field name is not authoritative evidence of seconds |",
        f"| `period` | integer values {', '.join(str(value) for value in sorted(periods))}; counts {', '.join(f'{value}: {count}' for value, count in sorted(periods.items()))} | Period labels/boundaries and their relationship to the clock are unverified |",
        "| `eventId` | unique string in every source record | Preserve exact source ID; do not replace with generated canonical identity |",
        f"| `matchId`, `teamId`, `teamName` | {len(match_ids)} distinct non-null match IDs; {len(team_pairs)} distinct observed team ID/name pairs | Source-to-canonical/competition identity mapping is not approved |",
        "",
        "### Coordinates and metrics",
        "",
        "| Field group | Structural observation | Meaning status |",
        "|---|---|---|",
        f"| Event `pitchX/pitchY`, `endX/endY`, `ballX/ballY`; position `x/y` | Observed numeric ranges: event pitchX `{summary(events, 'pitchX')}`, pitchY `{summary(events, 'pitchY')}`, endX `{summary(events, 'endX')}`, endY `{summary(events, 'endY')}`, ballX `{summary(events, 'ballX')}`, ballY `{summary(events, 'ballY')}`; position x `{summary(players, 'x')}`, y `{summary(players, 'y')}` | Unit, pitch dimensions, axis order, origin, orientation, team attack direction, and whether values are percentages are unverified; do not convert to metres or calculate distances |",
        f"| Position `speed`, `distanceCovered`, `heatIndex`, `passAccuracy`, `touches`, `stamina`, `heartRate` | {len(players):,} position objects; per-field presence, JSON type, range and distinct count are listed above | Definitions, units, sampling basis, derivation, validity and biometric/privacy status are unverified; do not present as measured player performance |",
        f"| `position`, `jerseyNumber`, player/team labels | Jersey numeric range `{_safe_range(jersey_values)}` across {len(jersey_values):,} present values; label types and distributions are listed above | Position taxonomy and stable identity semantics are unverified; names and shirt numbers are not canonical IDs |",
        "",
        "## Admission boundary",
        "",
        "This dictionary is not a canonical contract and does not approve a source adapter. Preserve source bytes, event IDs, original sequence, raw clock values, provenance and payload order. Coordinate/time normalization, player metric interpretation, event mappings, score certification, football realism, and independent rights/privacy review remain blocked pending evidence and Product Owner/football review. `candidate_not_canonical` stays the manifest status; canonical event creation remains disabled.",
        "",
    ]
    return "\n".join(lines)


def _ensure_distinct_output(output: Path, inputs: list[Path]) -> None:
    output_resolved = output.resolve()
    for input_path in inputs:
        try:
            if output_resolved == input_path.resolve() or output.exists() and os.path.samefile(output, input_path):
                raise DictionaryError("Output path must not alias the source or manifest")
        except FileNotFoundError:
            continue


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    _ensure_distinct_output(args.output, [args.source, args.manifest])
    document = render_dictionary(args.source, args.manifest)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(document, encoding="utf-8")
    print(f"Wrote structural dictionary for SHA-256 {hashlib.sha256(args.source.read_bytes()).hexdigest()} to {args.output}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, json.JSONDecodeError, DictionaryError) as exc:
        raise SystemExit(f"field dictionary generation failed: {exc}") from exc
