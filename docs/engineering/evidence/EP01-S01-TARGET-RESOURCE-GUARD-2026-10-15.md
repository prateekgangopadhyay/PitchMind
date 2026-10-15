# EP01-S01 target-time production resource guard — 2026-10-15

**Scope:** Close an independent-review finding in the production embedded-resource boundary. This is local implementation and verification evidence, not story acceptance.

**Code revision:** `610a7644240a55584f059a924af831e51c30ed72`, based on main `3df1d0aacaecc424181c95824fab73bcf99659e6`.

The architecture test previously captured target-created references at `FindReferenceAssembliesForReferences`, which was too early to observe every production resource item. The guard now separately captures `EmbeddedResource` items after MSBuild `PrepareResources`, records target-created items as `TargetEmbeddedResource`, and applies the existing fixture/scenario path and metadata policy to both evaluated and target-created resources. Its regression injects `tests/fictional-match.json` before `CreateManifestResourceNames` and asserts that the later resource-preparation capture rejects the item.

An independent static re-review of this exact change found no actionable findings. The reviewer did not run tests.

## Verification

- Focused late target-injection regression — 1 passed, 0 failed, 0 skipped.
- Full architecture test project — 25 passed, 0 failed, 0 skipped.
- `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` — 489 passed, 0 failed, 28 skipped. All skips are opt-in PostgreSQL integration tests because this run had no test connection configured.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, no dependency cycles, 2 approval gates.
- `git diff --check` — passed.
- NuGet audit was disabled and existing local restore assets were reused; no package audit or fresh-machine restore is claimed.
- No hosted CI, PostgreSQL integration, Azure operation, or deployment occurred.

EP01-S01 remains unaccepted pending human technical review and Product Owner acceptance. This change does not alter architecture boundaries, public APIs, or C4 diagrams. The fixture scanner remains a bounded heuristic and not proof against every runtime-generated or encoded literal.
