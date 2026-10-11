#!/usr/bin/env python3
"""Validate DEV/STAGE/PROD IaC parameter parity without contacting Azure."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path


ENVIRONMENTS = ("dev", "stage", "prod")
REQUIRED_PROFILE_KEYS = ("environment", "enableAdvancedAnalytics", "enableDevFoundation")
OPTIONAL_PROFILE_KEYS = ("virtualNetworkCidr",)


def _bicepparam_values(path: Path) -> dict[str, object]:
    values: dict[str, object] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        match = re.fullmatch(r"\s*param\s+([A-Za-z][A-Za-z0-9_]*)\s*=\s*(.*?)\s*", line)
        if not match:
            continue
        name, raw = match.groups()
        if raw in ("true", "false"):
            values[name] = raw == "true"
        elif raw.startswith("'") and raw.endswith("'"):
            values[name] = raw[1:-1]
        else:
            values[name] = raw
    return values


def _without_bicep_comments(source: str) -> str:
    source = re.sub(r"/\*[\s\S]*?\*/", "", source)
    return re.sub(r"^\s*//.*$", "", source, flags=re.MULTILINE)


def validate_profiles(repo_root: Path) -> list[str]:
    errors: list[str] = []
    infra = repo_root / "infra"
    main = _without_bicep_comments((infra / "main.bicep").read_text(encoding="utf-8"))

    if "@allowed(['dev', 'stage', 'prod'])" not in main:
        errors.append("infra/main.bicep must restrict environment to dev, stage, or prod")
    for module_name in ("network", "vault"):
        if not re.search(rf"module\s+{module_name}\s+'[^']+'\s*=\s*if\s*\(enableDevFoundation\s*&&\s*environment\s*==\s*'dev'\)", main):
            errors.append(f"infra/main.bicep must guard {module_name} with enableDevFoundation && environment == 'dev'")

    for environment in ENVIRONMENTS:
        json_path = infra / "environments" / f"{environment}.json"
        params_path = infra / "environments" / f"{environment}.bicepparam"
        try:
            json_values = {
                key: entry["value"]
                for key, entry in json.loads(json_path.read_text(encoding="utf-8"))["parameters"].items()
            }
            source_values = _bicepparam_values(params_path)
        except (OSError, json.JSONDecodeError, KeyError, TypeError) as error:
            errors.append(f"{environment} profile cannot be read: {error}")
            continue

        for key in REQUIRED_PROFILE_KEYS:
            if key not in source_values or key not in json_values:
                errors.append(f"{environment} profile is missing required parameter {key}")
            elif json_values[key] != source_values[key]:
                errors.append(f"{environment} profile disagrees on {key} between .json and .bicepparam")
        for key in OPTIONAL_PROFILE_KEYS:
            in_json = key in json_values
            in_source = key in source_values
            if in_json != in_source:
                errors.append(f"{environment} profile disagrees on presence of optional parameter {key}")
            if environment != "dev" and (in_json or in_source):
                errors.append(f"{environment} profile must not override DEV-only parameter {key}")
            if in_json and in_source and json_values[key] != source_values[key]:
                errors.append(f"{environment} profile disagrees on {key} between .json and .bicepparam")
        if environment == "dev" and ("virtualNetworkCidr" not in json_values or "virtualNetworkCidr" not in source_values):
            errors.append("dev profile must explicitly provide virtualNetworkCidr in both parameter files")
        if json_values.get("environment") != environment or source_values.get("environment") != environment:
            errors.append(f"{environment} profile must explicitly target its own environment")
        expected_foundation = environment == "dev"
        if json_values.get("enableDevFoundation") is not expected_foundation or source_values.get("enableDevFoundation") is not expected_foundation:
            errors.append(f"{environment} profile must set enableDevFoundation={str(expected_foundation).lower()}")
        if json_values.get("enableAdvancedAnalytics") is not False or source_values.get("enableAdvancedAnalytics") is not False:
            errors.append(f"{environment} profile must keep optional advanced analytics disabled")

    return errors


def main() -> int:
    repo_root = Path(__file__).resolve().parent.parent
    errors = validate_profiles(repo_root)
    if errors:
        for error in errors:
            print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print("PASS: DEV/STAGE/PROD Bicep parameter profiles are synchronized and isolated")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
