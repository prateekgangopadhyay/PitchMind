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

    def test_rejects_secret_shaped_connection_values(self):
        secret_shaped = (
            "clientSecret=not-a-real-secret",
            "password: not-a-real-password",
            "Bearer not-a-real-token",
            "ghp_012345678901234567890123456789012345",
            "eyJhbGciOiJub25lIn0.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signaturepart123456",
        )
        for value in secret_shaped:
            with self.subTest(value=value), self.assertRaises(validator.IdentityInputError):
                validator.validate(value, "pitchmind-dev-deploy")


if __name__ == "__main__":
    unittest.main()
