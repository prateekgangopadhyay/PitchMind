# EP01-S02 local Docker target guard evidence

**Base:** `c836011cf0c846dd6eddfa1aa1ebb47afca3d44b` (current `main` at implementation start)  
**Verification:** 2026-10-12, local isolated clone  
**Code revision:** published as the head of the linked pull request

## Change

The shared `scripts/lib/local-docker-target.sh` resolves Docker CLI target precedence (`DOCKER_CONTEXT`, then `DOCKER_HOST`, then the active context), fails closed when context inspection fails, and permits only local `unix:///` endpoints. `dev-up.sh`, `dev-down.sh`, `dev-reset.sh`, and `dev-health.sh` call the shared guard before Docker service operations. Startup validates the target before creating or changing local credentials.

## Evidence

- `python3 -m unittest scripts.test_dev_bootstrap -v`: **23 passed, 0 failed**. Coverage includes local Unix targets for stop/reset/health; SSH context and TCP host rejection before service calls; and context-over-host precedence.
- `python3 -m unittest discover -s scripts -p 'test_*.py' -v`: **78 passed, 0 failed**.
- `bash -n scripts/dev-up.sh scripts/dev-down.sh scripts/dev-reset.sh scripts/dev-health.sh scripts/lib/local-docker-target.sh`: passed.
- `docker compose --env-file <temporary synthetic credentials> -f docker/compose.yml config --quiet`: passed. The temporary file was removed; no credentials were committed.
- `node scripts/validate_operating_model.mjs`: passed (12 epics, 59 stories, 28 Council roles, 0 dependency cycles, 2 approval gates).
- `git diff --check`: passed.
- Independent read-only code review: no correctness blocker. Minor UX observation: reset requests confirmation before rejecting a remote Docker target.

## Limits

No Docker daemon, containers, data volumes, or Azure resources were changed. These checks do not verify live service startup, local state reuse, fresh-machine onboarding, hosted CI, or Product Owner acceptance. EP01-S02 remains unaccepted.
