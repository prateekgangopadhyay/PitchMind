# Engineering Council skills and story routing — static verification record
**Branch:** `docs/engineering-council-skills-operating-model` | **Scope checked:** source files at GitHub commit `11dd3f2831dca41b8e2a1909fb6582107605a312` (2026-10-08). **Method:** GitHub REST metadata/content checks and JavaScript syntax parse; this is **not** a CI run or a deployment.
## Actually checked
- Existing draft PR: [#7](https://github.com/prateekgangopadhyay/PitchMind/pull/7); commit tree is on the requested branch, not main.
- 28 unique skill directories with `SKILL.md` and corresponding registry entries. Each now has a role-specific proof/handoff gate.
- 59 source backlog story headings map one-to-one to machine-readable story routes and workpacket file paths; distinct IDs = 59. Priority counts: 49 P0, 7 P1, 3 P2.
- Story dependency graph traversed without unknown predecessor IDs or cycles.
- C4 L1/L2/L3 and deployment diagrams explicitly distinguish OBSERVED CURRENT from TARGET / optional. Existing offline DEV demo **is not** asserted to use managed PostgreSQL/Foundry/SignalR.
- Pipeline definition has `enableDevDelivery=false` by default and two `ManualValidation@0` gates before ACR image build/push and DEV deployment. **Manual resume is not identity proof**; owner approval for exact story/change is still mandatory, plus protected Environment checks.
- Source file is present at `data/simulations/manu-liv-match-dataset.json` (28,601,013 bytes per Git tree). Source semantic and distribution approval are open. Source data counts and warnings are from [earlier observed profile](../../docs/data/MANU-LIV-SOURCE-PROFILE.md).
- JS files `scripts/validate_operating_model.mjs` and `scripts/audit_simulation_feed.mjs` were syntax parsed after stripping the Unix shebang and top-level imports for a parser-only check. **Not executed on actual repository runtime** in this verification.
## Not executed or not yet accepted
- Node validator and 28MB JSON audit in clean Azure DevOps build; dotnet build/tests, npm UI tests, Playwright/axe, Docker + real PostgreSQL, Bicep validation, live Azure service integration, penetration or 500-eps load tests.
- User rights confirmation, synthetic content statement, pitch units/orientation, match realism / source coverage and approved `fixture-manifest.json`.
- Source-specific C# normalizer, canonical PG outbox, Event Hubs/SignalR, Entra login, Microsoft Foundry, final seven-screen website, and release acceptance of any story.
- Actual Azure DevOps manual approval identity binding/Environment branch policy enforcement—these require platform configuration and observed pipeline execution.
- **NO Azure DEV resource or deployment action was performed.**
## Next verification commands in a clean checkout
```bash
node scripts/validate_operating_model.mjs --self-test
node scripts/validate_operating_model.mjs
node scripts/audit_simulation_feed.mjs --self-test
node scripts/audit_simulation_feed.mjs
python3 -m unittest discover -s scripts -p 'test_profile_simulation_feed.py' -v
./scripts/validate-all.sh
```
After rights/units approval, run source intake strict mode, canonical storage/replay golden, and full local integration. An Azure DEV candidate may be considered only after a separate explicit owner approval referencing the exact change, image digest, what-if, cost and rollback.
