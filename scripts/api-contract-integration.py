#!/usr/bin/env python3
"""Local HTTP contract checks against fresh Release API processes.

Run after the Release solution build. The process binds to loopback only and uses
the local runtime profile; no Docker or Azure services are contacted.
"""
import os
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
API_DLL = ROOT / "src" / "PitchMind.Api" / "bin" / "Release" / "net10.0" / "PitchMind.Api.dll"
CONTRACT_SCRIPTS = ("api-smoke.py", "api-request-contract-smoke.py")


def reserve_loopback_port():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


def wait_until_ready(process, base_url, log_file):
    deadline = time.monotonic() + 40
    last_error = None
    while time.monotonic() < deadline:
        if process.poll() is not None:
            log_file.seek(0)
            raise RuntimeError(
                f"API exited with status {process.returncode}:\n{log_file.read().decode(errors='replace')}"
            )
        try:
            with urllib.request.urlopen(base_url + "/api/health", timeout=1) as response:
                if response.status == 200:
                    return
        except (OSError, urllib.error.URLError) as error:
            last_error = error
        time.sleep(0.25)

    process.terminate()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait()
    log_file.seek(0)
    raise RuntimeError(
        f"API did not become healthy ({last_error}):\n{log_file.read().decode(errors='replace')}"
    )


def run_api_contracts(scripts):
    port = reserve_loopback_port()
    base_url = f"http://127.0.0.1:{port}"
    environment = os.environ.copy()
    environment.update(
        {
            "ASPNETCORE_ENVIRONMENT": "Development",
            "ASPNETCORE_URLS": base_url,
            "Runtime__Profile": "local",
            "Identity__DevelopmentHeaderAuthEnabled": "true",
        }
    )

    with tempfile.TemporaryFile() as api_log:
        process = subprocess.Popen(
            [
                "dotnet",
                str(API_DLL),
            ],
            cwd=ROOT,
            env=environment,
            stdout=api_log,
            stderr=subprocess.STDOUT,
        )
        try:
            wait_until_ready(process, base_url, api_log)
            for script in scripts:
                subprocess.run(
                    [sys.executable, str(ROOT / "scripts" / script), base_url],
                    cwd=ROOT,
                    check=True,
                )
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


def main():
    run_api_contracts(CONTRACT_SCRIPTS)
    # The rate-limit contract must start with an unused fixed-window bucket.
    run_api_contracts(("api-limits-smoke.py",))


if __name__ == "__main__":
    main()
