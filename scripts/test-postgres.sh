#!/usr/bin/env bash
set -euo pipefail
# Keep credentials out of shell tracing even if the caller invokes bash -x.
set +x
cd "$(dirname "$0")/.."
command -v docker >/dev/null || { echo "Docker CLI required" >&2; exit 1; }
command -v dotnet >/dev/null || { echo ".NET SDK required" >&2; exit 1; }
command -v python3 >/dev/null || { echo "Python 3 required" >&2; exit 1; }
[[ -f docker/.env ]] || { echo "Run ./scripts/dev-up.sh first" >&2; exit 1; }
python3 - <<'PY'
import json
import contextlib
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import uuid
import xml.etree.ElementTree as ET

compose = ['docker', 'compose', '--env-file', 'docker/.env', '-f', 'docker/compose.yml']
# Capture the resolved Compose config in memory; never print it or its credentials.
config = subprocess.run(compose + ['config', '--format', 'json'], capture_output=True, text=True)
if config.returncode:
    sys.exit('FAIL: could not resolve local Compose configuration')
try:
    postgres = json.loads(config.stdout)['services']['postgres']
    environment = postgres['environment']
    port = next(p for p in postgres['ports'] if int(p['target']) == 5432)
    if port.get('host_ip') != '127.0.0.1':
        sys.exit('FAIL: PostgreSQL tests require a loopback-bound Compose service')
    password = environment['POSTGRES_PASSWORD']
    username = environment['POSTGRES_USER']
    published = int(port['published'])
except (KeyError, ValueError, StopIteration, TypeError):
    sys.exit('FAIL: local PostgreSQL Compose configuration is incomplete')

def quoted(value):
    return '"' + str(value).replace('"', '""') + '"'

def postgres_command(arguments):
    # Password stays inside the running container environment; no command-line password.
    return subprocess.run(compose + ['exec', '-T', 'postgres'] + arguments,
                          capture_output=True, text=True)

project = 'tests/PitchMind.Infrastructure.Tests/PitchMind.Infrastructure.Tests.csproj'
command = ['dotnet', 'test', project, '--nologo', '-m:1', '-p:UseSharedCompilation=false']
listing = subprocess.run(command + ['--list-tests'], capture_output=True, text=True)
if listing.returncode:
    sys.exit('FAIL: could not discover PostgreSQL integration tests')
tests = [line.strip() for line in listing.stdout.splitlines()
         if line.strip().startswith('PitchMind.Infrastructure.Tests.')]
if not tests:
    sys.exit('FAIL: no PostgreSQL integration tests were discovered')

retained_results = os.environ.get('PITCHMIND_TEST_RESULTS_DIRECTORY')
if retained_results:
    Path(retained_results).mkdir(parents=True, exist_ok=True)
result_context = contextlib.nullcontext(retained_results) if retained_results else tempfile.TemporaryDirectory(prefix='pitchmind-postgres-tests-')
exit_code = 0
with result_context as results:
    for index, test_name in enumerate(tests, start=1):
        database = 'pitchmind_test_' + uuid.uuid4().hex
        created = postgres_command(['createdb', '-U', username, database])
        if created.returncode:
            print('FAIL: cannot create isolated local test database; ensure Compose PostgreSQL is healthy', file=sys.stderr)
            exit_code = 1
            continue
        try:
            test_environment = os.environ.copy()
            test_environment['PITCHMIND_TEST_POSTGRES'] = (
                'Host=127.0.0.1;Port=' + str(published) + ';Username=' + quoted(username) +
                ';Password=' + quoted(password) + ';Database=' + database + ';Include Error Detail=false')
            report_name = 'postgres-' + str(index) + '.trx'
            test_command = command + ['--no-restore', '--no-build', '--filter', 'FullyQualifiedName=' + test_name,
                                      '--logger', 'trx;LogFileName=' + report_name, '--results-directory', results]
            print('=== ' + test_name + ' (isolated disposable database) ===', flush=True)
            report = Path(results) / report_name
            report.unlink(missing_ok=True)
            result = subprocess.run(test_command, env=test_environment)
            if result.returncode or not report.exists():
                print('FAIL: PostgreSQL test failed or result evidence is missing: ' + test_name, file=sys.stderr)
                exit_code = 1
            else:
                ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
                counters = ET.parse(report).find('t:ResultSummary/t:Counters', ns)
                if counters is None or int(counters.get('executed', '0')) != 1 or int(counters.get('notExecuted', '0')) != 0:
                    print('FAIL: PostgreSQL test must execute exactly once without skips: ' + test_name, file=sys.stderr)
                    exit_code = 1
        except Exception:
            print('FAIL: unexpected PostgreSQL test runner error: ' + test_name, file=sys.stderr)
            exit_code = 1
        finally:
            cleaned = postgres_command(['dropdb', '-U', username, '--if-exists', '--force', database])
            if cleaned.returncode:
                print('FAIL: isolated test database cleanup failed; database=' + database, file=sys.stderr)
                exit_code = 1
if exit_code == 0:
    print('PASS: all ' + str(len(tests)) + ' real PostgreSQL integration tests executed without skips in isolated disposable databases')
sys.exit(exit_code)
PY
