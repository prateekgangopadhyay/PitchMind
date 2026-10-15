#!/usr/bin/env python3
"""Explicitly migrate LOCAL Docker PostgreSQL with the owner; provision the API role afterward."""

import json
import os
from pathlib import Path
import re
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
COMPOSE = ["docker", "compose", "--env-file", "docker/.env", "-f", "docker/compose.yml"]
PASSWORD = re.compile(r"^[0-9a-f]{48}$")


def fail(message: str) -> None:
    raise SystemExit(f"FAIL: {message}")


def read_api_password(path: Path) -> str:
    if path.is_symlink() or not path.is_file():
        fail("docker/.env must be a regular file; run ./scripts/dev-up.sh first")
    values = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("PITCHMIND_API_PASSWORD="):
            values.append(line.partition("=")[2])
    if len(values) != 1 or not PASSWORD.fullmatch(values[0]):
        fail("docker/.env must contain one valid PITCHMIND_API_PASSWORD; credentials were not displayed")
    return values[0]


def connection_value(value: str) -> str:
    return '"' + value.replace('"', '""') + '"'


def run() -> int:
    os.chdir(ROOT)
    api_password = read_api_password(ROOT / "docker" / ".env")
    compose_env = os.environ.copy()
    for credential_name in ("POSTGRES_PASSWORD", "PITCHMIND_API_PASSWORD", "REDIS_PASSWORD"):
        compose_env.pop(credential_name, None)
    result = subprocess.run(COMPOSE + ["config", "--format", "json"], capture_output=True, text=True,
                            env=compose_env)
    if result.returncode:
        fail("could not resolve local Compose configuration")
    try:
        postgres = json.loads(result.stdout)["services"]["postgres"]
        env = postgres["environment"]
        mapping = next(item for item in postgres["ports"] if int(item["target"]) == 5432)
        if mapping.get("host_ip") != "127.0.0.1":
            fail("local migration requires a loopback-only PostgreSQL mapping")
        host_port = int(mapping["published"])
        username = str(env["POSTGRES_USER"])
        password = str(env["POSTGRES_PASSWORD"])
        database = str(env["POSTGRES_DB"])
    except (KeyError, StopIteration, TypeError, ValueError, json.JSONDecodeError):
        fail("local PostgreSQL Compose configuration is incomplete")

    connection = (
        "Host=127.0.0.1;Port=" + str(host_port) + ";Username=" + connection_value(username)
        + ";Password=" + connection_value(password) + ";Database=" + connection_value(database)
        + ";Include Error Detail=false"
    )
    migration_env = os.environ.copy()
    migration_env.update({"Runtime__Profile": "local", "ConnectionStrings__Postgres": connection,
                          "PITCHMIND_API_PASSWORD": api_password})
    completed = subprocess.run(
        ["dotnet", "run", "--project", "src/PitchMind.DbMigrator"],
        env=migration_env,
        check=False,
    )
    return completed.returncode


if __name__ == "__main__":
    sys.exit(run())
