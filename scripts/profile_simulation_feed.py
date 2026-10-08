#!/usr/bin/env python3
"""Profile, do not ingest, an owner-supplied football JSON/NDJSON file.

This deliberately makes NO assumptions about vendor key names or football rules.
The report contains metadata only (field names, types/counts, no raw event values).
Requires Python 3.11+, standard library only. Exit 0 valid JSON, 2 invalid/missing.
"""
from __future__ import annotations

import argparse
from collections import Counter
from hashlib import sha256
import json
from pathlib import Path
import sys
from typing import Any

MAX_BYTES = 50_000_000
MAX_DEPTH = 10
MAX_ARRAY_SAMPLE = 200
MAX_FIELDS = 5000


def _type(value: Any) -> str:
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
    return "object" if isinstance(value, dict) else "unknown"


def _walk(value: Any, path: str, depth: int, types: dict[str, Counter], arrays: dict[str, dict]) -> None:
    if depth > MAX_DEPTH or len(types) >= MAX_FIELDS and path not in types:
        return
    types.setdefault(path, Counter())[_type(value)] += 1
    if isinstance(value, list):
        bucket = arrays.setdefault(path, {"instances": 0, "minLength": None, "maxLength": 0, "sampleLimit": MAX_ARRAY_SAMPLE})
        bucket["instances"] += 1
        n = len(value)
        bucket["minLength"] = n if bucket["minLength"] is None else min(bucket["minLength"], n)
        bucket["maxLength"] = max(bucket["maxLength"], n)
        for item in value[:MAX_ARRAY_SAMPLE]:
            _walk(item, path + "[]", depth + 1, types, arrays)
    elif isinstance(value, dict):
        # Sorted deterministic paths; do not include any field values in report.
        for key in sorted(value):
            if not isinstance(key, str):
                continue
            # Escape user-controlled key names as JSON path segments, not dot strings.
            child = path + "[" + json.dumps(key, ensure_ascii=True) + "]"
            _walk(value[key], child, depth + 1, types, arrays)


def profile(path: Path, *, ndjson: bool = False, max_bytes: int = MAX_BYTES) -> dict[str, Any]:
    if not path.is_file():
        raise ValueError("Simulation source must be an existing regular file")
    if path.is_symlink():
        raise ValueError("Symbolic-link sources are not accepted")
    size = path.stat().st_size
    if size == 0 or size > max_bytes:
        raise ValueError(f"File size {size} is empty or exceeds configured limit {max_bytes}")
    data = path.read_bytes()
    try:
        content = data.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        raise ValueError("Source must be UTF-8") from exc
    try:
        if ndjson:
            records = [json.loads(line, parse_constant=_reject_nonfinite) for line in content.splitlines() if line.strip()]
            if not records:
                raise ValueError("NDJSON contains no records")
            source = records
        else:
            source = json.loads(content, parse_constant=_reject_nonfinite)
    except (json.JSONDecodeError, RecursionError) as exc:
        raise ValueError("Malformed JSON; no canonical import was attempted") from exc
    types: dict[str, Counter] = {}
    arrays: dict[str, dict] = {}
    _walk(source, "$", 0, types, arrays)
    return {
        "status": "profiled_not_validated",
        "sourceFileName": path.name,
        "sha256": sha256(data).hexdigest(),
        "byteSize": len(data),
        "encoding": "utf-8",
        "format": "ndjson" if ndjson else "json",
        "rootType": _type(source),
        "sampledArrayElementsPerInstance": MAX_ARRAY_SAMPLE,
        "maxProfileDepth": MAX_DEPTH,
        "fieldTypes": {key: dict(sorted(count.items())) for key, count in sorted(types.items())},
        "arrayLengths": {key: arrays[key] for key in sorted(arrays)},
        "warnings": ["Structural profiling is not schema mapping, football validation, proof of synthetic provenance, rights clearance or ingestion."],
    }


def _reject_nonfinite(value: str) -> None:
    raise ValueError("JSON must not contain NaN or Infinity")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Read-only schema profile for owner-supplied simulation JSON; no values or secrets emitted")
    parser.add_argument("path", type=Path)
    parser.add_argument("--ndjson", action="store_true", help="parse one JSON value per non-empty line")
    parser.add_argument("--max-bytes", type=int, default=MAX_BYTES)
    parser.add_argument("--output", type=Path, help="optional report JSON path (never overwrites source)")
    args = parser.parse_args(argv)
    try:
        if args.max_bytes < 1:
            raise ValueError("--max-bytes must be positive")
        report = profile(args.path, ndjson=args.ndjson, max_bytes=args.max_bytes)
        result = json.dumps(report, ensure_ascii=True, sort_keys=True, indent=2) + "\n"
        if args.output is not None:
            if args.output.resolve() == args.path.resolve():
                raise ValueError("Report output must not overwrite input source")
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(result, encoding="utf-8")
        else:
            sys.stdout.write(result)
    except (ValueError, OSError) as exc:
        print(f"Simulation feed preflight failed: {exc}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
