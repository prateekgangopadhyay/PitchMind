"""Tests for the pinned candidate's structural, non-authoritative field dictionary."""
from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("generate_manu_liv_field_dictionary.py")
spec = importlib.util.spec_from_file_location("manu_liv_field_dictionary", SCRIPT)
dictionary = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(dictionary)


class FieldDictionaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / "candidate.json"
        self.manifest = self.root / "manifest.json"
        self.events = [
            {"eventId": "source-id-1", "sequence": 1, "clockSeconds": 0, "period": 1,
             "eventType": "Pass", "pitchX": 50.0, "pitchY": 50.0,
             "metadata": {"metric": "12.5", "optional": None},
             "playerPositions": [{"x": 40.0, "y": 60, "heartRate": 150}]},
            {"eventId": "source-id-2", "sequence": 2, "clockSeconds": 12, "period": 1,
             "eventType": "Goal", "pitchX": 52.0, "pitchY": 50.0,
             "metadata": {"metric": None},
             "playerPositions": [{"x": 41.0, "y": 61, "heartRate": 151}]},
        ]
        self.payload = json.dumps(self.events, separators=(",", ":")).encode()
        self.source.write_bytes(self.payload)
        self.write_manifest()

    def tearDown(self):
        self.temp.cleanup()

    def write_manifest(self, payload: bytes | None = None):
        body = payload if payload is not None else self.payload
        manifest = {"source": {"sha256": hashlib.sha256(body).hexdigest(), "byteSize": len(body)}}
        self.manifest.write_text(json.dumps(manifest), encoding="utf-8")

    def test_emits_structure_without_inventing_field_meaning_or_exposing_values(self):
        output = dictionary.render_dictionary(self.source, self.manifest)
        self.assertIn("repository-derived structural observations only", output)
        self.assertIn("`clockSeconds`", output)
        self.assertIn("source order nondecreasing", output)
        self.assertIn("Unit, origin, elapsed-vs-period basis", output)
        self.assertIn("`metadata.metric`", output)
        self.assertIn("null × 1, string × 1", output)
        self.assertIn("`playerPositions[].heartRate`", output)
        self.assertIn("numeric range 150..151", output)
        self.assertNotIn("source-id-1", output)
        self.assertNotIn("12.5", output)

    def test_reports_clock_order_only_when_derived_from_source_values(self):
        self.events[0]["clockSeconds"] = 20
        self.events[1]["clockSeconds"] = 12
        payload = json.dumps(self.events, separators=(",", ":")).encode()
        self.source.write_bytes(payload)
        self.write_manifest(payload)

        output = dictionary.render_dictionary(self.source, self.manifest)

        self.assertIn("source order not established as nondecreasing", output)

    def test_rejects_source_bytes_that_do_not_match_manifest(self):
        self.manifest.write_text(json.dumps({"source": {"sha256": "0" * 64, "byteSize": len(self.payload)}}))
        with self.assertRaisesRegex(dictionary.DictionaryError, "do not match"):
            dictionary.render_dictionary(self.source, self.manifest)

    def test_rejects_duplicate_json_object_keys(self):
        payload = b'[{"eventId":"one","eventId":"two"}]'
        self.source.write_bytes(payload)
        self.write_manifest(payload)
        with self.assertRaisesRegex(dictionary.DictionaryError, "Duplicate JSON object key"):
            dictionary.render_dictionary(self.source, self.manifest)

    def test_output_cannot_alias_source_or_manifest(self):
        with self.assertRaisesRegex(dictionary.DictionaryError, "must not alias"):
            dictionary._ensure_distinct_output(self.source, [self.source, self.manifest])


if __name__ == "__main__":
    unittest.main()
