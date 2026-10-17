# ADR-005 — Azure DevOps owns CI/CD
Status: Accepted (target), 2026-10-08

**Decision:** GitHub hosts source, Azure Pipelines performs PR validation, builds and promotions. Build images once and promote by immutable digest DEV→STAGE→PROD. Use workload identity federation, protected Environments, explicit approvals and release evidence.

**Consequences:** No GitHub Actions CI/CD. Pipeline templates are starter scaffolds until Azure DevOps project, GitHub service connection, agent pools and permissions are configured. Image publication and application deployment use distinct workload-federated service connections: `AcrPush` scoped to the registry and the minimum update permission scoped to the named app. The opt-in delivery path rejects missing or reused connection names before ACR access. `ManualValidation@0` is a pause, not owner identity enforcement; queue permissions and protected Environment approvers must be restricted to the repository owner and verified externally. The repository does not assert that connections, role assignments, or approval controls are configured.


**Superseded in part by [ADR-015](ADR-015-main-to-dev-automatic-delivery.md):** owner-reviewed commits to protected `main` now authorize automatic application delivery to DEV after validation. The historical opt-in/manual-gate description above records the former policy. Infrastructure mutations and STAGE/PROD remain separately approved.
