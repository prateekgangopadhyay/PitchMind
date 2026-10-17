# Azure DevOps Pipelines

Source lives in GitHub; Azure DevOps is the sole CI/CD orchestrator. No GitHub Actions.

**Delivery behavior:** PRs targeting `main` or `codex/*` run validation only. Each commit to protected `main` runs the same validation first, checks release identity/target inputs, builds and pushes one immutable image digest, deploys that digest to `pitchmind-dev`, and runs an API smoke check. Failed stages stop the release. DEV application release requires no manual approval after merge; the owner authorization is the reviewed PR entering protected `main`.

This YAML change does not configure GitHub branch rules, register the pipeline, create federated service connections, assign Azure RBAC, or remove an Environment approval check. Until those external settings are configured and verified, automatic delivery is not operational. Do not deploy Azure resources as part of enabling the application pipeline.

## Required setup
1. Connect the GitHub repository to Azure DevOps and register `pipelines/azure-pipelines.yml`.
2. Protect GitHub `main`: require an owner-approved PR, require successful Azure Pipeline validation, restrict direct pushes and bypass. Ensure commits deployed to DEV have passed the same checks.
3. Create two distinct Azure Resource Manager workload-identity-federated service connections: `pitchmind-acr-publisher-wif` with `AcrPush` scoped only to the registry; and `pitchmind-dev-deployer-wif` with the minimum reviewed permission scoped only to `pm-dev-api`. Authorize only this pipeline. Never reuse the publisher identity or grant resource-group-wide access.
4. Create/verify Azure DevOps Environment `pitchmind-dev` with no manual approval check for automatic app release. Keep `pitchmind-stage` and `pitchmind-prod` separately protected.
5. Confirm the DEV target parameters and smoke endpoint match the intended resources. Then run a normal PR and merge a reviewed change to main to observe first delivery. A successful local check or YAML parse is not Azure execution evidence.

Azure infrastructure changes, role assignments, schema migrations and STAGE/PROD releases remain outside this automatic application delivery path and require their own approvals. Never place secrets in pipeline YAML. PRs must not receive production credentials.
