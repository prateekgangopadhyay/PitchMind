# Azure DevOps Pipelines

Source lives in GitHub; Azure DevOps is the sole CI/CD orchestrator. No GitHub Actions.

**Current state:** repository validation and infrastructure-preview templates. The PR validation jobs restore/build/test .NET, install the frontend from its committed npm lockfile, build/test/lint it, run repository contract/feed checks, smoke-test local Docker services with real PostgreSQL integration tests, and compile Bicep without deployment. .NET TRX and frontend JUnit results are published as Azure DevOps test results, including on failed jobs. Deployment/promotion is intentionally blocked until service connections, managed identity, private network, image digest promotion, DB migration, rollback and environment approvals are configured.

The YAML PR trigger includes `main` and `codex/*` target branches so stacked feature PRs receive validation triggers when the pipeline is registered. PR validation is defined for GitHub; the hosted pipeline and external required-check policy still need observation.

EP01-S03's deliberate failure probe is a separate `Validate` job, disabled by default. To verify the hosted gate manually, queue a validation-only run with `runGateFailureProbe=true`; the xUnit probe must fail and make `Validate` fail. This is intentionally a red run and does not enable the Azure delivery parameter. Local tests verify the probe's behavior and the YAML contract; only an observed Azure DevOps run can prove hosted execution and PR blocking. Keep the PR branch policy requirement below until that external policy and run are independently observed.

## Setup
1. Connect private GitHub repo to Azure DevOps.
2. Create Azure Resource Manager service connection `sc-pitchmind-dev` using workload identity federation.
3. Configure Azure DevOps Environments `pitchmind-dev`, `pitchmind-stage`, `pitchmind-prod` with protected approvals and checks.
4. Register `pipelines/azure-pipelines.yml` as the pipeline.
5. Set Azure Repos/GitHub PR branch protection to require this Azure Pipeline validation. This repository cannot verify that external branch policy from YAML; until configured and observed, required-check enforcement is unverified.
6. Add workload app build and release templates when code exists.

Never place secrets in pipeline YAML. PRs must not receive production credentials.
