"""Networkless regression tests for the local developer bootstrap scripts."""

from __future__ import annotations

import os
import shutil
import stat
import subprocess
import tempfile
import unittest
from pathlib import Path


REPOSITORY = Path(__file__).resolve().parents[1]
SCRIPTS = ("dev-up.sh", "dev-down.sh", "dev-reset.sh")


class DevBootstrapTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="pitchmind-bootstrap-")
        self.root = Path(self.temp.name) / "repo"
        (self.root / "scripts").mkdir(parents=True)
        (self.root / "docker").mkdir()
        for script in SCRIPTS:
            shutil.copy2(REPOSITORY / "scripts" / script, self.root / "scripts" / script)
        shutil.copy2(REPOSITORY / "docker" / "compose.yml", self.root / "docker" / "compose.yml")

        self.bin = Path(self.temp.name) / "bin"
        self.bin.mkdir()
        self.docker_log = Path(self.temp.name) / "docker.log"
        self.openssl_count = Path(self.temp.name) / "openssl.count"
        self._executable(
            "docker",
            """#!/bin/sh
set -eu
printf '%s\\n' \"$*\" >> \"$PITCHMIND_FAKE_DOCKER_LOG\"
if [ \"${1:-}\" = info ]; then exit \"${PITCHMIND_FAKE_DOCKER_INFO_STATUS:-0}\"; fi
exit 0
""",
        )
        self._executable(
            "openssl",
            """#!/bin/sh
set -eu
count=0
if [ -f \"$PITCHMIND_FAKE_OPENSSL_COUNT\" ]; then count=$(cat \"$PITCHMIND_FAKE_OPENSSL_COUNT\"); fi
count=$((count + 1))
printf '%s' \"$count\" > \"$PITCHMIND_FAKE_OPENSSL_COUNT\"
if [ \"$count\" -eq 1 ]; then printf '%048d\\n' 1; else printf '%048d\\n' 2; fi
""",
        )
        self.env = os.environ.copy()
        self.env.update(
            {
                "PATH": f"{self.bin}{os.pathsep}{self.env['PATH']}",
                "PITCHMIND_FAKE_DOCKER_LOG": str(self.docker_log),
                "PITCHMIND_FAKE_OPENSSL_COUNT": str(self.openssl_count),
            }
        )

    def tearDown(self) -> None:
        self.temp.cleanup()

    def _executable(self, name: str, contents: str) -> None:
        path = self.bin / name
        path.write_text(contents, encoding="utf-8")
        path.chmod(0o700)

    def _run(self, script: str, *args: str, input_text: str | None = None) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["bash", str(self.root / "scripts" / script), *args],
            cwd=self.root,
            env=self.env,
            input=input_text,
            capture_output=True,
            text=True,
            check=False,
        )

    def test_first_boot_generates_private_distinct_passwords_and_waits_for_health(self) -> None:
        result = self._run("dev-up.sh")
        env_file = self.root / "docker" / ".env"

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(stat.S_IMODE(env_file.stat().st_mode), 0o600)
        values = dict(line.split("=", 1) for line in env_file.read_text().splitlines())
        postgres_password = values["POSTGRES_PASSWORD"]
        redis_password = values["REDIS_PASSWORD"]
        self.assertRegex(postgres_password, r"^[0-9a-f]{48}$")
        self.assertRegex(redis_password, r"^[0-9a-f]{48}$")
        self.assertNotEqual(postgres_password, redis_password)
        self.assertNotIn(postgres_password, result.stdout + result.stderr)
        self.assertNotIn(redis_password, result.stdout + result.stderr)
        self.assertEqual(
            self.docker_log.read_text().splitlines(),
            ["info", "compose --env-file docker/.env -f docker/compose.yml up -d --wait"],
        )

    def test_docker_unavailable_fails_before_creating_local_credentials(self) -> None:
        self.env["PITCHMIND_FAKE_DOCKER_INFO_STATUS"] = "1"
        result = self._run("dev-up.sh")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Start Docker Desktop first", result.stderr)
        self.assertFalse((self.root / "docker" / ".env").exists())
        self.assertFalse(self.openssl_count.exists())

    def test_bootstrap_refuses_symlink_credentials_without_following_it(self) -> None:
        target = Path(self.temp.name) / "outside.env"
        target.write_text("DO_NOT_CHANGE=1\n", encoding="utf-8")
        link = self.root / "docker" / ".env"
        link.symlink_to(target)
        result = self._run("dev-up.sh")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Refusing to use docker/.env symlink", result.stderr)
        self.assertTrue(link.is_symlink())
        self.assertEqual(target.read_text(), "DO_NOT_CHANGE=1\n")

    def test_bootstrap_refuses_dangling_symlink_before_writing_credentials(self) -> None:
        target = Path(self.temp.name) / "outside.env"
        link = self.root / "docker" / ".env"
        link.symlink_to(target)
        result = self._run("dev-up.sh")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Refusing to use docker/.env symlink", result.stderr)
        self.assertTrue(link.is_symlink())
        self.assertFalse(target.exists())
        self.assertFalse(self.openssl_count.exists())

    def test_existing_strong_credentials_are_restricted_before_compose_start(self) -> None:
        env_file = self.root / "docker" / ".env"
        env_file.write_text(
            "POSTGRES_PASSWORD=" + "a" * 48 + "\nREDIS_PASSWORD=" + "b" * 48 + "\n",
            encoding="utf-8",
        )
        env_file.chmod(0o644)
        result = self._run("dev-up.sh")

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(
            env_file.read_text(),
            "POSTGRES_PASSWORD=" + "a" * 48 + "\nREDIS_PASSWORD=" + "b" * 48 + "\n",
        )
        self.assertEqual(stat.S_IMODE(env_file.stat().st_mode), 0o600)
        self.assertFalse(self.openssl_count.exists())

    def test_placeholder_or_short_existing_password_is_rejected_without_disclosure(self) -> None:
        env_file = self.root / "docker" / ".env"
        env_file.write_text(
            "POSTGRES_PASSWORD=replace-with-random-local-password\nREDIS_PASSWORD=short\n",
            encoding="utf-8",
        )
        result = self._run("dev-up.sh")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("must contain distinct, generated 48-character", result.stderr)
        self.assertNotIn("replace-with-random-local-password", result.stderr)
        self.assertNotIn("short", result.stderr)
        self.assertEqual(stat.S_IMODE(env_file.stat().st_mode), 0o600)
        self.assertEqual(self.docker_log.read_text().splitlines(), ["info"])

    def test_duplicate_password_keys_and_reused_password_are_rejected(self) -> None:
        valid = "a" * 48
        env_file = self.root / "docker" / ".env"
        env_file.write_text(
            f"POSTGRES_PASSWORD={valid}\nPOSTGRES_PASSWORD={valid}\nREDIS_PASSWORD={'b' * 48}\n",
            encoding="utf-8",
        )
        duplicate = self._run("dev-up.sh")
        self.assertNotEqual(duplicate.returncode, 0)
        self.assertEqual(self.docker_log.read_text().splitlines(), ["info"])

        self.docker_log.write_text("")
        env_file.write_text(f"POSTGRES_PASSWORD={valid}\nREDIS_PASSWORD={valid}\n", encoding="utf-8")
        reused = self._run("dev-up.sh")
        self.assertNotEqual(reused.returncode, 0)
        self.assertEqual(self.docker_log.read_text().splitlines(), ["info"])

    def test_stop_keeps_volumes_and_reset_requires_explicit_force_in_noninteractive_mode(self) -> None:
        (self.root / "docker" / ".env").write_text("POSTGRES_PASSWORD=x\nREDIS_PASSWORD=y\n")
        stop = self._run("dev-down.sh")
        self.assertEqual(stop.returncode, 0, stop.stderr)
        self.assertIn("compose --env-file docker/.env -f docker/compose.yml down", self.docker_log.read_text())
        self.assertNotIn("--volumes", self.docker_log.read_text())

        guarded = self._run("dev-reset.sh")
        self.assertEqual(guarded.returncode, 2)
        self.assertIn("run interactively or pass --force", guarded.stderr)
        before_force = self.docker_log.read_text()
        self.assertNotIn("--volumes", before_force)

        forced = self._run("dev-reset.sh", "--force")
        self.assertEqual(forced.returncode, 0, forced.stderr)
        self.assertIn("down --volumes --remove-orphans", self.docker_log.read_text())
        self.assertTrue((self.root / "docker" / ".env").is_file())

    def test_compose_services_bind_only_to_loopback(self) -> None:
        compose = (REPOSITORY / "docker" / "compose.yml").read_text(encoding="utf-8")
        self.assertIn('"127.0.0.1:${POSTGRES_PORT:-5432}:5432"', compose)
        self.assertIn('"127.0.0.1:${REDIS_PORT:-6379}:6379"', compose)


if __name__ == "__main__":
    unittest.main()
