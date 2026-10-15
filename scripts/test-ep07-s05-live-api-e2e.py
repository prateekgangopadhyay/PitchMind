#!/usr/bin/env python3
"""Run EP07-S05's persisted PostgreSQL -> local API -> Playwright journey."""

from __future__ import annotations

import json
import os
from pathlib import Path
import signal
import socket
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
DOCKER_ENV = Path(os.environ.get("PITCHMIND_DOCKER_ENV", ROOT / "docker/.env")).expanduser().resolve()
COMPOSE = ["docker", "compose", "--env-file", str(DOCKER_ENV), "-f", "docker/compose.yml"]
API_PORT = 5080  # frontend/vite.config.ts proxies /api to this loopback listener


def run(args: list[str], cwd: Path = ROOT, **kwargs) -> subprocess.CompletedProcess:
    return subprocess.run(args, cwd=cwd, check=False, **kwargs)


def fail(message: str) -> None:
    raise RuntimeError(message)


def quote_connection_value(value: object) -> str:
    return '"' + str(value).replace('"', '""') + '"'


def stop_process_group(process: subprocess.Popen) -> None:
    """Stop dotnet and any app child it started before dropping the test DB."""
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        pass

    # The dotnet parent can exit before its child. Check the whole group and
    # escalate if anything is still alive, including when the parent exited.
    try:
        os.killpg(process.pid, 0)
    except ProcessLookupError:
        return
    os.killpg(process.pid, signal.SIGKILL)
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        pass


def wait_for_api_port_release() -> bool:
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        with socket.socket() as probe:
            probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            try:
                probe.bind(("127.0.0.1", API_PORT))
                return True
            except OSError:
                time.sleep(0.1)
    return False


def main() -> int:
    if not DOCKER_ENV.is_file():
        fail("Set PITCHMIND_DOCKER_ENV to the local Docker Compose .env file, or create docker/.env using docker/.env.example.")
    if not (ROOT / "frontend/node_modules/.bin/playwright").exists():
        fail("Install the repository's frontend dependencies before running this journey (npm ci in frontend/).")
    for executable in ("docker", "dotnet", "npm"):
        if shutil.which(executable) is None:
            fail(f"Required local executable is missing: {executable}")

    with socket.socket() as probe:
        probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            probe.bind(("127.0.0.1", API_PORT))
        except OSError:
            fail(f"Port {API_PORT} is already in use; stop the other local API before running this isolated journey.")

    # Reuse the already-running local service. Never run `compose up` here: a
    # worktree's Compose model can differ from the primary checkout's live config.
    resolved = run(COMPOSE + ["config", "--format", "json"], capture_output=True, text=True)
    if resolved.returncode:
        fail("Could not resolve the local Compose PostgreSQL configuration.")
    try:
        postgres = json.loads(resolved.stdout)["services"]["postgres"]
        env = postgres["environment"]
        port = next(item for item in postgres["ports"] if int(item["target"]) == 5432)
        if port.get("host_ip") != "127.0.0.1":
            fail("The local PostgreSQL port must be bound to 127.0.0.1 for this test harness.")
        username, password, host_port = env["POSTGRES_USER"], env["POSTGRES_PASSWORD"], int(port["published"])
    except (KeyError, StopIteration, TypeError, ValueError):
        fail("The local Compose PostgreSQL service must expose a loopback-bound port and POSTGRES_USER/PASSWORD.")

    service = run(COMPOSE + ["ps", "-q", "postgres"], capture_output=True, text=True)
    container_id = service.stdout.strip()
    if service.returncode or not container_id:
        fail("Start the local PostgreSQL service with the primary checkout's Compose configuration first.")

    database = "pitchmind_ep07_s05_" + uuid.uuid4().hex

    def pg(args: list[str]) -> subprocess.CompletedProcess:
        return run(["docker", "exec", container_id] + args, capture_output=True, text=True)

    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        ready = pg(["pg_isready", "-U", str(username)])
        if ready.returncode == 0:
            break
        time.sleep(1)
    else:
        fail("Local Compose PostgreSQL did not become ready within 60 seconds.")

    created = pg(["createdb", "-U", str(username), database])
    if created.returncode:
        fail("Could not create a uniquely named isolated PostgreSQL database.")

    connection = ";".join([
        f"Host=127.0.0.1", f"Port={host_port}", f"Username={quote_connection_value(username)}",
        f"Password={quote_connection_value(password)}", f"Database={quote_connection_value(database)}",
        "Include Error Detail=false"
    ])
    test_env = os.environ.copy()
    test_env["PITCHMIND_TEST_POSTGRES"] = connection
    api_env = os.environ.copy()
    api_env.update({
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": f"http://127.0.0.1:{API_PORT}",
        "Runtime__Profile": "local",
        "ConnectionStrings__PitchMind": connection,
        "Identity__DevelopmentHeaderAuthEnabled": "true",
        "Realtime__Provider": "disabled",
    })
    browser_env = os.environ.copy()
    browser_env["CI"] = "1"  # Playwright starts a fresh Vite server instead of reusing an unrelated one.

    api = None
    api_log = tempfile.TemporaryFile(mode="w+t")
    exit_code = 1
    try:
        seeded = run([
            "dotnet", "test", "tests/PitchMind.Infrastructure.Tests/PitchMind.Infrastructure.Tests.csproj",
            "--configuration", "Release", "--no-restore", "--nologo", "--verbosity", "minimal",
            "--maxcpucount:1", "--disable-build-servers", "-p:UseSharedCompilation=false",
            "--filter", "FullyQualifiedName~SeedLocalBrowserJourneyFixtureForApiE2E"
        ], env=test_env)
        if seeded.returncode:
            fail("The PostgreSQL migration/seed test failed; see its test output above.")

        api_build = run([
            "dotnet", "build", "src/PitchMind.Api/PitchMind.Api.csproj", "--configuration", "Release",
            "--no-restore", "--nologo", "--verbosity", "minimal", "--maxcpucount:1",
            "--disable-build-servers", "-p:UseSharedCompilation=false"
        ])
        if api_build.returncode:
            fail("The local API Release build failed; see its build output above.")

        api = subprocess.Popen([
            "dotnet", "run", "--project", "src/PitchMind.Api/PitchMind.Api.csproj", "--configuration", "Release",
            "--no-restore", "--no-build", "--no-launch-profile"
        ], cwd=ROOT, env=api_env, stdout=api_log, stderr=subprocess.STDOUT, text=True, start_new_session=True)
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            if api.poll() is not None:
                api_log.seek(0)
                sys.stderr.write(api_log.read())
                fail(f"Local API exited early with status {api.returncode}.")
            try:
                with urllib.request.urlopen(f"http://127.0.0.1:{API_PORT}/api/health", timeout=1) as response:
                    if response.status == 200:
                        break
            except (urllib.error.URLError, TimeoutError):
                time.sleep(1)
        else:
            fail("Local API did not become healthy within 90 seconds.")

        browser = run(["npm", "run", "test:e2e", "--", "--grep", "reads persisted PostgreSQL evidence"],
                      cwd=ROOT / "frontend", env=browser_env)
        exit_code = browser.returncode
        if exit_code == 0:
            print("PASS: isolated PostgreSQL -> local authenticated API -> browser evidence/citation journey")
    except RuntimeError as error:
        print(f"FAIL: {error}", file=sys.stderr)
    finally:
        if api is not None:
            stop_process_group(api)
            if not wait_for_api_port_release():
                print(f"FAIL: API process group left port {API_PORT} bound after cleanup.", file=sys.stderr)
                exit_code = 1
        api_log.close()
        dropped = pg(["dropdb", "-U", str(username), "--if-exists", "--force", database])
        if dropped.returncode:
            print(f"FAIL: isolated database cleanup failed for {database}.", file=sys.stderr)
            exit_code = 1
    return exit_code


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(130)
