#!/usr/bin/env python3
"""Validate DEV/STAGE/PROD IaC parameter parity without contacting Azure."""

from __future__ import annotations

import json
import ipaddress
import re
import sys
from pathlib import Path


ENVIRONMENTS = ("dev", "stage", "prod")
REQUIRED_PROFILE_KEYS = ("environment", "enableAdvancedAnalytics", "enableDevFoundation")
OPTIONAL_PROFILE_KEYS = ("virtualNetworkCidr",)


def _validate_dev_network_cidr(value: object) -> list[str]:
    """Check the DEV address space against the /24 subnet indexes in networking.bicep."""
    if not isinstance(value, str):
        return ["dev virtualNetworkCidr must be an IPv4 CIDR string"]
    try:
        network = ipaddress.ip_network(value, strict=True)
    except ValueError:
        return ["dev virtualNetworkCidr must be a canonical IPv4 CIDR network"]
    if not isinstance(network, ipaddress.IPv4Network):
        return ["dev virtualNetworkCidr must be an IPv4 CIDR network"]
    if network.prefixlen > 22:
        return ["dev virtualNetworkCidr must be /22 or larger to contain subnet indexes 1 and 2 as /24s"]
    # Prefix <= /22 guarantees indexes 1 and 2 exist for cidrSubnet(..., 24, index).
    return []


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


def _without_bicep_comments_and_strings(source: str) -> str:
    """Mask comments and string literals while preserving line boundaries for source checks."""
    characters = list(source)
    index = 0

    def mask(start: int, end: int) -> None:
        for offset in range(start, end):
            if characters[offset] not in "\r\n":
                characters[offset] = " "

    while index < len(source):
        if source.startswith("//", index):
            end = source.find("\n", index)
            if end < 0:
                end = len(source)
            mask(index, end)
            index = end
        elif source.startswith("/*", index):
            closing = source.find("*/", index + 2)
            end = len(source) if closing < 0 else closing + 2
            mask(index, end)
            index = end
        elif source.startswith("'''", index):
            closing = source.find("'''", index + 3)
            end = len(source) if closing < 0 else closing + 3
            mask(index, end)
            index = end
        elif source[index] == "'":
            start = index
            index += 1
            while index < len(source):
                if source[index] == "'":
                    if index + 1 < len(source) and source[index + 1] == "'":
                        index += 2
                        continue
                    index += 1
                    break
                index += 1
            mask(start, index)
        else:
            index += 1

    return "".join(characters)


def _has_active_environment_allowlist(source: str, active_code: str) -> bool:
    expected = "@allowed(['dev', 'stage', 'prod'])"
    source_lines = source.splitlines()
    code_lines = active_code.splitlines()
    for index, (source_line, code_line) in enumerate(zip(source_lines, code_lines)):
        if source_line.strip() != expected or not code_line.strip().startswith("@allowed("):
            continue
        next_index = index + 1
        while next_index < len(code_lines) and not code_lines[next_index].strip():
            next_index += 1
        if next_index < len(code_lines) and code_lines[next_index].strip() == "param environment string":
            return True
    return False


def validate_profiles(repo_root: Path) -> list[str]:
    errors: list[str] = []
    infra = repo_root / "infra"
    main_source = (infra / "main.bicep").read_text(encoding="utf-8")
    main = _without_bicep_comments_and_strings(main_source)

    if not _has_active_environment_allowlist(main_source, main):
        errors.append("infra/main.bicep must restrict environment to dev, stage, or prod")
    declarations = re.findall(
        r"(?m)^[ \t]*module[ \t]+(\w+)[ \t]+[^=\r\n]*=[ \t]*(.*)$",
        main,
    )
    modules: dict[str, list[str]] = {}
    for name, expression in declarations:
        modules.setdefault(name, []).append(expression.strip())
    required_guard = re.compile(r"if \(enableDevFoundation && environment ==\s*\) \{")
    for module_name in ("network", "vault"):
        module_guards = modules.get(module_name, [])
        if len(module_guards) != 1 or not required_guard.fullmatch(module_guards[0]):
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
        if environment == "dev":
            for profile_name, values in (("JSON", json_values), (".bicepparam", source_values)):
                if "virtualNetworkCidr" in values:
                    errors.extend(
                        f"dev {profile_name} profile: {error}"
                        for error in _validate_dev_network_cidr(values["virtualNetworkCidr"])
                    )
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
