"""Fail-closed behavior tests for the NuGet advisory gate."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import unittest

SCRIPT = Path(__file__).with_name("check_nuget_vulnerabilities.py")
spec = importlib.util.spec_from_file_location("nuget_vulnerability_audit", SCRIPT)
audit = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(audit)


class NugetVulnerabilityAuditTests(unittest.TestCase):
    def test_accepts_report_with_projects_and_advisory_sources(self):
        report = {"version": 1, "sources": ["https://api.nuget.org/v3/index.json"],
                  "projects": [{"path": "app.csproj"}, {"path": "tests.csproj"}]}
        self.assertEqual(audit.validate_report(report), 2)

    def test_rejects_top_level_vulnerability_records(self):
        report = {"version": 1, "sources": ["nuget"], "projects": [{"path": "app.csproj"}],
                  "frameworks": [{"topLevelPackages": [{"id": "Example", "vulnerabilities": [
                      {"severity": "High", "advisoryurl": "https://example.invalid/advisory"}
                  ]}]}]}
        with self.assertRaisesRegex(audit.AuditError, "Example.*advisory"):
            audit.validate_report(report)

    def test_rejects_transitive_vulnerability_records(self):
        report = {"version": 1, "sources": ["nuget"], "projects": [{"path": "app.csproj",
                  "frameworks": [{"transitivePackages": [{"id": "Transit", "vulnerabilities": [
                      {"severity": "Moderate"}
                  ]}]}]}]}
        with self.assertRaisesRegex(audit.AuditError, "Transit.*advisory"):
            audit.validate_report(report)

    def test_rejects_missing_projects_sources_and_unknown_format(self):
        for report in (
            {"version": 1, "sources": ["nuget"], "projects": []},
            {"version": 1, "sources": [], "projects": [{"path": "app.csproj"}]},
            {"version": 2, "sources": ["nuget"], "projects": [{"path": "app.csproj"}]},
            {"version": True, "sources": ["nuget"], "projects": [{"path": "app.csproj"}]},
            {"version": 1, "sources": ["nuget"], "projects": [{"path": "app.csproj"}],
             "vulnerabilities": "unexpected"},
        ):
            with self.subTest(report=report), self.assertRaises(audit.AuditError):
                audit.validate_report(report)


if __name__ == "__main__":
    unittest.main()
