#!/usr/bin/env python3
"""Positive and negative checks for PitchMind's pinned Gitleaks wrapper."""

import pathlib
import subprocess
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
SCANNER = ROOT / "scripts" / "secret-scan.sh"


def synthetic_canary():
    # Avoid compile-time constant folding so even bytecode caches cannot contain
    # the complete token-shaped test value.
    return "".join(("ghp_", "AbCdEf0123456789GhIjKlMnOpQrStUvWxYz"))


class SecretScanTests(unittest.TestCase):
    def run_scan(self, source):
        return subprocess.run(
            ["bash", str(SCANNER), str(source)],
            capture_output=True,
            text=True,
            timeout=120,
            check=False,
        )

    def test_clean_tree_passes(self):
        with tempfile.TemporaryDirectory(prefix="pitchmind-secret-scan-clean-") as temp_dir:
            pathlib.Path(temp_dir, "README.txt").write_text(
                "This isolated fixture contains no credentials.\n", encoding="utf-8"
            )
            result = self.run_scan(temp_dir)

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("no leaks found", result.stdout + result.stderr)

    def test_token_shaped_canary_fails_without_printing_secret(self):
        # Synthetic, unissued test value assembled from fragments so the
        # repository-level scanner does not find the canary in this test code.
        canary = synthetic_canary()
        with tempfile.TemporaryDirectory(prefix="pitchmind-secret-scan-canary-") as temp_dir:
            pathlib.Path(temp_dir, "canary.txt").write_text(
                f'token = "{canary}"\n', encoding="utf-8"
            )
            result = self.run_scan(temp_dir)

        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("leaks found: 1", result.stdout + result.stderr)
        self.assertNotIn(canary, result.stdout + result.stderr)

    def test_repository_config_cannot_replace_trusted_rules(self):
        canary = synthetic_canary()
        with tempfile.TemporaryDirectory(prefix="pitchmind-secret-scan-config-") as temp_dir:
            pathlib.Path(temp_dir, ".gitleaks.toml").write_text(
                'title = "Untrusted empty rules"\n[extend]\nuseDefault = false\n',
                encoding="utf-8",
            )
            pathlib.Path(temp_dir, "canary.txt").write_text(
                f'token = "{canary}"\n', encoding="utf-8"
            )
            result = self.run_scan(temp_dir)

        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("leaks found: 1", result.stdout + result.stderr)
        self.assertNotIn(canary, result.stdout + result.stderr)

    def test_inline_allow_cannot_suppress_canary(self):
        canary = synthetic_canary()
        with tempfile.TemporaryDirectory(prefix="pitchmind-secret-scan-inline-allow-") as temp_dir:
            pathlib.Path(temp_dir, "canary.txt").write_text(
                f'token = "{canary}" # gitleaks:allow\n', encoding="utf-8"
            )
            result = self.run_scan(temp_dir)

        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("leaks found: 1", result.stdout + result.stderr)
        self.assertNotIn(canary, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
