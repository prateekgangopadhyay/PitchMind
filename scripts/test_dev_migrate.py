"""Credential and LOCAL connection guards for the explicit migration launcher."""

from __future__ import annotations

import importlib.util
from pathlib import Path
import tempfile
import unittest


MODULE_PATH = Path(__file__).with_name("dev-migrate.py")
SPEC = importlib.util.spec_from_file_location("dev_migrate", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
dev_migrate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(dev_migrate)


class LocalMigrationLauncherTests(unittest.TestCase):
    def test_reads_one_strong_api_password_without_echoing_it(self) -> None:
        sample_value = "".join(["0123456789abcdef"] * 3)
        with tempfile.TemporaryDirectory() as directory:
            env_file = Path(directory) / ".env"
            env_file.write_text(f"POSTGRES_USER=pitchmind\nPITCHMIND_API_PASSWORD={sample_value}\n", encoding="utf-8")
            self.assertEqual(sample_value, dev_migrate.read_api_password(env_file))

    def test_rejects_missing_duplicate_and_weak_api_passwords(self) -> None:
        cases = ("POSTGRES_USER=pitchmind\n", "PITCHMIND_API_PASSWORD=bad\n",
                 f"PITCHMIND_API_PASSWORD={'a' * 48}\n",
                 f"PITCHMIND_API_PASSWORD={'a' * 48}\nPITCHMIND_API_PASSWORD={'b' * 48}\n")
        with tempfile.TemporaryDirectory() as directory:
            env_file = Path(directory) / ".env"
            for contents in cases:
                with self.subTest(lines=len(contents.splitlines())):
                    env_file.write_text(contents, encoding="utf-8")
                    with self.assertRaises(SystemExit):
                        dev_migrate.read_api_password(env_file)

    def test_refuses_symlink_credentials_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / "private.env"
            target.write_text(f"PITCHMIND_API_PASSWORD={'0123456789abcdef' * 3}\n", encoding="utf-8")
            link = root / ".env"
            link.symlink_to(target)
            with self.assertRaises(SystemExit):
                dev_migrate.read_api_password(link)

    def test_npgsql_quoted_connection_values_escape_quotes(self) -> None:
        self.assertEqual('"owner""role"', dev_migrate.connection_value('owner"role'))


if __name__ == "__main__":
    unittest.main()
