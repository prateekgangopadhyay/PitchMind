# EP01-S05 local security-gate evidence — 2026-10-12

**Story:** EP01-S05 — Quality, security and developer readiness gate  
**Environment:** local macOS workstation; Docker scanner containers only  
**Parent:** draft PR #17, exact base `9862c49eb5654a8f704b23f0264b0927e086dfc5`  
**Code revision:** `e443c6e9c50039081e03ec832ffcb81d1d3a7732`  
**Status:** implementation and local checks observed; story acceptance remains pending.

## Observed checks

- `bash scripts/validate-all.sh` exited 0.
- .NET Release build succeeded with 0 warnings and 0 errors.
- .NET tests: 23 passed, 0 failed, 5 PostgreSQL integration tests skipped because the aggregate local command had no configured test database.
- NuGet vulnerability audit: no advisories across 12 projects.
- Frontend: production build and lint passed; 3 Vitest tests passed; npm audit found 0 vulnerabilities.
- Python checks: feed profiler 5/5, NuGet report parser 7/7, secret-scanner wrapper 2/2, and SBOM generator test 1/1.
- Pinned Gitleaks v8.29.1 scan of the repository completed over approximately 34.47 MB and reported no findings.
- Pinned Syft v1.54.1 generated a source SPDX 2.3 document. The unit test parsed the generated JSON and checked its SPDX fields; aggregate validation wrote its artifact to a temporary directory and removed it at exit.
- Azure pipeline YAML parsed; shell syntax and `git diff --check` passed.
- Council validator passed for 12 epics, 59 stories, 28 Council skills, zero dependency cycles, and two approval gates. DAG and design-skill self-tests passed.

## Scope limits and pending gates

The secret scan covers the checked-out source tree, not all Git history. The SBOM covers source files, not the immutable release image. No Azure DevOps hosted run, branch-policy binding, release-image SBOM, full clean-runner execution, independent human review, or Product Owner acceptance was observed. The five PostgreSQL integration tests remain skipped in the aggregate command. This change did not deploy to Azure or mutate Azure resources; local application containers and persistent volumes were not started, stopped, or changed by these checks.
