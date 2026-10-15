# EP03-S01 local fixture and source-profile re-verification — 2026-10-16

## Exact source and data

- Git source commit: `bed004848c240d0d6b189f350d6905f5d74dc228` (main-based local checkout).
- Candidate file: `data/simulations/manu-liv-match-dataset.json`.
- Candidate SHA-256: `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`.
- Candidate bytes are profiled only; they are not used by the fictional seeded fixture or canonicalized by these checks.

## Executed checks

- `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj -c Release --filter FullyQualifiedName~SyntheticFixtureTests -m:1 --disable-build-servers -p:UseSharedCompilation=false -p:NuGetAudit=false --no-restore` — **14 passed, 0 failed, 0 skipped**.
- `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --filter FullyQualifiedName~SyntheticMatchTests -m:1 --disable-build-servers -p:UseSharedCompilation=false -p:NuGetAudit=false --no-restore` — **50 passed, 0 failed, 0 skipped**.
- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_profile_manu_liv_observed_dictionary scripts.test_generate_manu_liv_field_dictionary scripts.test_validate_manu_liv_candidate -v` — **28 passed**.
- `python3 scripts/profile_manu_liv_observed_dictionary.py --output /private/tmp/manu-liv-observed-bed0048.md` followed by `cmp docs/data/MANU-LIV-OBSERVED-DATA-DICTIONARY.md /private/tmp/manu-liv-observed-bed0048.md` — generated output matched the checked-in dictionary byte-for-byte. The report was written outside the repository.
- `sha256sum data/simulations/manu-liv-match-dataset.json` — matched the candidate hash above.
- `node scripts/validate_operating_model.mjs` — passed for 12 epics, 59 stories, 28 Council skills, 33 discoverable skills, zero dependency cycles, and two approval gates.
- `git diff --check` — passed after the documentation changes.

The fixture suite covers deterministic seeded artifact generation and a complete 5,400-second fictional match. Application checks include selected-time score transitions without future leakage and seeded scenario behavior. These tests verify the local code assertions; they do not constitute a human football review.

## Limits and disposition

No PostgreSQL admission/replay, Event Hubs, hosted CI, browser/E2E, Azure, or deployment check ran in this increment. The candidate remains `candidate_not_canonical`: provider semantics for clock/period, coordinate units/axes/orientation, player metrics, identity, event mappings, football realism, and independent rights/privacy review remain unresolved. No canonical facts were created from the candidate. Product Owner acceptance and independent human football-domain review remain pending; EP03-S01 remains unaccepted.
