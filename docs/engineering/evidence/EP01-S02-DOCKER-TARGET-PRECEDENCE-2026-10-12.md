# EP01-S02 Docker target precedence evidence — 2026-10-12

**Tested code head:** `ff5641c54205db20040983dbedec4be234da1c2b` (parent `9d67f73bdcd3902790aef7e412221275ad749fbb`).

An independent code review of PR #142 found the guard followed Docker context precedence, but the combination of `DOCKER_CONTEXT` and `DOCKER_HOST` was not covered by committed tests. The follow-up adds two fake-Docker regressions:

- A local Unix-socket `DOCKER_CONTEXT` overrides a remote TCP `DOCKER_HOST` and is the endpoint inspected/used.
- A remote SSH `DOCKER_CONTEXT` overrides a local Unix-socket `DOCKER_HOST` and fails before `docker info`, secret generation, or Compose.

## Executed checks

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -q` — 24 passed, 0 failed.
- `bash -n scripts/dev-up.sh` — passed.
- `docker compose --env-file docker/.env.example -f docker/compose.yml config --quiet` — passed; daemonless configuration only.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, 0 cycles, 2 approval gates).
- `git diff --check` — passed.
- Independent read-only Docker context inspection observed `desktop-linux` at `unix:///Users/prateekgangopadhyay/.docker/run/docker.sock`; no live daemon was invoked for these tests.

No live runtime behavior or fresh-machine acceptance is claimed. Hosted CI, human technical review and Product Owner acceptance remain pending. No Azure resources were changed.
