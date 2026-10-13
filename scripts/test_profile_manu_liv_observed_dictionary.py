"""Regression tests for the observed dictionary generator's protected inputs."""
from __future__ import annotations

import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("profile_manu_liv_observed_dictionary.py")
spec = importlib.util.spec_from_file_location("observed_dictionary", SCRIPT)
generator = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(generator)


class ObservedDictionaryOutputProtectionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.source = self.root / "candidate.json"
        self.manifest = self.root / "manifest.json"
        self.generator_path = SCRIPT.resolve()
        self.generator_bytes = self.generator_path.read_bytes()
        self.source_bytes = json.dumps([{
            "eventId": "event-1", "sequence": 1, "clockSeconds": 0,
            "period": 1, "eventType": "Pass", "outcome": "Complete",
            "metadata": {}, "playerPositions": [],
        }], separators=(",", ":")).encode()
        self.source.write_bytes(self.source_bytes)
        self.manifest_bytes = json.dumps({
            "manifestVersion": "test",
            "status": "candidate_not_canonical",
            "source": {
                "path": "candidate.json",
                "sha256": hashlib.sha256(self.source_bytes).hexdigest(),
                "byteSize": len(self.source_bytes),
                "expectedEventCount": 1,
                "provenanceConfirmedOn": "test",
                "independentRightsPrivacyReview": "pending",
            },
        }).encode()
        self.manifest.write_bytes(self.manifest_bytes)

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_render_derives_field_counts_and_keeps_semantics_unverified(self) -> None:
        report = generator.render(self.source, self.manifest)
        self.assertIn("`clockSeconds` is an integer field; the source unit is **UNSPECIFIED / UNVERIFIED**", report)
        self.assertIn("| `eventId` | `string` 1 | 0 | 0 | 1 | — |", report)
        self.assertIn("| Source sequence endpoints | 1–1; 1 distinct |", report)
        self.assertIn("| `playerPositions`", report)
        self.assertNotIn("canonical events may be created", report)

    def test_direct_symlink_and_hardlink_aliases_cannot_overwrite_protected_files(self) -> None:
        for protected, original_bytes, label in (
            (self.source, self.source_bytes, "source"),
            (self.manifest, self.manifest_bytes, "manifest"),
            (self.generator_path, self.generator_bytes, "generator"),
        ):
            for alias_kind in ("direct", "symlink", "hardlink"):
                with self.subTest(protected=label, alias=alias_kind):
                    output = self._alias_path(protected, alias_kind, label)
                    stderr = io.StringIO()
                    with contextlib.redirect_stderr(stderr):
                        with self.assertRaises(SystemExit) as failure:
                            generator.main([
                                "--source", str(self.source),
                                "--manifest", str(self.manifest),
                                "--output", str(output),
                            ])
                    self.assertEqual(failure.exception.code, 2)
                    self.assertIn("Output must not overwrite", stderr.getvalue())
                    self.assertEqual(protected.read_bytes(), original_bytes)
                    self.assertEqual(self.source.read_bytes(), self.source_bytes)
                    self.assertEqual(self.manifest.read_bytes(), self.manifest_bytes)
                    self.assertEqual(self.generator_path.read_bytes(), self.generator_bytes)

    def _alias_path(self, protected: Path, alias_kind: str, label: str) -> Path:
        if alias_kind == "direct":
            return protected
        alias = self.root / f"{label}-{alias_kind}-alias"
        try:
            if alias_kind == "symlink":
                alias.symlink_to(protected)
            else:
                alias.hardlink_to(protected)
        except (OSError, NotImplementedError) as exc:
            self.skipTest(f"{alias_kind} aliases are unavailable: {exc}")
        return alias


if __name__ == "__main__":
    unittest.main()
