# EP01-S02 Compose identity verification — 2026-10-12

## Scope

The exact main base was `373faf5bec7604487c297cd15bfd029f24b7d014`. It declared Compose project `pitchmind-local` and default project-scoped service volumes. Prior local runtime evidence recorded healthy `pitchmind-postgres-1` and `pitchmind-redis-1` containers using named volumes `pitchmind_postgres_data` and `pitchmind_redis_data`. PR #178 had merged this configuration fix into the EP01-S02 feature stack, but its changes were absent from the exact main base.

The follow-up changes only the local Compose project identity, explicit volume names, onboarding text, a daemonless resolved-config regression, and this evidence. Compose resolves to project `pitchmind`, PostgreSQL volume `pitchmind_postgres_data`, and Redis volume `pitchmind_redis_data`. Host bindings remain `127.0.0.1`.

The explicit volume names are global across Compose projects. An isolated runtime check must override both names; setting only a different project name still targets the PitchMind data volumes.

## Observed verification

Code commit: `f11382d90570f22c744531f767ccd039748ffc03` (based on `373faf5bec7604487c297cd15bfd029f24b7d014`).

- `python3 -m unittest scripts.test_dev_bootstrap.DevBootstrapTests.test_compose_reuses_stable_local_project_and_volume_identity -v` — passed 1/1.
- `python3 -m unittest discover -s scripts -p 'test_*.py' -v` — passed 73/73.
- `bash -n scripts/dev-up.sh scripts/dev-down.sh scripts/dev-reset.sh` — passed.
- `docker compose --env-file docker/.env.example -f docker/compose.yml config --format json` plus assertions over the rendered project, both volume names, and PostgreSQL/Redis loopback bindings — passed.
- `node scripts/validate_operating_model.mjs --self-test` — passed.
- `git diff --check` — passed.

## Limits

No `docker compose up`, `down`, `ps`, or other daemon operation was run. No local container, network, credential, or volume was inspected or mutated as part of this verification. Rendered identity matching is not proof that Compose runtime will reuse the existing containers or that a clean machine can bootstrap successfully. Those runtime/clean-machine checks remain unverified; independent human review and Product Owner acceptance are pending. No Azure resources or deployment were touched. EP01-S02 remains unaccepted.
