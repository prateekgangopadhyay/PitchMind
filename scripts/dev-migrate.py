#!/usr/bin/env python3
"""Explicitly migrate the LOCAL Docker PostgreSQL database using its owner identity."""

import json
import os
from pathlib import Path
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
COMPOSE = ["docker", "compose", "--env-file", "docker/.env", "-f", "docker/compose.yml"]


def fail(message: str) -> None:
    raise SystemExit(f"FAIL: {message}")


def quote(value: object) -> str:
    return '"' + str(value).replace('"', '""') + '"'


def run() -> int:
    os.chdir(ROOT)
    if not Path("docker/.env").is_file():
        fail("run ./scripts/dev-up.sh first to create local credentials and start PostgreSQL")

    config = subprocess.run(COMPOSE + ["config", "--format", "json"], capture_output=True, text=True)
    if config.returncode:
        fail("could not resolve local Compose configuration")
    try:
        service = json.loads(config.stdout)["services"]["postgres"]
        environment = service["environment"]
        port = next(item for item in service["ports"] if int(item["target"]) == 5432)
        if port.get("host_ip") != "127.0.0.1":
            fail("local migration requires a loopback-only PostgreSQL mapping")
        username = environment["POSTGRES_USER"]
        password = environment["POSTGRES_PASSWORD"]
        database = environment["POSTGRES_DB"]
        host_port = int(port["published"])
    except (KeyError, StopIteration, TypeError, ValueError):
        fail("local PostgreSQL Compose configuration is incomplete")

    migration_environment = os.environ.copy()
    migration_environment.update(
        {
            "Runtime__Profile": "local",
            "ConnectionStrings__Postgres": (
                "Host=127.0.0.1;Port=" + str(host_port) + ";Username=" + quote(username)
                + ";Password=" + quote(password) + ";Database=" + quote(database)
                + ";Include Error Detail=false"
            ),
        }
    )
    migration = subprocess.run(
        ["dotnet", "run", "--project", "src/PitchMind.DbMigrator"],
        env=migration_environment,
    )
    if migration.returncode:
        return migration.returncode

    permissions = subprocess.run(
        COMPOSE
        + [
            "exec",
            "-T",
            "postgres",
            "sh",
            "-c",
            'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -f /docker-entrypoint-initdb.d/20-local-worker-role.sql',
        ],
        stdout=subprocess.DEVNULL,
    )
    if permissions.returncode:
        fail("local worker permissions could not be applied after migration")
    print("Applied LOCAL migrations and refreshed least-privilege worker grants.")
    return 0


if __name__ == "__main__":
    sys.exit(run())
