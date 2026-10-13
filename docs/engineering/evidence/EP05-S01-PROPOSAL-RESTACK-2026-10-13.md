# EP05-S01 proposal restack verification — 2026-10-13

## Scope and stack

This restack contains only the EP05-S01 concept proposal, strict Domain proposal loader and tests, story packet note, and execution-register update. It is based on PR #29's current head `69cb2be0e5259ad186588308513eb1ec6373e216`, which carries the routed EP03-S01 and EP04 implementation ancestry. The proposal is deliberately marked `proposed`; approval references remain null and source semantics remain unapproved.

Implementation was cherry-picked from the existing EP05-S01 proposal commit. At code head `67d1c2f`, the clean restack changes six files. It adds no runtime consumer, detector thresholds, source-event mapping, HTTP contract, or candidate-feed import.

## Observed verification

- `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false` — **35 passed, 0 failed, 0 skipped** at code head `67d1c2f`.
- The proposal digest test verified the candidate-feed SHA-256 against both the proposal and candidate manifest. This verifies file identity only; it does not validate feed semantics or football realism.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, zero dependency cycles, two approval gates).
- `git diff --check HEAD^..HEAD` — passed on the implementation commit.
- No full-solution or PostgreSQL test run was performed for this Domain-only proposal slice.

## Review and acceptance limits

The proposal defines terms and minimum/insufficient evidence for press, trap, response, threat, overload, line-break, counterpress and rest-defence. All thresholds remain unapproved. The candidate source's coordinate units/orientation, match-time semantics, player metrics and canonical mappings remain unvalidated; no proposal field grants approval. Football Analyst and Product Owner review, approved football fixtures, exact-head independent GitHub review and Product Owner acceptance remain pending. This evidence does not accept EP05-S01.

No Azure resources or deployments were touched.


## Current-parent restack verification — 2026-10-13

The exact current PR #29 head checked on GitHub is `83e3543c7ad648bfeb20ab88e1a8d520f9cfa395`. The isolated local clone did not contain that commit object, so I fetched the local equivalent parent commit `6fd56d2c43ff8bf36dbdf2b893a47f6288a70030`; its tree exactly matches the GitHub head tree `5196e60c73d963ab456885dbf74dca74ac915b9d`. Merging it with the existing EP05-S01 head produced local restack commit `edeeae664638d255ee3b48b9133e2d8f22b192b7` with tree `3c8bc71dff837d34acbaead8f1b5a18a42acad9d`.

- Full Release solution command: `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false` — **100 passed, 0 failed, 21 PostgreSQL opt-in tests skipped**. The first sandboxed invocation aborted before tests because VSTest could not bind its loopback socket; the same command was rerun with local loopback permission and completed successfully. NuGet audit was disabled.
- Local PostgreSQL harness: `COMPOSE_PROJECT_NAME=pitchmind NuGetAudit=false ./scripts/test-postgres.sh` — **21 passed, 0 failed, 0 skipped** against the existing healthy PostgreSQL service mounted on `pitchmind_postgres_data`. The harness created and dropped its unique test database; it did not recreate the service or volume.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, zero dependency cycles, two approval gates). `git diff --check` — passed after the restack and before this evidence-only documentation update.

The merge adds no EP05 runtime behavior beyond the existing proposal. The proposal digest is checked against both the candidate manifest and exact feed bytes. That establishes source-file identity only, not coordinate units/orientation, clock semantics, player metric validity, canonical mapping, or football realism. No C4/API/ADR boundary changed. EP05-S01 remains unaccepted pending football review, exact-head independent review, approved fixtures and explicit Product Owner acceptance. No Azure resources or deployments were touched.
