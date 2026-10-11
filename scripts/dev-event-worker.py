#!/usr/bin/env python3
"""Run the LOCAL outbox worker using Docker Compose credentials without printing them."""

import json
import os
from pathlib import Path
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
COMPOSE = ["docker", "compose", "--env-file", "docker/.env", "-f", "docker/compose.yml"]


def fail(message: str) -> None:
    raise SystemExit(f"FAIL: {message}")


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
            fail("EventWorker requires a loopback-only local PostgreSQL mapping")
        username = "pitchmind_worker"
        password = environment["PITCHMIND_WORKER_PASSWORD"]
        database = environment["POSTGRES_DB"]
        host_port = int(port["published"])
    except (KeyError, StopIteration, TypeError, ValueError):
        fail("local PostgreSQL Compose configuration is incomplete")

    def quote(value: object) -> str:
        return '"' + str(value).replace('"', '""') + '"'

    worker_environment = os.environ.copy()
    worker_environment.update(
        {
            "Runtime__Profile": "local",
            "Outbox__Provider": "postgres-local",
            "ConnectionStrings__Postgres": (
                "Host=127.0.0.1;Port=" + str(host_port) + ";Username=" + quote(username)
                + ";Password=" + quote(password) + ";Database=" + quote(database)
                + ";Include Error Detail=false"
            ),
        }
    )
    print("Starting LOCAL EventWorker. Credentials remain in process memory.")
    return subprocess.run(
        ["dotnet", "run", "--project", "src/PitchMind.EventWorker"],
        env=worker_environment,
    ).returncode


if __name__ == "__main__":
    sys.exit(run())
