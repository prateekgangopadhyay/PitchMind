# ADR-005 — Azure DevOps owns CI/CD
Status: Accepted (target), 2026-10-08

**Decision:** GitHub hosts source, Azure Pipelines performs PR validation, builds and promotions. Build images once and promote by immutable digest DEV→STAGE→PROD. Use workload identity federation, protected Environments, explicit approvals and release evidence.

**Consequences:** No GitHub Actions CI/CD. Pipeline templates are starter scaffolds until Azure DevOps project, GitHub service connection, agent pools and permissions are configured.
