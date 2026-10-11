#!/usr/bin/env python3
"""Generate a structural, source-only dictionary for the pinned MANU-LIV JSON.

The script checks manifest byte size and SHA-256, then emits observed JSON
field types, absent/null counts, numeric ranges, and cardinalities. It does not
normalize or canonicalize football data.
"""
from __future__ import annotations

import argparse
from collections import Counter
from hashlib import sha256
import json
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = ROOT / "data/simulations/manu-liv-match-dataset.json"
DEFAULT_MANIFEST = ROOT / "data/simulations/manu-liv-candidate-manifest.json"
DEFAULT_OUTPUT = ROOT / "docs/data/MANU-LIV-OBSERVED-DATA-DICTIONARY.md"


def json_type(value: Any) -> str:
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
    return "unknown"


def profile_fields(rows: list[dict[str, Any]]) -> dict[str, dict[str, Any]]:
    names = sorted({name for row in rows for name in row})
    result: dict[str, dict[str, Any]] = {}
    for name in names:
        present = [row[name] for row in rows if name in row]
        nonnull = [value for value in present if value is not None]
        numeric = [value for value in nonnull
                   if isinstance(value, (int, float)) and not isinstance(value, bool)]
        result[name] = {
            "types": dict(sorted(Counter(json_type(value) for value in present).items())),
            "missing": len(rows) - len(present),
            "null": sum(value is None for value in present),
            "distinct": len({json.dumps(value, ensure_ascii=False, sort_keys=True,
                                        separators=(",", ":")) for value in nonnull}),
            "minimum": min(numeric) if numeric else None,
            "maximum": max(numeric) if numeric else None,
        }
    return result


def cells(profile: dict[str, Any]) -> str:
    types = ", ".join(f"`{kind}` {count}" for kind, count in profile["types"].items())
    numeric_range = (f"{profile['minimum']}–{profile['maximum']}"
                     if profile["minimum"] is not None else "—")
    return (f"{types} | {profile['missing']} | {profile['null']} | "
            f"{profile['distinct']} | {numeric_range}")


def field_table(title: str, rows: list[dict[str, Any]]) -> list[str]:
    lines = [f"### {title}", "", "| Field | Observed JSON type (count) | Missing keys | Explicit nulls | Distinct non-null values | Numeric min–max |",
             "|---|---|---:|---:|---:|---:|"]
    for name, stats in profile_fields(rows).items():
        lines.append(f"| `{name}` | {cells(stats)} |")
    lines.append("")
    return lines


def render(source_path: Path, manifest_path: Path) -> str:
    source_bytes = source_path.read_bytes()
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    policy = manifest["source"]
    digest = sha256(source_bytes).hexdigest()
    if len(source_bytes) != policy["byteSize"] or digest != policy["sha256"]:
        raise ValueError("Source bytes do not match the candidate manifest; dictionary not generated")
    events = json.loads(source_bytes.decode("utf-8-sig"))
    if not isinstance(events, list) or len(events) != policy["expectedEventCount"]:
        raise ValueError("Source event array does not match the manifest event count")
    if any(not isinstance(row, dict) for row in events):
        raise ValueError("Source array contains a non-object event")
    positions = [position for event in events
                 for position in event.get("playerPositions", [])]
    if any(not isinstance(row, dict) for row in positions):
        raise ValueError("Source playerPositions contains a non-object row")
    metadata = [event.get("metadata") for event in events]
    if any(not isinstance(row, dict) for row in metadata):
        raise ValueError("Source event metadata contains a non-object value")

    lines = [
        "# MANU-LIV observed data dictionary", "",
        "**Classification:** measured structural observations from one pinned candidate JSON file. This is not an authoritative semantic dictionary, football certification, canonicalization approval, or evidence that values are real-world observations.", "",
        f"**Source:** `{policy['path']}`; {len(source_bytes):,} bytes; SHA-256 `{digest}`; manifest `data/simulations/manu-liv-candidate-manifest.json` v{manifest.get('manifestVersion')}; status `{manifest.get('status')}`.", "",
        f"**Provenance:** manifest records synthetic provenance and redistribution permission as repository-owner-confirmed on {policy.get('provenanceConfirmedOn')}. Independent rights/privacy review is `{policy.get('independentRightsPrivacyReview')}`. This declaration does not validate semantics or football realism.", "",
        f"**Reproduce:** from the repository root, run `python3 scripts/profile_manu_liv_observed_dictionary.py`. The command validates manifest size/hash before replacing this Markdown file. Pass `--output /path/outside/repository/report.md` to write elsewhere.", "",
        "## Interpretation limits", "",
        "- Field names, JSON types, nulls, ranges, distinct counts, and source-order aggregates below are measured from the pinned bytes. For the tables, “missing keys” means a key is absent in an object; “explicit nulls” means it is present with JSON `null`. Distinct counts exclude nulls and count values as JSON values.",
        "- Numeric ranges are raw values only. Coordinate units, axis/origin/orientation, attack direction, and pitch dimensions are **UNSPECIFIED / UNVERIFIED**. No conversion or distance inference is supported.",
        "- `clockSeconds` is an integer field; the source unit is **UNSPECIFIED / UNVERIFIED**. The `Seconds` suffix is part of the field name and is not an authoritative data dictionary. Whether values denote elapsed match time, period-local time, stoppage time, or the meaning of period markers is also unverified.",
        "- Manifest-pinned clock ordering, period labels, and marker row values are candidate-file structural regression checks only. They do not establish time units, period meanings, football semantics, or approval for other feeds.",
        "- Player metric definitions, units, calculation windows, sampling cadence, identity stability, validity, and real-world status are **UNSPECIFIED / UNVERIFIED**. Do not present these values as measured player performance or biometrics.",
        "- Source event labels are preserved as labels. Event semantics and source-to-domain mappings are **UNSPECIFIED / UNVERIFIED**; the manifest explicitly has no approved mappings. Exact label equality is not semantic equivalence.",
        "- Score snapshot component order/team-side mapping and canonical score authority are **UNSPECIFIED / UNVERIFIED**. The source’s score strings are not certified match facts.",
        "- Football realism is **UNVERIFIED**. Counts are observations, not a plausibility or rules verdict. `playerPositions` is event-attached and does not establish continuous tracking.",
        "- This dictionary preserves source identifiers, sequence, raw clock-field values, coordinates, and ordering as-is. It performs no canonical imports, transformations, or value conversions.", "",
        "## Record structure", "",
        f"The JSON root is an array with {len(events):,} event objects and {len(positions):,} event-attached player-position objects. Each event has a `metadata` object. The tables cover every object of each type in the pinned file.", "",
    ]
    lines += field_table("Event object fields", events)
    lines += field_table("`metadata` object fields", metadata)
    lines += field_table("`playerPositions[]` object fields", positions)

    event_counts = Counter(event["eventType"] for event in events)
    outcomes = Counter(event["outcome"] for event in events)
    sequence = [event["sequence"] for event in events]
    clocks = [event["clockSeconds"] for event in events]
    periods = Counter(event["period"] for event in events)
    adjacent_equal_clocks = sum(a == b for a, b in zip(clocks, clocks[1:]))
    clock_regressions = sum(b < a for a, b in zip(clocks, clocks[1:]))
    lines += [
        "## Observed categorical values and order checks", "",
        "| `eventType` label | Rows |", "|---|---:|"]
    lines += [f"| `{label}` | {count} |" for label, count in sorted(event_counts.items())]
    lines += ["", "| `outcome` label | Rows |", "|---|---:|"]
    lines += [f"| `{label}` | {count} |" for label, count in sorted(outcomes.items())]
    lines += [
        "", "| Check | Observed result |", "|---|---|",
        f"| Source sequence endpoints | {min(sequence)}–{max(sequence)}; {len(set(sequence)):,} distinct |",
        f"| Sequence contiguous and ascending | {sequence == list(range(sequence[0], sequence[0] + len(sequence)))} |",
        f"| `clockSeconds` raw range | {min(clocks)}–{max(clocks)} |",
        f"| Adjacent equal clock pairs | {adjacent_equal_clocks} |",
        f"| Adjacent clock regressions | {clock_regressions} |",
        f"| `period` counts | {', '.join(f'{period}: {count}' for period, count in sorted(periods.items()))} |",
        "",
        "## Provenance and use boundary", "",
        "The source ID, sequence, clock and value spellings are data provenance. The manifest status is `candidate_not_canonical`; it states that canonical events may not be created from this file. Preserve original event IDs, source order and clock semantics verbatim during any future reviewed work. Canonical admission remains blocked until coordinate/time semantics, player metrics and identities, event mappings, score interpretation, football realism, and independent rights/privacy review have the required evidence.", "",
        "**Generator:** `scripts/profile_manu_liv_observed_dictionary.py`. It uses only the Python standard library and refuses to emit output when the source bytes do not match the manifest.", "",
    ]
    return "\n".join(lines)


def _aliases_protected_input(output: Path, protected: Path) -> bool:
    """Return true for path, symlink, or hard-link aliases; fail closed on I/O errors."""
    try:
        if output.resolve() == protected.resolve():
            return True
        return output.exists() and protected.exists() and output.samefile(protected)
    except OSError as exc:
        raise ValueError("Cannot safely verify output path identity") from exc


def write_output(text: str, output: Path, source: Path, manifest: Path) -> None:
    """Write only when output cannot replace data inputs or this generator."""
    protected_files = (source, manifest, Path(__file__))
    if any(_aliases_protected_input(output, protected) for protected in protected_files):
        raise ValueError("Output must not overwrite the source, manifest, or generator")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(text, encoding="utf-8")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args(argv)
    try:
        text = render(args.source, args.manifest)
        write_output(text, args.output, args.source, args.manifest)
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as exc:
        parser.error(str(exc))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
