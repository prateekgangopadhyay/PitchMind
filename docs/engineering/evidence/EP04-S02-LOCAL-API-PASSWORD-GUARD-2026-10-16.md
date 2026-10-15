# EP04-S02 LOCAL API password guard — 2026-10-16

**Code commits:** `57a6406e` and `5fa9ab55` on the EP04-S02 child branch stacked on PR #299.

## Change

The local bootstrap already requires 48 lowercase hex characters with at least 12 distinct hex digits. The explicit migration launcher and `LocalApiRoleProvisioner` now enforce the same diversity rule independently. A low-diversity, otherwise well-formed API password is rejected before opening a PostgreSQL connection.

Re-provisioning also resets role attributes to `NOSUPERUSER`, `NOCREATEDB`, `NOCREATEROLE`, `NOINHERIT`, `NOREPLICATION`, and `NOBYPASSRLS`; revokes all role memberships; reassigns any role-owned objects in the migrated database to the migration identity; and revokes direct database, schema, table, sequence, and routine grants across non-system schemas before restoring the narrowly required runtime grants. This prevents an existing role from retaining privileges from an earlier or manually altered configuration.

## Executed evidence

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_dev_bootstrap scripts.test_dev_migrate -v` — **38 passed**.
- Focused Infrastructure Release regression, `LocalApiRoleProvisionerRejectsLowDiversityPasswordBeforeDatabaseConnection` — **1 passed, 0 failed, 0 skipped**.
- `./scripts/test-postgres.sh` — Infrastructure **90/90** and Contracts **154/154**, no skips. Its regression deliberately elevates the API role, grants role membership and database/schema/table/sequence access, and transfers ownership of a probe table to that role; re-provisioning removes those privileges and restores the allowed operations. It used a uniquely named disposable local database, removed it after the run, and removed the temporary `docker/.env` symlink. The shared `pitchmind` database was not the test target.
- `bash -n scripts/dev-up.sh`, Council validator, and `git diff --check` — passed.

## Limits

This is LOCAL credential validation and PostgreSQL role-grant/reset evidence only. No Azure resources, cloud identities, deployment, or Product Owner acceptance were involved. EP04-S02 remains unaccepted pending independent exact-head review, broader provenance/backup-restore evidence, and PO acceptance.
