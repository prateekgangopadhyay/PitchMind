# M03 — Azure DEV infrastructure baseline

**Scope:** reviewed DEV foundation and safe validation tooling. **Not a record of an Azure deployment.**

## Architecture and boundaries

- LOCAL uses Docker PostgreSQL and Redis from M02. No Azure dependencies are required locally.
- DEV uses Azure resources only. STAGE/PROD remain explicitly opt-out of the new DEV-only VNet and vault.
- Baseline modules: Log Analytics, Storage, Container Registry, Event Hubs, Service Bus, Container Apps environment, DEV VNet and RBAC Key Vault.
- No application images, production ingress, PostgreSQL Flexible Server, Azure Managed Redis, private endpoints, private DNS, RBAC role assignments, or application secret bootstrap are provisioned in this milestone. Those require reviewed network topology, service SKUs, identity bindings, and cost approval.
- Storage and Key Vault have public access disabled. They will not be reachable by public build agents; connectivity through private endpoints and approved networking must be implemented before workloads consume them.
- Container Registry uses Basic SKU and retains public endpoint access with admin user disabled. This is a documented DEV exception: a private registry requires Premium SKU and private networking. Do not promote this policy to production.
- Event Hubs, Service Bus, and Container Apps environment are baseline resources, not yet hardened application runtimes. Do not expose workloads or production data.

## Preflight and deployment safety

1. Install Azure CLI and Bicep: `az version`, `az bicep version`.
2. Sign in using an approved identity: `az login`, `az account set --subscription <subscription-id>`.
3. Ensure resource providers are registered and target region supports each selected SKU.
4. Obtain approved DEV subscription, resource group, region, cost budget, RBAC access, and CIDR allocation. Default DEV VNet is `10.42.0.0/16` and its two subnets are fixed `10.42.1.0/24` and `10.42.2.0/24`; change all three together if allocation differs.
5. Compile: `az bicep build --file infra/main.bicep`; compile each `infra/environments/*.bicepparam`.
6. Preview: `./scripts/azure-dev-whatif.sh --resource-group <existing-dev-resource-group>`.
7. Security, SRE, and FinOps reviewers must approve the **full** what-if output and estimated monthly charges.
8. Deploy only from an explicitly approved Azure DevOps deployment stage/service connection. M03 deliberately does not add an automatic deploy trigger.

## Acceptance criteria and evidence

- Bicep and all three parameter files compile without diagnostics on an approved Bicep version.
- DEV what-if has no unapproved deletion/replacement, unexpected public access, or out-of-scope resource.
- DEV resources are deployed by an authorized pipeline and are inspectable through Azure Resource Graph.
- Key Vault RBAC, purge protection, and network isolation are verified.
- Storage disallows anonymous blob and shared-key access.
- Resource costs, logs, quotas, region availability, and rollback strategy are reviewed.
- No credentials or environment-specific secrets are committed.
- LOCAL Docker and application test suites remain green.

**Current evidence:** code committed for review only; Bicep compilation, what-if, Azure DevOps execution, and Azure deployment have **not** been verified in this environment.

## Follow-up risks / next milestones

- Configure private endpoints and DNS for Storage, Key Vault, ACR and managed services, plus subnet routing and workload identity.
- Validate PostgreSQL Flexible Server private access and Azure Managed Redis SKU/networking before provisioning.
- Replace ACR DEV public endpoint exception with Premium private connectivity for hardened environments.
- Apply least-privilege role assignments for Azure DevOps workload identity federation; never use stored client secrets.
- Enforce budget alerts, resource tags, diagnostics, Defender policies and disaster-recovery controls before production.
