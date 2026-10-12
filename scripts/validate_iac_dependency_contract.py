#!/usr/bin/env python3
"""Validate the EP02-S01 source dependency matrix without contacting Azure."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys
from typing import Any

ROOT = Path(__file__).resolve().parent.parent
CONTRACT = ROOT / "infra/resource-dependency-contract.json"
ALLOWED_STATES = {"declared", "candidate_only", "demo_only", "not_declared", "deferred"}
ALLOWED_NETWORK = {"disabled", "enabled", "unverified", "not_applicable"}
ALLOWED_CONNECTIVITY = {"blocked", "demo_only", "unverified", "verified"}
REQUIRED_IDS = {
    "offline-demo-api", "storage-account", "key-vault", "canonical-postgres-candidate",
    "service-bus-private-path", "canonical-api-workload", "canonical-feeder-workload",
    "canonical-event-worker", "canonical-agent-worker", "canonical-runtime-identities",
    "migration-identity", "diagnostics-alerts-budget",
}


class ContractError(ValueError):
    pass


def _repository_file(root: Path, relative: Any, label: str) -> Path:
    if not isinstance(relative, str) or not relative or Path(relative).is_absolute() or ".." in Path(relative).parts:
        raise ContractError(f"Unsafe {label} path")
    root_path = root.resolve()
    path = root_path / relative
    current = root_path
    for part in Path(relative).parts:
        current = current / part
        if current.is_symlink():
            raise ContractError(f"{label} is missing or symlinked")
    try:
        resolved = path.resolve(strict=True)
        resolved.relative_to(root_path)
    except (OSError, ValueError) as exc:
        raise ContractError(f"{label} is missing or outside the repository") from exc
    if not resolved.is_file() or path.is_symlink():
        raise ContractError(f"{label} is missing or symlinked")
    return resolved


def _load(path: Path) -> dict[str, Any]:
    def pairs(items: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in items:
            if key in result:
                raise ContractError(f"Duplicate JSON key: {key}")
            result[key] = value
        return result

    try:
        result = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=pairs)
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ContractError("Dependency contract must be readable UTF-8 JSON") from exc
    if not isinstance(result, dict):
        raise ContractError("Dependency contract root must be an object")
    return result


def validate(contract: dict[str, Any], root: Path = ROOT) -> int:
    if contract.get("contractVersion") != "1.0.0" or contract.get("storyId") != "EP02-S01":
        raise ContractError("Unsupported contract version or story ID")
    if not isinstance(contract.get("evidenceBoundary"), str) or not contract["evidenceBoundary"].strip():
        raise ContractError("Evidence boundary must be explicit")
    resources = contract.get("resources")
    if not isinstance(resources, list) or not resources:
        raise ContractError("Resources must be a non-empty array")

    ids: set[str] = set()
    for item in resources:
        if not isinstance(item, dict):
            raise ContractError("Every resource entry must be an object")
        resource_id = item.get("id")
        if not isinstance(resource_id, str) or not resource_id or resource_id in ids:
            raise ContractError("Resource IDs must be non-empty and unique")
        ids.add(resource_id)
        if item.get("deploymentState") not in ALLOWED_STATES:
            raise ContractError(f"Unknown deployment state for {resource_id}")
        if item.get("publicNetworkAccess") not in ALLOWED_NETWORK:
            raise ContractError(f"Unknown public-network state for {resource_id}")
        disposition = item.get("disposition")
        if not isinstance(disposition, str) or not disposition.strip():
            raise ContractError(f"Disposition is required for {resource_id}")
        consumers = item.get("consumers")
        if not isinstance(consumers, list) or any(not isinstance(value, str) or not value for value in consumers):
            raise ContractError(f"Consumers must be a string list for {resource_id}")
        connectivity = item.get("connectivity")
        if not isinstance(connectivity, dict) or connectivity.get("status") not in ALLOWED_CONNECTIVITY:
            raise ContractError(f"Connectivity status is required for {resource_id}")
        state = item["deploymentState"]
        source = item.get("source")
        if state in {"declared", "candidate_only", "demo_only"}:
            if not isinstance(source, dict):
                raise ContractError(f"Source declaration is required for {resource_id}")
            source_path = source.get("path")
            if not isinstance(source_path, str) or not source_path.startswith("infra/"):
                raise ContractError(f"Unsafe or missing source path for {resource_id}")
            file_path = _repository_file(root, source_path, f"Source for {resource_id}")
            if source.get("kind") == "file":
                if source.get("symbol") is not None:
                    raise ContractError(f"File-only source cannot claim a Bicep symbol for {resource_id}")
            else:
                if source.get("kind") not in {"resource", "module"} or not isinstance(source.get("symbol"), str):
                    raise ContractError(f"Source kind/symbol is invalid for {resource_id}")
                text = file_path.read_text(encoding="utf-8")
                match = re.search(rf"^\s*{source['kind']}\s+{re.escape(source['symbol'])}\b[^\n]*", text, re.MULTILINE)
                if match is None:
                    raise ContractError(f"Bicep {source['kind']} {source['symbol']} is absent for {resource_id}")
                gate = source.get("gate")
                if state == "candidate_only" and (not isinstance(gate, str) or f"if ({gate})" not in match.group(0)):
                    raise ContractError(f"Candidate resource lacks its declared opt-in gate: {resource_id}")
        elif state == "deferred" and source is not None:
            if not isinstance(source, dict) or source.get("kind") != "file" or source.get("symbol") is not None:
                raise ContractError(f"Deferred source must be a file reference without a Bicep symbol: {resource_id}")
            source_path = source.get("path")
            if not isinstance(source_path, str) or not source_path.startswith("infra/"):
                raise ContractError(f"Unsafe deferred source path for {resource_id}")
            _repository_file(root, source_path, f"Deferred source for {resource_id}")
        elif source is not None:
            raise ContractError(f"Unimplemented/deferred resource cannot claim a source declaration: {resource_id}")

        if item["publicNetworkAccess"] == "disabled":
            if connectivity["status"] != "verified":
                if connectivity["status"] != "blocked":
                    raise ContractError(f"Disabled-public-access resource must be blocked until private connectivity is verified: {resource_id}")
            else:
                evidence = connectivity.get("evidence")
                if not isinstance(evidence, dict) or not evidence.get("testId") or not evidence.get("path"):
                    raise ContractError(f"Verified private connectivity requires evidence path and test ID: {resource_id}")
                _repository_file(root, evidence["path"], f"Connectivity evidence for {resource_id}")
        if connectivity["status"] == "demo_only":
            evidence = connectivity.get("evidence")
            if not isinstance(evidence, dict) or not evidence.get("testId") or not evidence.get("path"):
                raise ContractError(f"Demo-only verification requires evidence path and test ID: {resource_id}")
            _repository_file(root, evidence["path"], f"Demo evidence for {resource_id}")
        if connectivity["status"] == "verified" and state in {"candidate_only", "not_declared", "deferred"}:
            raise ContractError(f"Undeployed target cannot claim verified runtime connectivity: {resource_id}")
        if item.get("disposition") == "available" and connectivity["status"] != "verified":
            raise ContractError(f"Available disposition requires verified connectivity: {resource_id}")

    missing = REQUIRED_IDS - ids
    if missing:
        raise ContractError("Required dependency rows are missing: " + ", ".join(sorted(missing)))
    return len(resources)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--contract", type=Path, default=CONTRACT)
    args = parser.parse_args(argv)
    try:
        count = validate(_load(args.contract))
    except ContractError as exc:
        print(f"IaC dependency contract invalid: {exc}", file=sys.stderr)
        return 2
    print(f"PASS: EP02-S01 dependency contract has {count} resource rows; no Azure calls made.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
