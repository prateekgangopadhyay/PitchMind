# Azure DevOps Pipelines

Source lives in GitHub; Azure DevOps is the sole CI/CD orchestrator. No GitHub Actions.

**Current state:** validation and infrastructure-preview templates. Deployment/promotion is intentionally blocked until service connections, managed identity, private network, image digest promotion, DB migration, rollback and environment approvals are configured.

## Setup
1. Connect private GitHub repo to Azure DevOps.
2. Create Azure Resource Manager service connection `sc-pitchmind-dev` using workload identity federation.
3. Configure Azure DevOps Environments `pitchmind-dev`, `pitchmind-stage`, `pitchmind-prod` with protected approvals and checks.
4. Register `pipelines/azure-pipelines.yml` as the pipeline.
5. Set Azure Repos/GitHub PR branch protection to require Azure Pipeline validation.
6. Add workload app build and release templates when code exists.

Never place secrets in pipeline YAML. PRs must not receive production credentials.
