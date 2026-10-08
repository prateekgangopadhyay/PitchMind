#!/usr/bin/env python3
"""Fail closed when NuGet reports an advisory or an unusable audit result."""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
from typing import Any


class AuditError(ValueError):
    pass


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


def validate_report(report: Any) -> int:
    if (not isinstance(report, dict) or isinstance(report.get("version"), bool)
            or not isinstance(report.get("version"), int) or report["version"] != 1):
        raise AuditError("NuGet returned an unsupported vulnerability report format")
    projects = report.get("projects")
    sources = report.get("sources")
    if (not isinstance(projects, list) or not projects
            or any(not isinstance(project, dict) or not isinstance(project.get("path"), str)
                   for project in projects)):
        raise AuditError("NuGet vulnerability report did not identify the solution projects")
    if (not isinstance(sources, list) or not sources
            or any(not isinstance(source, str) or not source.strip() for source in sources)):
        raise AuditError("NuGet vulnerability report did not identify an advisory source")
    findings = _find_vulnerable_packages(report)
    if findings:
        raise AuditError("Vulnerable NuGet dependencies found: " + "; ".join(findings))
    return len(projects)


def audit(solution: Path) -> int:
    command = [
        "dotnet", "list", str(solution), "package", "--vulnerable", "--include-transitive", "--format", "json"
    ]
    result = subprocess.run(command, capture_output=True, text=True, check=False)
    if result.returncode != 0:
        details = result.stderr.strip() or result.stdout.strip() or f"exit code {result.returncode}"
        raise AuditError(f"NuGet vulnerability audit command failed: {details}")
    try:
        report = json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        raise AuditError("NuGet vulnerability audit did not return valid JSON") from exc
    return validate_report(report)


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
