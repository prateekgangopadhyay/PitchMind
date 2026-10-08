#!/usr/bin/env python3
"""Standard-library, networkless tests for feed profiling utility."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest

MODULE = Path(__file__).with_name('profile_simulation_feed.py')
spec = importlib.util.spec_from_file_location('feed_profiler', MODULE)
profiler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profiler)


class FeedProfilerTests(unittest.TestCase):
    def test_profiles_field_types_without_leaking_values(self):
        with TemporaryDirectory() as folder:
            path = Path(folder) / 'owner.json'
            path.write_text(json.dumps({'records': [{'sequence': 0, 'secret': 'PRIVATE-MOCK-DO-NOT-LOG'}, {'sequence': 1, 'secret': None}]}))
            report = profiler.profile(path)
            serialized = json.dumps(report)
            self.assertNotIn('PRIVATE-MOCK-DO-NOT-LOG', serialized)
            self.assertEqual(report['status'], 'profiled_not_validated')
            self.assertEqual(report['arrayLengths']['$["records"]']['maxLength'], 2)
            self.assertEqual(report['fieldTypes']['$["records"][]["secret"]'], {'null': 1, 'string': 1})
            self.assertEqual(report['sha256'], profiler.sha256(path.read_bytes()).hexdigest())

    def test_invalid_or_missing_data_fails_closed(self):
        with TemporaryDirectory() as folder:
            bad = Path(folder) / 'bad.json'
            bad.write_text('{invalid')
            with self.assertRaisesRegex(ValueError, 'Malformed JSON'):
                profiler.profile(bad)
            with self.assertRaisesRegex(ValueError, 'existing regular file'):
                profiler.profile(Path(folder) / 'missing.json')
            too_big = Path(folder) / 'large.json'
            too_big.write_text('"' + 'x' * 200 + '"')
            with self.assertRaisesRegex(ValueError, 'exceeds'):
                profiler.profile(too_big, max_bytes=100)

    def test_ndjson_is_explicit_and_bounded(self):
        with TemporaryDirectory() as folder:
            path = Path(folder) / 'events.ndjson'
            path.write_text('{"sequence":1}\n{"sequence":2}\n')
            report = profiler.profile(path, ndjson=True)
            self.assertEqual(report['format'], 'ndjson')
            self.assertEqual(report['arrayLengths']['$']['maxLength'], 2)
            with self.assertRaisesRegex(ValueError, 'Malformed JSON'):
                profiler.profile(path)

    def test_cli_never_overwrites_source(self):
        with TemporaryDirectory() as folder:
            path = Path(folder) / 'data.json'
            path.write_text('{}')
            result = subprocess.run([sys.executable, str(MODULE), str(path), '--output', str(path)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 2)
            self.assertEqual(path.read_text(), '{}')

    def test_non_finite_fails_closed(self):
        with TemporaryDirectory() as folder:
            path = Path(folder) / 'bad.json'
            path.write_text('{"x":NaN}')
            with self.assertRaisesRegex(ValueError, 'NaN or Infinity'):
                profiler.profile(path)


if __name__ == '__main__':
    unittest.main()
