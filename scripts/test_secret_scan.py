#!/usr/bin/env python3
"""Positive and negative checks for PitchMind's pinned Gitleaks wrapper."""

import pathlib
import subprocess
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
SCANNER = ROOT / "scripts" / "secret-scan.sh"


class SecretScanTests(unittest.TestCase):
    def test_clean_tree_passes(self):
        with tempfile.TemporaryDirectory(prefix="pitchmind-secret-scan-clean-") as temp_dir:
            pathlib.Path(temp_dir, "README.txt").write_text(
                "This isolated fixture contains no credentials.\n", encoding="utf-8"
            )
            result = subprocess.run(
                ["bash", str(SCANNER), temp_dir],
                capture_output=True,
                text=True,
                timeout=120,
                check=False,
            )

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("no leaks found", result.stdout + result.stderr)

    def test_token_shaped_canary_fails_without_printing_secret(self):
        # Synthetic, unissued test value assembled from fragments so the
        # repository-level scanner does not find the canary in this test code.
        canary = "ghp_" + "AbCdEf0123456789GhIjKlMnOpQrStUvWxYz"
        with tempfile.TemporaryDirectory(prefix="pitchmind-secret-scan-canary-") as temp_dir:
            pathlib.Path(temp_dir, "canary.txt").write_text(
                f'token = "{canary}"\n', encoding="utf-8"
            )
            result = subprocess.run(
                ["bash", str(SCANNER), temp_dir],
                capture_output=True,
                text=True,
                timeout=120,
                check=False,
            )

        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("leaks found: 1", result.stdout + result.stderr)
        self.assertNotIn(canary, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
