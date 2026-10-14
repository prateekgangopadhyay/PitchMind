# EP03-S01 seeded fixture match identity — 2026-10-14

## Change

On code commit `27983ec17947c738a09b752fdc3564d0d316342f`, the test-only `SyntheticFixtureGenerator` derives `matchId` from the fixture version and seed. Repeated generation with one seed retains one match identity; distinct seeds receive distinct identities, and every generated event retains the fixture's match ID. Event IDs remain deterministic and seed-scoped.

The source candidate `data/simulations/manu-liv-match-dataset.json` was not consumed or transformed. Its clock, coordinate, metric, event-mapping and football semantics remain unverified as documented by the [candidate profile](../../data/MANU-LIV-SOURCE-PROFILE.md). No canonical admission or production match data change is claimed.

## Verification

Executed on the code commit above in the isolated local clone:

| Command | Result |
|---|---:|
| `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false --logger 'console;verbosity=minimal'` | 5 passed, 0 failed, 0 skipped |
| `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false --logger 'console;verbosity=minimal'` | 11 passed, 0 failed, 0 skipped |
| `dotnet test tests/PitchMind.Contracts.Tests/PitchMind.Contracts.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false --logger 'console;verbosity=minimal'` | 16 passed, 0 failed, 0 skipped |
| `git diff --check` | passed |

The first sandboxed VSTest attempts could not bind their local communication socket. The same commands were rerun with local execution permission and passed. NuGet audit was disabled. No PostgreSQL integration, hosted CI, Azure operation, independent football review, or Product Owner acceptance was run or is claimed.

## Status

This is a verified fixture-identity code slice, not acceptance of EP03-S01. Remaining work includes a football-reviewed 90-minute scenario with the story's full disciplinary/substitution and possession requirements, source semantics/quality review if the candidate feed is to be admitted, independent technical review and Product Owner acceptance. No Azure resources or deployments were accessed or changed.
