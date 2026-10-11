# Azure DevOps Pipelines

Source lives in GitHub; Azure DevOps is the sole CI/CD orchestrator. No GitHub Actions.

**Current state:** repository validation and infrastructure-preview templates. The PR validation jobs restore/build/test .NET, install the frontend from its committed npm lockfile, build/test/lint it, run repository contract/feed checks, smoke-test local Docker services with real PostgreSQL integration tests, and compile Bicep without deployment. .NET TRX and frontend JUnit results are published as Azure DevOps test results, including on failed jobs. Deployment/promotion is intentionally blocked until service connections, managed identity, private network, image digest promotion, DB migration, rollback and environment approvals are configured.

## Setup
1. Connect private GitHub repo to Azure DevOps.
2. Create two Azure Resource Manager service connections using workload identity federation: an image publisher scoped to the exact ACR with `AcrPush`, and a DEV deployer scoped to the named Container App with the minimum reviewed update role. Configure distinct connection names for `acrPushServiceConnection` and `devDeploymentServiceConnection`; the opt-in release gate fails closed if either is missing or both names match. Do not reuse the publisher principal for deployment or grant either connection resource-group-wide permissions.
3. Configure Azure DevOps Environments `pitchmind-dev`, `pitchmind-stage`, `pitchmind-prod` with protected approvals and checks. Restrict pre-mutation pipeline queue/approval permissions and protected Environment approvers to the repository owner for each DEV change. The YAML `ManualValidation@0` pause alone is not owner-only: [Microsoft's task documentation](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/manual-validation-v0?view=azure-pipelines) says users with Queue builds permission can resume or reject it.
4. Register `pipelines/azure-pipelines.yml` as the pipeline.
5. Set Azure Repos/GitHub PR branch protection to require this Azure Pipeline validation. This repository cannot verify that external branch policy from YAML; until configured and observed, required-check enforcement is unverified.
6. Keep DEV delivery disabled unless the repository owner explicitly approves the exact change set. The separate connection contract does not create either connection or authorize deployment. Owner-only queue/approval permissions and protected checks are not configured or verified by this repository.

Never place secrets in pipeline YAML. PRs must not receive production credentials.
