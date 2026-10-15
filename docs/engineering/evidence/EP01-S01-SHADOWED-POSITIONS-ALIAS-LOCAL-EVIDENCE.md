# EP01-S01 shadowed player-position alias — local evidence

**Scope:** Fix the numeric-literal architecture guard to resolve the nearest visible `playerPositions` array declaration when an inner `positions` binding shadows a file-scope array. This is a targeted guard increment; it is not complete language parsing or EP01-S01 acceptance.

**Base:** PR #136 updated feature head `154260b9a4e15ef45df8870eef1fd0e437d86fe9`.
**Working branch:** `codex/ep01-s01-shadowed-positions-alias`.

## Regression and behavior

- The review regression uses a file-scope `positions` array with unrelated explicit geometry, then a function-local `positions` array whose `{ x }` shorthand consumes a candidate-feed coordinate.
- Before the resolver change, the focused test failed because the guard reported no violations.
- The resolver now filters declarations to those preceding the `playerPositions` assignment and visible in its scope, then selects the deepest visible scope and latest declaration within that scope.
- After the change, the focused regression passed and the surrounding architecture suite remained green.

## Verification

| Command | Result |
|---|---|
| `dotnet test tests/PitchMind.Architecture.Tests/PitchMind.Architecture.Tests.csproj -c Release --filter FullyQualifiedName~ShadowedPlayerPositionsArrayAlias --no-restore --disable-build-servers -m:1` | 1 passed, 0 failed, 0 skipped. |
| `dotnet test tests/PitchMind.Architecture.Tests/PitchMind.Architecture.Tests.csproj -c Release --no-restore --disable-build-servers -m:1` | 37 passed, 0 failed, 0 skipped. |
| `dotnet test PitchMind.slnx -c Release --no-restore --disable-build-servers -m:1` | 55 passed, 0 failed, 5 skipped. All skips are opt-in PostgreSQL tests without a configured connection. Exit code 0. |

VSTest used local permission for its loopback socket. The full Release suite built all included projects. The failing-before/passing-after focused regression was run on this branch before documentation updates.

## Limits and pending evidence

- This remains bounded lexical analysis; it does not implement full C#/TypeScript binding, reassignment, destructuring, or arbitrary data-flow semantics.
- Candidate feed units and player-metric semantics remain unverified; this guard does not admit the feed as canonical.
- Independent review of the updated PR head, hosted CI, and Product Owner acceptance remain pending. EP01-S01 remains `unverified`.
- No PostgreSQL integration, Azure resource, or deployment action was performed.
