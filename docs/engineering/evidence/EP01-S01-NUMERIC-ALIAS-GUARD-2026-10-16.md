# EP01-S01 production numeric alias guard — 2026-10-16

## Scope and status

This slice strengthens the architecture test that detects candidate football data embedded in production C# or React source. The guard now follows bounded direct numeric declarations and simple alias chains, and treats shadowed or mutated aliases as suspicious when they are assigned to football numeric properties. A negative regression covers TypeScript aliases and alias chains, both candidate/noncandidate shadowing orders, simple typed TypeScript numeric declarations, reassignment, a C# alias, and unrelated layout assignments. Independent review of the first draft found and helped close the reverse-shadowing-order gap. A read-only re-review of the corrected code and documentation found no new actionable issue; the reviewer did not run tests. This does not change runtime architecture, public contracts, C4 boundaries, or story acceptance. Product Owner acceptance remains pending; EP01-S01 remains unaccepted.

The source tree is based on exact `main` revision `4928aae433ae36f9b9b178b8beea1c25938ee306`. Candidate values are read from `data/simulations/manu-liv-match-dataset.json`; this guard does not admit that feed as canonical data.

## Verification performed

- `dotnet restore PitchMind.slnx --locked-mode -p:NuGetAudit=false` — passed.
- Focused alias regression `ProductionFixtureGuard_FailsClosedWhenCandidateNumericAliasesAreShadowedOrMutated` — 1 passed, 0 failed, 0 skipped.
- Full Release architecture suite — 26 passed, 0 failed, 0 skipped.
- Clean Release solution build — passed with 0 warnings and 0 errors.
- Full Release solution suite — 512 passed, 0 failed, 29 skipped. The 29 skips are opt-in PostgreSQL tests that were not configured for this run.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, 33 discoverable skills, zero dependency cycles, and 2 approval gates.
- `git diff --check` — passed.

The initial sandboxed VSTest attempt could not bind its loopback communication socket before running tests; the same test command completed with local loopback permission. NuGet audit was disabled for this local run. PostgreSQL integration, hosted CI, browser accessibility, Azure operations, and deployment were not run. No Azure resources or deployments were changed.

## Limits and remaining gates

The alias scan is a bounded lexical heuristic, not a complete C# or TypeScript parser. It covers simple numeric declarations and identifier chains and fails closed for detected shadowing/mutation cases; it does not prove the absence of every runtime-generated, encoded, indirect, or syntactically unusual literal. Independent exact-diff review and hosted checks remain pending. Human technical review and Product Owner acceptance remain separate gates. EP01-S01 remains unaccepted.
