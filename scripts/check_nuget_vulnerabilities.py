#!/usr/bin/env python3
"""Fail closed when NuGet reports an advisory or an unusable audit result."""
from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess
import sys
from typing import Any
from urllib.parse import urlsplit
import xml.etree.ElementTree as ET


class AuditError(ValueError):
    pass


EXPECTED_AUDIT_SOURCE = "https://api.nuget.org/v3/index.json"


def _find_vulnerable_packages(value: Any, path: str = "report") -> list[str]:
    findings: list[str] = []
    if isinstance(value, dict):
        vulnerabilities = value.get("vulnerabilities", [])
        if not isinstance(vulnerabilities, list):
            raise AuditError(f"NuGet vulnerability report has an invalid advisory list at {path}")
        if vulnerabilities:
            package = value.get("id", "unknown package")
            findings.append(f"{package} ({len(vulnerabilities)} advisory record(s)) at {path}")
        for key, nested in value.items():
            findings.extend(_find_vulnerable_packages(nested, f"{path}.{key}"))
    elif isinstance(value, list):
        for index, nested in enumerate(value):
            findings.extend(_find_vulnerable_packages(nested, f"{path}[{index}]"))
    return findings


def _validate_project_shapes(projects: list[dict[str, Any]]) -> None:
    for project in projects:
        # NuGet's clean `--vulnerable --format json` result omits frameworks
        # entirely; the observed version-1 schema returns each solution path.
        # If the framework section is present, validate every nested collection.
        if "frameworks" not in project:
            continue
        frameworks = project["frameworks"]
        if not isinstance(frameworks, list) or not frameworks:
            raise AuditError("NuGet vulnerability report has an invalid framework collection")
        for framework in frameworks:
            if (not isinstance(framework, dict) or not isinstance(framework.get("framework"), str)
                    or not framework["framework"].strip()):
                raise AuditError("NuGet vulnerability report has an invalid framework entry")
            package_collections = [key for key in ("topLevelPackages", "transitivePackages") if key in framework]
            if not package_collections:
                raise AuditError("NuGet vulnerability report framework has no package collections")
            for key in package_collections:
                packages = framework[key]
                if not isinstance(packages, list):
                    raise AuditError(f"NuGet vulnerability report has an invalid {key} collection")
                for package in packages:
                    if (not isinstance(package, dict) or not isinstance(package.get("id"), str)
                            or not package["id"].strip()):
                        raise AuditError(f"NuGet vulnerability report has a malformed {key} package")
                    vulnerabilities = package.get("vulnerabilities")
                    if not isinstance(vulnerabilities, list) or not vulnerabilities:
                        raise AuditError(f"NuGet --vulnerable report has a package without advisory records in {key}")


def _normalize_project_path(path: str, solution_directory: Path) -> str:
    candidate = Path(path)
    if not candidate.is_absolute():
        candidate = solution_directory / candidate
    return str(candidate.resolve())


def solution_projects(solution: Path) -> set[str]:
    try:
        root = ET.parse(solution).getroot()
    except (OSError, ET.ParseError) as exc:
        raise AuditError("Could not read the solution project list") from exc
    paths = [project.get("Path") for project in root.iter("Project")]
    if not paths or any(not isinstance(path, str) or not path.strip() for path in paths):
        raise AuditError("Solution does not contain a complete project path list")
    normalized = {_normalize_project_path(path, solution.parent) for path in paths if path is not None}
    if len(normalized) != len(paths) or any(not Path(path).is_file() for path in normalized):
        raise AuditError("Solution project paths are missing, duplicated, or do not exist")
    return normalized


def validate_report(report: Any, expected_projects: set[str]) -> int:
    if (not isinstance(report, dict) or isinstance(report.get("version"), bool)
            or not isinstance(report.get("version"), int) or report["version"] != 1):
        raise AuditError("NuGet returned an unsupported vulnerability report format")
    projects = report.get("projects")
    sources = report.get("sources")
    if (not isinstance(projects, list) or not projects
            or any(not isinstance(project, dict) or not isinstance(project.get("path"), str)
                   for project in projects)):
        raise AuditError("NuGet vulnerability report did not identify the solution projects")
    actual_projects = {_normalize_project_path(project["path"], Path.cwd()) for project in projects}
    if len(actual_projects) != len(projects) or actual_projects != expected_projects:
        raise AuditError("NuGet vulnerability report project coverage differs from the solution")
    if (not isinstance(sources, list) or not sources
            or any(not isinstance(source, str) or source.strip() != EXPECTED_AUDIT_SOURCE
                   for source in sources)):
        raise AuditError("NuGet vulnerability report did not identify an advisory source")
    _validate_project_shapes(projects)
    findings = _find_vulnerable_packages(report)
    if findings:
        raise AuditError("Vulnerable NuGet dependencies found: " + "; ".join(findings))
    return len(projects)


def audit(solution: Path) -> int:
    expected_projects = solution_projects(solution)
    command = [
        "dotnet", "list", str(solution), "package", "--vulnerable", "--include-transitive", "--format", "json"
    ]
    try:
        result = subprocess.run(command, capture_output=True, text=True, check=False, timeout=120)
    except subprocess.TimeoutExpired as exc:
        raise AuditError("NuGet vulnerability audit timed out after 120 seconds") from exc
    if result.returncode != 0:
        details = result.stderr.strip() or result.stdout.strip() or f"exit code {result.returncode}"
        raise AuditError(f"NuGet vulnerability audit command failed: {details}")
    if re.search(r"\bwarning\b|\bNU190\d+\b", result.stderr, re.IGNORECASE):
        raise AuditError("NuGet reported a vulnerability audit warning: " + result.stderr.strip())
    try:
        report = json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        raise AuditError("NuGet vulnerability audit did not return valid JSON") from exc
    return validate_report(report, expected_projects)


def main(argv: list[str] | None = None) -> int:
    args = argv if argv is not None else sys.argv[1:]
    solution = Path(args[0]) if args else Path("PitchMind.slnx")
    if len(args) > 1:
        print("Usage: check_nuget_vulnerabilities.py [solution.slnx]", file=sys.stderr)
        return 2
    try:
        project_count = audit(solution)
    except (AuditError, OSError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        return 1
    print(f"PASS: NuGet vulnerability audit found no advisories across {project_count} projects")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
