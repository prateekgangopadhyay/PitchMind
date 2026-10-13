# EP01-S05 local security-gate evidence — 2026-10-12

**Story:** EP01-S05 — Quality, security and developer readiness gate
**Environment:** local macOS workstation; Docker scanner containers only
**Parent:** draft PR #17, exact base `48b1e95a5c5e5f4c129147ea9d136f679acf3d83`
**Restack:** PR #109 head `5b573764e7f47b99bc774889a98e1291a5e6ba26` was based on parent revision `9862c49eb5654a8f704b23f0264b0927e086dfc5`; the child commits were replayed onto the current PR #17 head after GitHub reported the stack nonmergeable.
**Code revision tested:** `0d26bb77f317075a9120c40ad514c36a8a3e05e6`
**Status:** implementation and local checks observed; story acceptance remains pending.

## Observed checks

- `bash scripts/validate-all.sh` exited 0 on the restacked tree.
- .NET Release build succeeded with 0 warnings and 0 errors.
- .NET tests: 23 passed, 0 failed, 5 PostgreSQL integration tests skipped because the aggregate local command had no configured test database.
- NuGet vulnerability audit: no advisories across 12 projects.
- Frontend: production build and lint passed; 3 Vitest tests passed; npm audit with the minimum `low` threshold found 0 vulnerabilities.
- Python checks: feed profiler 5/5, NuGet report parser 7/7, secret-scanner wrapper 4/4, and SBOM generator path/identity tests 3/3.
- Pinned Gitleaks v8.29.1 scan of the repository completed over approximately 34.55 MB and reported no findings. Production-wrapper regressions proved that an untrusted repository `.gitleaks.toml` cannot disable built-in rules and an inline `gitleaks:allow` cannot suppress the synthetic token canary; canary output remained redacted.
- Pinned Syft v1.54.1 generated a source SPDX 2.3 document. The regression parses the JSON, rejects an empty package list, and checks for the expected `pitchmind-source` package identity and supplied fixture version; aggregate validation writes its artifact to a temporary directory and removes it at exit.
- Azure pipeline YAML parsed; shell syntax and `git diff --check` passed.
- Council validator passed for 12 epics, 59 stories, 28 Council skills, zero dependency cycles, and two approval gates. DAG and five design-skill self-tests passed.
- Docker scanner execution required local Docker access; the scanner containers ran with network disabled. No application containers, Azure CLI, Azure resource mutations, or Azure deployment were used.
- The npm gate intentionally fails at every published advisory severity (low through critical); this repository has no severity exception policy.

## Scope limits and pending gates

The secret scan covers the checked-out source tree, not all Git history. The SBOM covers source files, not the immutable release image. No Azure DevOps hosted run, branch-policy binding, release-image SBOM, full clean-runner execution, independent human review, or Product Owner acceptance was observed. The five PostgreSQL integration tests remain skipped in the aggregate command. This change did not deploy to Azure or mutate Azure resources; local application containers and persistent volumes were not started, stopped, or changed by these checks.
