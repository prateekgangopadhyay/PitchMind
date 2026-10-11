"""Tests for the fail-closed Azure release identity parameter guard."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import unittest

SCRIPT = Path(__file__).with_name("validate_release_identity_inputs.py")
spec = importlib.util.spec_from_file_location("release_identity_inputs", SCRIPT)
validator = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(validator)


class ReleaseIdentityInputTests(unittest.TestCase):
    def test_accepts_distinct_configured_connections(self):
        self.assertIsNone(validator.validate("pitchmind-acr-push", "pitchmind-dev-deploy"))

    def test_rejects_same_connection_even_with_case_differences(self):
        with self.assertRaisesRegex(validator.IdentityInputError, "must be distinct"):
            validator.validate("PitchMind-Release", "pitchmind-release")

    def test_rejects_empty_or_placeholder_connections(self):
        for values in ((None, "deploy"), ("push", "  "), ("unconfigured", "deploy"), ("push", "UNCONFIGURED")):
            with self.subTest(values=values), self.assertRaises(validator.IdentityInputError):
                validator.validate(*values)


if __name__ == "__main__":
    unittest.main()
