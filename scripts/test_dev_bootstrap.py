"""Networkless regression tests for the local developer bootstrap scripts."""

from __future__ import annotations

import ipaddress
import json
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
if [ "${1:-}" = context ] && [ "${2:-}" = show ]; then
  printf '%s\n' "${PITCHMIND_FAKE_DOCKER_CONTEXT:-desktop-linux}"
  exit 0
fi
if [ "${1:-}" = context ] && [ "${2:-}" = inspect ]; then
  printf '%s\n' "${PITCHMIND_FAKE_DOCKER_ENDPOINT:-unix:///var/run/docker.sock}"
  exit 0
fi
if [ \"${1:-}\" = info ]; then exit \"${PITCHMIND_FAKE_DOCKER_INFO_STATUS:-0}\"; fi
if [ \"${1:-}\" = compose ]; then
  if [ \"${PITCHMIND_FAKE_ASSERT_COMPOSE_CREDENTIALS_UNSET:-0}\" = 1 ] \\
    && { [ \"${POSTGRES_PASSWORD+x}\" = x ] || [ \"${REDIS_PASSWORD+x}\" = x ]; }; then
    exit 73
  fi
  exit \"${PITCHMIND_FAKE_COMPOSE_STATUS:-0}\"
fi
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
if [ \"$count\" -eq 1 ]; then printf '0123456789abcdef0123456789abcdef0123456789abcdef\\n'; else printf 'fedcba9876543210fedcba9876543210fedcba9876543210\\n'; fi
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

    def _run(
        self,
        script: str,
        *args: str,
        input_text: str | None = None,
        timeout: float = 5.0,
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["bash", str(self.root / "scripts" / script), *args],
            cwd=self.root,
            env=self.env,
            input=input_text,
            capture_output=True,
            text=True,
            check=False,
            timeout=timeout,
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
            [
                "context show",
                "context inspect desktop-linux --format {{(index .Endpoints \"docker\").Host}}",
                "info",
                "compose --env-file docker/.env -f docker/compose.yml up -d --wait",
            ],
        )

    def test_remote_context_fails_before_credentials_or_docker_info(self) -> None:
        self.env["PITCHMIND_FAKE_DOCKER_ENDPOINT"] = "ssh://builder@example.invalid"

        result = self._run("dev-up.sh")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Refusing non-local Docker endpoint", result.stderr)
        self.assertFalse((self.root / "docker" / ".env").exists())
        self.assertFalse(self.openssl_count.exists())
        self.assertEqual(
            self.docker_log.read_text().splitlines(),
            [
                "context show",
                "context inspect desktop-linux --format {{(index .Endpoints \"docker\").Host}}",
            ],
        )

    def test_remote_docker_host_override_is_rejected(self) -> None:
        self.env["DOCKER_HOST"] = "tcp://builder.example.invalid:2376"

        result = self._run("dev-up.sh")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Refusing non-local Docker endpoint", result.stderr)
        self.assertFalse((self.root / "docker" / ".env").exists())
        self.assertFalse(self.docker_log.exists())

    def test_docker_context_overrides_remote_docker_host(self) -> None:
        self.env["DOCKER_CONTEXT"] = "desktop-local"
        self.env["DOCKER_HOST"] = "tcp://builder.example.invalid:2376"

        result = self._run("dev-up.sh")

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(
            self.docker_log.read_text().splitlines(),
            [
                "context inspect desktop-local --format {{(index .Endpoints \"docker\").Host}}",
                "info",
                "compose --env-file docker/.env -f docker/compose.yml up -d --wait",
            ],
        )

    def test_remote_docker_context_overrides_local_docker_host(self) -> None:
        self.env["DOCKER_CONTEXT"] = "remote-builder"
        self.env["DOCKER_HOST"] = "unix:///var/run/docker.sock"
        self.env["PITCHMIND_FAKE_DOCKER_ENDPOINT"] = "ssh://builder@example.invalid"

        result = self._run("dev-up.sh")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Refusing non-local Docker endpoint", result.stderr)
        self.assertFalse((self.root / "docker" / ".env").exists())
        self.assertFalse(self.openssl_count.exists())
        self.assertEqual(
            self.docker_log.read_text().splitlines(),
            [
                "context inspect remote-builder --format {{(index .Endpoints \"docker\").Host}}",
            ],
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

    def test_bootstrap_refuses_fifo_credentials_without_blocking_or_writing_secrets(self) -> None:
        credentials = self.root / "docker" / ".env"
        os.mkfifo(credentials)

        result = self._run("dev-up.sh", timeout=2.0)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Refusing non-regular docker/.env path", result.stderr)
        self.assertTrue(credentials.is_fifo())
        self.assertFalse(self.openssl_count.exists())
        self.assertFalse(self.docker_log.exists())

    def test_existing_strong_credentials_are_restricted_before_compose_start(self) -> None:
        env_file = self.root / "docker" / ".env"
        postgres_password = "0123456789abcdef0123456789abcdef0123456789abcdef"
        redis_password = "fedcba9876543210fedcba9876543210fedcba9876543210"
        env_file.write_text(
            f"POSTGRES_PASSWORD={postgres_password}\nREDIS_PASSWORD={redis_password}\n",
            encoding="utf-8",
        )
        env_file.chmod(0o644)
        result = self._run("dev-up.sh")

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(
            env_file.read_text(),
            f"POSTGRES_PASSWORD={postgres_password}\nREDIS_PASSWORD={redis_password}\n",
        )
        self.assertEqual(stat.S_IMODE(env_file.stat().st_mode), 0o600)
        self.assertFalse(self.openssl_count.exists())

    def test_exported_passwords_cannot_override_validated_env_file(self) -> None:
        env_file = self.root / "docker" / ".env"
        env_file.write_text(
            "POSTGRES_PASSWORD=0123456789abcdef0123456789abcdef0123456789abcdef\n"
            "REDIS_PASSWORD=fedcba9876543210fedcba9876543210fedcba9876543210\n",
            encoding="utf-8",
        )
        self.env["POSTGRES_PASSWORD"] = "weak-shell-override"
        self.env["REDIS_PASSWORD"] = "reused-shell-override"
        self.env["PITCHMIND_FAKE_ASSERT_COMPOSE_CREDENTIALS_UNSET"] = "1"

        result = self._run("dev-up.sh")

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("compose --env-file docker/.env", self.docker_log.read_text())

    def test_compose_readiness_failure_explains_safe_local_recovery(self) -> None:
        env_file = self.root / "docker" / ".env"
        env_file.write_text(
            "POSTGRES_PASSWORD=0123456789abcdef0123456789abcdef0123456789abcdef\n"
            "REDIS_PASSWORD=fedcba9876543210fedcba9876543210fedcba9876543210\n",
            encoding="utf-8",
        )
        self.env["PITCHMIND_FAKE_COMPOSE_STATUS"] = "1"

        result = self._run("dev-up.sh")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Inspect Docker logs", result.stderr)
        self.assertIn("./scripts/dev-down.sh", result.stderr)
        self.assertIn("volumes are preserved", result.stderr)
        self.assertEqual(
            self.docker_log.read_text().splitlines(),
            [
                "context show",
                "context inspect desktop-linux --format {{(index .Endpoints \"docker\").Host}}",
                "info",
                "compose --env-file docker/.env -f docker/compose.yml up -d --wait",
            ],
        )

    def test_placeholder_or_short_existing_password_is_rejected_without_disclosure(self) -> None:
        env_file = self.root / "docker" / ".env"
        env_file.write_text(
            "POSTGRES_PASSWORD=replace-with-random-local-password\nREDIS_PASSWORD=short\n",
            encoding="utf-8",
        )
        result = self._run("dev-up.sh")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("must contain distinct 48-character", result.stderr)
        self.assertNotIn("replace-with-random-local-password", result.stderr)
        self.assertNotIn("short", result.stderr)
        self.assertEqual(stat.S_IMODE(env_file.stat().st_mode), 0o600)
        self.assertEqual(
            self.docker_log.read_text().splitlines(),
            [
                "context show",
                "context inspect desktop-linux --format {{(index .Endpoints \"docker\").Host}}",
                "info",
            ],
        )

    def test_low_diversity_password_is_rejected(self) -> None:
        env_file = self.root / "docker" / ".env"
        env_file.write_text(
            f"POSTGRES_PASSWORD={'a' * 48}\nREDIS_PASSWORD={'b' * 48}\n",
            encoding="utf-8",
        )
        result = self._run("dev-up.sh")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("at least 12 unique hex digits", result.stderr)
        self.assertEqual(
            self.docker_log.read_text().splitlines(),
            [
                "context show",
                "context inspect desktop-linux --format {{(index .Endpoints \"docker\").Host}}",
                "info",
            ],
        )

    def test_duplicate_password_keys_and_reused_password_are_rejected(self) -> None:
        valid = "0123456789abcdef0123456789abcdef0123456789abcdef"
        distinct = "fedcba9876543210fedcba9876543210fedcba9876543210"
        env_file = self.root / "docker" / ".env"
        env_file.write_text(
            f"POSTGRES_PASSWORD={valid}\nPOSTGRES_PASSWORD={valid}\nREDIS_PASSWORD={distinct}\n",
            encoding="utf-8",
        )
        duplicate = self._run("dev-up.sh")
        self.assertNotEqual(duplicate.returncode, 0)
        self.assertEqual(
            self.docker_log.read_text().splitlines(),
            [
                "context show",
                "context inspect desktop-linux --format {{(index .Endpoints \"docker\").Host}}",
                "info",
            ],
        )

        self.docker_log.write_text("")
        env_file.write_text(f"POSTGRES_PASSWORD={valid}\nREDIS_PASSWORD={valid}\n", encoding="utf-8")
        reused = self._run("dev-up.sh")
        self.assertNotEqual(reused.returncode, 0)
        self.assertEqual(
            self.docker_log.read_text().splitlines(),
            [
                "context show",
                "context inspect desktop-linux --format {{(index .Endpoints \"docker\").Host}}",
                "info",
            ],
        )

    def test_compose_alternate_password_assignments_are_counted_as_duplicates(self) -> None:
        valid = "0123456789abcdef0123456789abcdef0123456789abcdef"
        distinct = "fedcba9876543210fedcba9876543210fedcba9876543210"
        cases = (
            ("POSTGRES_PASSWORD", "POSTGRES_PASSWORD: weak"),
            ("POSTGRES_PASSWORD", "POSTGRES_PASSWORD = weak"),
            ("REDIS_PASSWORD", "REDIS_PASSWORD: weak"),
            ("REDIS_PASSWORD", "REDIS_PASSWORD = weak"),
            ("POSTGRES_PASSWORD", "export POSTGRES_PASSWORD=weak"),
            ("REDIS_PASSWORD", "export REDIS_PASSWORD=weak"),
        )

        for key, duplicate in cases:
            with self.subTest(key=key, duplicate=duplicate):
                self.docker_log.unlink(missing_ok=True)
                env_file = self.root / "docker" / ".env"
                values = {
                    "POSTGRES_PASSWORD": valid,
                    "REDIS_PASSWORD": distinct,
                }
                env_file.write_text(
                    f"POSTGRES_PASSWORD={values['POSTGRES_PASSWORD']}\n"
                    f"REDIS_PASSWORD={values['REDIS_PASSWORD']}\n"
                    f"{duplicate}\n",
                    encoding="utf-8",
                )
                result = self._run("dev-up.sh")

                self.assertNotEqual(result.returncode, 0)
                self.assertIn("must contain distinct 48-character", result.stderr)
                self.assertEqual(
            self.docker_log.read_text().splitlines(),
            [
                "context show",
                "context inspect desktop-linux --format {{(index .Endpoints \"docker\").Host}}",
                "info",
            ],
        )

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

    def _resolved_compose_config(
        self,
        compose_file: Path,
        env_overrides: dict[str, str] | None = None,
    ) -> dict[str, object]:
        docker = shutil.which("docker")
        if docker is None:
            self.skipTest("Docker Compose CLI is unavailable for resolved-port validation")
        env = {
            key: value
            for key, value in os.environ.items()
            if key
            not in {
                "POSTGRES_USER",
                "POSTGRES_DB",
                "POSTGRES_PASSWORD",
                "POSTGRES_PORT",
                "REDIS_PASSWORD",
                "REDIS_PORT",
                "COMPOSE_PROJECT_NAME",
            }
        }
        if env_overrides:
            env.update(env_overrides)
        result = subprocess.run(
            [
                docker,
                "compose",
                "--env-file",
                str(REPOSITORY / "docker" / ".env.example"),
                "-f",
                str(compose_file),
                "config",
                "--format",
                "json",
            ],
            cwd=REPOSITORY,
            env=env,
            capture_output=True,
            text=True,
            check=False,
            timeout=10,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout)

    @staticmethod
    def _published_ports(config: dict[str, object]) -> list[tuple[str, dict[str, object]]]:
        services = config.get("services", {})
        if not isinstance(services, dict):
            return []
        bindings = []
        for service_name, service in services.items():
            if not isinstance(service, dict):
                continue
            ports = service.get("ports", [])
            if isinstance(ports, list):
                bindings.extend(
                    (service_name, port)
                    for port in ports
                    if isinstance(port, dict)
                )
        return bindings

    @classmethod
    def _published_ports_are_loopback(cls, config: dict[str, object]) -> bool:
        bindings = cls._published_ports(config)
        if not bindings:
            return False
        for _, port in bindings:
            host_ip = port.get("host_ip")
            if not isinstance(host_ip, str):
                return False
            try:
                if not ipaddress.ip_address(host_ip).is_loopback:
                    return False
            except ValueError:
                return False
        return True

    def test_compose_services_bind_only_to_loopback(self) -> None:
        config = self._resolved_compose_config(REPOSITORY / "docker" / "compose.yml")
        published_ports = self._published_ports(config)
        self.assertEqual(config.get("name"), "pitchmind")
        self.assertEqual(
            {
                key: volume.get("name")
                for key, volume in config.get("volumes", {}).items()
                if isinstance(volume, dict)
            },
            {
                "postgres_data": "pitchmind_postgres_data",
                "redis-data": "pitchmind_redis_data",
            },
        )
        self.assertEqual(len(published_ports), 2)
        self.assertTrue(self._published_ports_are_loopback(config), published_ports)
        self.assertEqual(
            {(service, port.get("published"), port.get("target")) for service, port in published_ports},
            {("postgres", "5432", 5432), ("redis", "6379", 6379)},
        )

    def test_compose_resolves_the_existing_local_volume_names_offline(self) -> None:
        config = self._resolved_compose_config(REPOSITORY / "docker" / "compose.yml")

        self.assertEqual(config.get("name"), "pitchmind")
        services = config.get("services", {})
        self.assertIsInstance(services, dict)
        self.assertEqual(services["postgres"].get("image"), "postgres:16-alpine")
        postgres_mounts = services["postgres"].get("volumes", [])
        self.assertEqual(
            [(mount.get("source"), mount.get("target")) for mount in postgres_mounts],
            [("postgres_data", "/var/lib/postgresql/data")],
        )
        redis_mounts = services["redis"].get("volumes", [])
        self.assertEqual(
            [(mount.get("source"), mount.get("target")) for mount in redis_mounts],
            [("redis-data", "/data")],
        )
        self.assertEqual(
            config.get("volumes"),
            {
                "postgres_data": {"name": "pitchmind_postgres_data"},
                "redis-data": {"name": "pitchmind_redis_data"},
            },
        )

    def test_alternate_compose_project_uses_isolated_volume_names(self) -> None:
        project = "pitchmind-bootstrap-isolated"
        config = self._resolved_compose_config(
            REPOSITORY / "docker" / "compose.yml",
            {"COMPOSE_PROJECT_NAME": project},
        )

        self.assertEqual(config.get("name"), project)
        self.assertEqual(
            config.get("volumes"),
            {
                "postgres_data": {"name": f"{project}_postgres_data"},
                "redis-data": {"name": f"{project}_redis_data"},
            },
        )

    def test_resolved_compose_port_check_handles_inline_lists_and_aliases(self) -> None:
        compose_file = Path(self.temp.name) / "compose-public-ports.yml"
        compose_file.write_text(
            """name: pitchmind-port-regression
x-public-ports: &public_ports
  - "0.0.0.0:8081:8081"
services:
  inline:
    image: alpine:3.20
    ports: ["0.0.0.0:8080:8080"]
  aliased:
    image: alpine:3.20
    ports: *public_ports
  implicit_host_binding:
    image: alpine:3.20
    ports: ["8082"]
""",
            encoding="utf-8",
        )
        config = self._resolved_compose_config(compose_file)
        published_ports = self._published_ports(config)
        self.assertEqual(len(published_ports), 3)
        self.assertFalse(self._published_ports_are_loopback(config), published_ports)
        self.assertIn("0.0.0.0", {port.get("host_ip") for _, port in published_ports})
        self.assertIn(None, {port.get("host_ip") for _, port in published_ports})


if __name__ == "__main__":
    unittest.main()
