# EP02-S02 — Identity design and evidence boundary

**Status:** Design evidence only; implementation and acceptance remain blocked.
**Revision basis:** `764251b39a497a0440ede1a0b87365e330692012` (`origin/main` at branch creation).
**Scope:** repository-observed Azure identity configuration and the least-privilege contracts needed before canonical workloads exist.

## Observed state

| Principal / boundary | Repository evidence | What this establishes | What remains unverified |
|---|---|---|---|
| Offline demo Container App | `infra/modules/demo-workload.bicep` creates a user-assigned identity and attaches it to the demo app. | The demo has an explicit workload identity in IaC. | No live identity attachment or token acquisition was tested in this work. |
| Demo image pull | The same module grants built-in `AcrPull` at the existing registry resource and configures that identity for the registry. | The declared role scope is the registry, not the subscription or resource group. | No live pull/denial test was run. |
| Azure DevOps pipeline | `pipelines/azure-pipelines.yml` takes a service-connection name; `pipelines/README.md` specifies workload identity federation. | The intended authentication mode is documented without a client-secret parameter. | No registered pipeline or federated service connection is present in repository evidence; tenant, subscription, project and pipeline subject binding are unresolved. |
| Key Vault | `infra/modules/key-vault.bicep` enables RBAC, purge protection and network denial. | Vault authorization is designed for RBAC. | There are no workload role assignments or private connectivity proof in this repository state. |
| Canonical API, worker and database | Container Apps module creates only the environment; PostgreSQL module is an explicit placeholder. | No canonical workload identity or PostgreSQL role is currently implemented here. | API/worker identities, database authentication, runtime/migrator separation and negative access tests are absent. |

The offline demo's `AcrPull` grant is not evidence for Event Hubs, Storage, Key Vault or PostgreSQL authorization. The target C4 and ADR-007 describe intended architecture, not observed access.

## Permission contract for future implementation

| Workload | Intended identity | Resource and minimum permission | Required denial case |
|---|---|---|---|
| Image build/push | Azure DevOps federated service connection, bound to the registered pipeline/stage subject | The exact ACR resource only; `AcrPush` for image publication. No client secret. | Cannot modify resource groups, app configuration, Key Vault, messaging or database. |
| App release | A separately scoped federated connection for the approved deployment stage | The named demo app only, with an owner-reviewed minimal update role. Do not reuse the registry-push principal for broad resource-group writes. | Cannot push images, change identities/RBAC, or write to unrelated apps. |
| Offline demo app | User-assigned managed identity | Exact ACR only; `AcrPull`. No data-plane permissions. | Cannot push/delete images or access Storage, Key Vault, Event Hubs or PostgreSQL. |
| API/runtime and event worker | Separate managed identities per workload | Add only after workload boundaries and concrete operations are implemented. PostgreSQL runtime must not own schema; Event Hubs roles must match sender/receiver direction. | Runtime cannot run DDL or migrations; API cannot consume/publish unrelated event streams; worker cannot access Key Vault secrets by default. |
| Schema migration | Dedicated migration identity, invoked only by a controlled migration stage | PostgreSQL schema-owner/DDL permissions required by the migration, on the application database/schema only. Never attach it to the running API or worker. | Runtime identity cannot perform DDL; migration identity cannot administer Azure resources or access unrelated databases. |
| Key Vault secret consumer | Workload identity only when a documented secret cannot be replaced by identity auth | `Key Vault Secrets User` on the specific vault, or narrower resource scope if supported by the secret lifecycle. | Cannot manage vault, keys, certificates or role assignments; unrelated workload cannot read the secret. |

These entries define boundaries, not grants to create now. In particular, no PostgreSQL, Event Hubs, API runtime, or migration role assignment should be added until the corresponding service/module, network path, database schema, owner and executable denial test exist.

## Verification matrix

| Requirement | Test/evidence | Result in this branch |
|---|---|---|
| The pipeline YAML has no direct secret-shaped parameter declarations | `test_azure_pipeline_yaml_has_no_secret_shaped_parameters` scans only direct secret-shaped declarations in `pipelines/azure-pipelines.yml`. It does not inspect Bicep, enforce WIF documentation, or inspect Azure DevOps configuration. | Static check only; not a repository-wide secret scan or evidence of a configured federation. |
| The offline demo template declares a user-assigned identity and registry-scoped `AcrPull` | `test_offline_demo_declares_registry_scoped_acrpull` checks the identity, registry scope, built-in role ID, image-pull identity reference and disabled ACR admin user in the relevant Bicep modules. | Static source assertions only; they do not prove this is the only grant, nor observe Azure RBAC. |
| Cross-role denial | Requires live PostgreSQL runtime-vs-migrator and Azure resource access probes. | **Blocked / not run:** canonical workload and database roles are absent; this local task makes no Azure calls. |
| Identity verifiable in DEV | Requires approved deployment, actual token/role probes and denied-operation receipts. | **Blocked / not run:** no DEV deployment or Azure mutation was authorized in this task. |

## Local release-pipeline guard added in draft PR #16 (historical; superseded by ADR-015)

The delivery pipeline uses separate `acrPushServiceConnection` and `devDeploymentServiceConnection` names. A local Python preflight rejects missing, placeholder, or identical names before any build/push/deployment stage can run. The preflight is an Ubuntu agent job; `ManualValidation@0` remains in an agentless `pool: server` job and depends on the preflight. Image build and deploy templates consume only their respective connection parameter. Documentation requires workload identity federation and registry/app-scoped permissions. This guards configuration shape; it does not prove that either service connection exists, uses federation, or has correct RBAC. This section describes the previous manual release design; ADR-015 now governs automatic application delivery from protected `main`.

The manual-validation task is not owner-only authorization: [Microsoft documents](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/manual-validation-v0?view=azure-pipelines) that pipeline users with Queue builds permission can resume or reject it. Restricting the pipeline queue permission and protected Environment approvers to the repository owner is an external prerequisite, currently **unverified** because Azure DevOps is not registered or configured in repository evidence.

On this revision, `python3 -m unittest scripts.test_identity_baseline scripts.test_validate_release_identity_inputs -v` passed 7/7, including separate connection-parameter routing, job-pool ordering, missing/placeholder/reused-name rejection, and the existing source assertions. `node scripts/validate_operating_model.mjs` passed with 12 epics, 59 stories, 28 skills, zero cycles, and two approval gates. These checks validate configuration names and YAML wiring only; they do not establish that names resolve to different principals, or that owner-only queue/approval restrictions exist. No Azure DevOps pipeline was registered or run. The actual DEV identity checks and cross-role negative probes remain blocked as stated above.

## Unblockers

1. Register the Azure DevOps pipeline and bind a workload-federated service connection to its exact organization/project/pipeline subject. Split image publication and release permissions before enabling delivery.
2. Implement the canonical workload and private PostgreSQL topology, then publish named runtime and migration database roles.
3. Add executable allow-and-deny tests against those actual identities and roles.
4. Obtain separate explicit approval for any exact DEV resource/identity change; then capture deployed principal IDs and both positive and denied operation receipts.

Until these conditions are met, EP02-S02 is not accepted. This document does not claim deployment readiness or Product Owner acceptance.
