# EP01-S02 current-main verification — 2026-10-14

## Scope and revision

This local verification covers the already-integrated EP01-S02 local workstation bootstrap on `main` at `2f3ca7e7e889d08108ccb932b728766b11a03ac2`. It is not a test of PR #44's older feature-branch head. Current `main` includes the credential checks, local Docker endpoint guard, stable Compose identity, loopback-only ports, and PostgreSQL volume/image-major preflight, with the expanded test suite.

Accountable role: Developer Experience Engineer. QA Architect consulted for test evidence and acceptance limits. This report records implementation verification only; it does not accept the story.

## Evidence observed

- `python3 -m unittest scripts.test_dev_bootstrap -v` — 32 passed, 0 failed.
- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v` — 110 passed, 0 failed. This run used local Docker Desktop access for the pinned, network-isolated Syft and Gitleaks test containers. The same command without Docker socket access failed 5 scanner/SBOM tests at Docker credential-helper startup; the successful rerun passed all tests.
- `bash -n scripts/dev-up.sh scripts/dev-down.sh scripts/dev-reset.sh scripts/dev-health.sh` — passed.
- `docker compose --env-file docker/.env.example -f docker/compose.yml config --quiet` — passed. Configuration-only; it did not start services.
- `node scripts/validate_operating_model.mjs --self-test` — passed.
- `git diff --check` — passed at the tested revision before this evidence-only change.

A focused Contracts test restore using the local NuGet cache did not complete in its first 30-second command window. A second attempt pinned the source to `/Users/prateekgangopadhyay/.nuget/packages`, used `--locked-mode --ignore-failed-sources`, and remained at “Determining projects to restore…” for more than three minutes; it was cancelled and exited 1. Neither attempt produced `project.assets.json`. No .NET build or test result is claimed for this revision.

## Limits and acceptance

This run did not start, stop, or reset Docker services or volumes; the preceding test-runner containers were isolated scanners. It did not perform a clean-machine bootstrap, hosted Azure DevOps run, PostgreSQL integration, or Product Owner review. Earlier same-host clean-source startup evidence remains linked from the story packet and is not upgraded by this test run. The old PR #44 feature head is not being merged as-is: its bootstrap behavior has been superseded by the more recent implementation already on `main`.

EP01-S02 remains unaccepted pending independent technical review, any remaining fresh-machine/hosted evidence required by the original criteria, and Product Owner pass/fail. No Azure resources or deployments were changed.
