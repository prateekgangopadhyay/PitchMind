# EP04-S02 LOCAL API password guard — 2026-10-16

**Locally verified code commits:** `57a6406e`, `5fa9ab55`, and `be2bc499`. The corresponding changes are published in draft PR #301, stacked on PR #299.

## Change

The local bootstrap already requires 48 lowercase hex characters with at least 12 distinct hex digits. The explicit migration launcher and `LocalApiRoleProvisioner` now enforce the same diversity rule independently. A low-diversity, otherwise well-formed API password and a 48-character hexadecimal password followed by a final newline are rejected before opening a PostgreSQL connection. The regex uses absolute start/end anchors so .NET cannot accept a final line terminator as an end-of-input match.

Re-provisioning also resets role attributes to `NOSUPERUSER`, `NOCREATEDB`, `NOCREATEROLE`, `NOINHERIT`, `NOREPLICATION`, and `NOBYPASSRLS`; revokes all role memberships; reassigns any role-owned objects in the migrated database to the migration identity; and revokes direct database, schema, table, sequence, and routine grants across non-system schemas before restoring the narrowly required runtime grants. This prevents an existing role from retaining privileges from an earlier or manually altered configuration.

## Executed evidence

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_dev_bootstrap scripts.test_dev_migrate -v` — **38 passed**.
- Focused Infrastructure Release regression, `LocalApiRoleProvisionerRejectsLowDiversityPasswordBeforeDatabaseConnection` — **1 passed, 0 failed, 0 skipped**; it checks both low diversity and a trailing newline.
- `./scripts/test-postgres.sh` — Infrastructure **90/90** and Contracts **154/154**, no skips. Its regression deliberately elevates the API role, grants role membership and database/schema/table/sequence access, and transfers ownership of a probe table to that role; re-provisioning removes those privileges and restores the allowed operations. It used a uniquely named disposable local database, removed it after the run, and removed the temporary `docker/.env` symlink. The shared `pitchmind` database was not the test target.
- `bash -n scripts/dev-up.sh`, Council validator, and `git diff --check` — passed.

## Independent static review

The exact-head review found that .NET regex `$` could match before a final newline. The guard was changed to `\A...\z`, and the focused regression now covers the newline case. The corrected GitHub head `f4938db45c343dbf3f26189be561e4f2d4857ed2` was statically re-reviewed: the newline issue is closed and no actionable P0–P2 findings remain in scope. The reviewer did not run tests or submit a GitHub review.

## Limits

This is LOCAL credential validation and PostgreSQL role-grant/reset evidence only. No Azure resources, cloud identities, deployment, or Product Owner acceptance were involved. EP04-S02 remains unaccepted pending formal GitHub review, broader provenance/backup-restore evidence, and PO acceptance.

## Existing PostgreSQL large-object ACL follow-up — 2026-10-16

Independent review of the combined EP04-S02 parent head `267536f45df86a2a783b63c37b879f5decbe8055` found that direct `SELECT` and `UPDATE` grants on existing large objects were not cleared by the API-role reset. The follow-up code commit `c7eb7c640aaa08d9a14a9eb7b9dd4bc25be4facd` enumerates large-object catalog ACLs granted directly to `pitchmind_api` and revokes them by OID. The integration regression creates a large object owned by the migration identity, grants both privileges to the API role, reprovisions the role, and confirms the direct ACL entries are absent. Test setup now encloses cleanup so a failed assertion removes the temporary role and object.

- `./scripts/test-postgres.sh` on the follow-up code tree — Infrastructure **90/90** and Contracts **154/154**, 0 failed, 0 skipped. The harness used a uniquely named database on the healthy LOCAL container backed by `pitchmind_postgres_data`, then dropped the database. The temporary `.env` symlink used by the harness was removed.
- Release Infrastructure test project on the follow-up code tree — **53 passed, 37 skipped, 0 failed**. The 37 are opt-in database tests, all exercised by the no-skip harness run above.
- `git diff --check` — passed.
- Independent re-review of the follow-up exact head and hosted CI remain pending; no GitHub review or hosted check is claimed. The story remains unaccepted.
