# EP04-S02 LOCAL API password guard — 2026-10-16

**Code commit:** `57a6406e` on the EP04-S02 child branch stacked on PR #299.

## Change

The local bootstrap already requires 48 lowercase hex characters with at least 12 distinct hex digits. The explicit migration launcher and `LocalApiRoleProvisioner` now enforce the same diversity rule independently. A low-diversity, otherwise well-formed API password is rejected before opening a PostgreSQL connection.

## Executed evidence

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_dev_bootstrap scripts.test_dev_migrate -v` — **38 passed**.
- Focused Infrastructure Release regression, `LocalApiRoleProvisionerRejectsLowDiversityPasswordBeforeDatabaseConnection` — **1 passed, 0 failed, 0 skipped**.
- `./scripts/test-postgres.sh` — Infrastructure **90/90** and Contracts **154/154**, no skips. It used a uniquely named disposable local database, removed it after the run, and removed the temporary `docker/.env` symlink. The shared `pitchmind` database was not the test target.
- `bash -n scripts/dev-up.sh`, Council validator, and `git diff --check` — passed.

## Limits

This is LOCAL credential validation and PostgreSQL role-grant evidence only. No Azure resources, cloud identities, deployment, or Product Owner acceptance were involved. EP04-S02 remains unaccepted pending independent exact-head review, broader provenance/backup-restore evidence, and PO acceptance.
