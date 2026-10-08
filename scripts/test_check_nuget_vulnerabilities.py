"""Fail-closed behavior tests for the NuGet advisory gate."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).with_name("check_nuget_vulnerabilities.py")
spec = importlib.util.spec_from_file_location("nuget_vulnerability_audit", SCRIPT)
audit = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(audit)


class NugetVulnerabilityAuditTests(unittest.TestCase):
    def test_accepts_report_with_projects_and_advisory_sources(self):
        report = {"version": 1, "sources": ["https://api.nuget.org/v3/index.json"],
                  "projects": [{"path": "/repo/app.csproj"}, {"path": "/repo/tests.csproj"}]}
        self.assertEqual(audit.validate_report(report, {"/repo/app.csproj", "/repo/tests.csproj"}), 2)

    def test_rejects_top_level_vulnerability_records(self):
        report = {"version": 1, "sources": ["https://api.nuget.org/v3/index.json"],
                  "projects": [{"path": "/repo/app.csproj", "frameworks": [{"framework": "net10.0",
                  "topLevelPackages": [{"id": "Example", "vulnerabilities": [
                      {"severity": "High", "advisoryurl": "https://example.invalid/advisory"}
                  ]}]}]}]}
        with self.assertRaisesRegex(audit.AuditError, "Example.*advisory"):
            audit.validate_report(report, {"/repo/app.csproj"})

    def test_rejects_transitive_vulnerability_records(self):
        report = {"version": 1, "sources": ["https://api.nuget.org/v3/index.json"],
                  "projects": [{"path": "/repo/app.csproj", "frameworks": [{"framework": "net10.0",
                  "transitivePackages": [{"id": "Transit", "vulnerabilities": [
                      {"severity": "Moderate"}
                  ]}]}]}]}
        with self.assertRaisesRegex(audit.AuditError, "Transit.*advisory"):
            audit.validate_report(report, {"/repo/app.csproj"})

    def test_rejects_missing_projects_sources_coverage_and_unknown_format(self):
        for report in (
            {"version": 1, "sources": ["https://api.nuget.org/v3/index.json"], "projects": []},
            {"version": 1, "sources": [], "projects": [{"path": "/repo/app.csproj"}]},
            {"version": 1, "sources": ["nuget"], "projects": [{"path": "/repo/app.csproj"}]},
            {"version": 1, "sources": ["https://untrusted.example/v3/index.json"], "projects": [{"path": "/repo/app.csproj"}]},
            {"version": 2, "sources": ["https://api.nuget.org/v3/index.json"], "projects": [{"path": "/repo/app.csproj"}]},
            {"version": True, "sources": ["https://api.nuget.org/v3/index.json"], "projects": [{"path": "/repo/app.csproj"}]},
            {"version": 1, "sources": ["https://api.nuget.org/v3/index.json"], "projects": [{"path": "/repo/app.csproj"}],
             "vulnerabilities": "unexpected"},
        ):
            with self.subTest(report=report), self.assertRaises(audit.AuditError):
                audit.validate_report(report, {"/repo/app.csproj"})

    def test_rejects_malformed_nested_framework_and_package_collections(self):
        clean = {"version": 1, "sources": ["https://api.nuget.org/v3/index.json"],
                 "projects": [{"path": "/repo/app.csproj"}]}
        malformed = [
            {**clean, "projects": [{"path": "/repo/app.csproj", "frameworks": "unexpected"}]},
            {**clean, "projects": [{"path": "/repo/app.csproj", "frameworks": [{"framework": "net10.0"}]}]},
            {**clean, "projects": [{"path": "/repo/app.csproj", "frameworks": [{"framework": "net10.0", "topLevelPackages": "unexpected"}]}]},
            {**clean, "projects": [{"path": "/repo/app.csproj", "frameworks": [{"framework": "net10.0", "topLevelPackages": [{"id": "Example"}]}]}]},
        ]
        for report in malformed:
            with self.subTest(report=report), self.assertRaises(audit.AuditError):
                audit.validate_report(report, {"/repo/app.csproj"})

    @patch.object(audit, "solution_projects", return_value={"/repo/app.csproj"})
    @patch.object(audit.subprocess, "run", side_effect=audit.subprocess.TimeoutExpired("dotnet", 120))
    def test_fails_closed_when_live_scan_times_out(self, _run, _projects):
        with self.assertRaisesRegex(audit.AuditError, "timed out after 120 seconds"):
            audit.audit(Path("/repo/PitchMind.slnx"))

    @patch.object(audit, "solution_projects", return_value={"/repo/app.csproj"})
    @patch.object(audit.subprocess, "run", return_value=audit.subprocess.CompletedProcess(
        args=["dotnet"], returncode=0, stdout='{"version":1,"sources":["https://api.nuget.org/v3/index.json"],"projects":[{"path":"/repo/app.csproj"}]}', stderr="warning: audit source unavailable"
    ))
    def test_fails_closed_when_cli_reports_success_with_warning(self, _run, _projects):
        with self.assertRaisesRegex(audit.AuditError, "audit warning"):
            audit.audit(Path("/repo/PitchMind.slnx"))


if __name__ == "__main__":
    unittest.main()
