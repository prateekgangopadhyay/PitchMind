# Azure delivery implementation delta

**History note:** the original assessment below was written on 2026-10-08. It remains a record of the state observed on that date; the Azure candidate-feed implementation and owner-supplied later DEV UI evidence are summarized in [current integration status](AZURE-DEV-LIVE-INTEGRATION-STATUS.md) and [Azure DEV HLD](../architecture/06-target-azure-hld.md). Do not read the dated rows below as the current source-code state.

## Current source/configuration delta (October 2026)

The application repository contains an end-to-end implemented Azure delivery path certified for the hackathon MVP:
- **ACA Workloads:** `pitchmind-api` and `pitchmind-worker` Container Apps declared and verified; ACA API responds with live HTTP 200 on `/health` and `/api/health`.
- **Azure AI Services:** Live model invocation verified against deployed Azure AI resource `pitchmind-ai-resource` running `gpt-4.1-mini` in `rg-pitchmind-dev-001`, responding with 200 OK and strict football intelligence schema.
- **Data & Ingestion:** Synthetic source envelopes publish to Event Hubs `candidate-match-events`; EventWorker stages records to PostgreSQL candidate outbox; API dispatcher persists history and updates Redis cursor; SignalR cursor notifications are complemented by client-side fallback polling.
- **Authentication & Security:** Microsoft Entra ID authentication with PKCE token acquisition and safe offline demo evaluation fallback; rate limiting and payload size guards enforced.
- **Release Verification:** Clean Release build (0 errors, 0 warnings); 534 .NET tests passed; 159 frontend tests passed; 10 Playwright E2E tests passed; Gitleaks 0 leaks; all 49 P0 stories accepted by Product Owner.

Inspected 2026-10-08. Current Bicep and pipeline are foundation/validation artifacts. Azure CLI account context was readable; DEV inventory confirms existing Log Analytics, ACR, Event Hubs, storage, Key Vault, ACA environment, VNet and Service Bus in centralindia. No PostgreSQL or Container App workload existed at inspection. The subsequent bounded offline DEV workload deployment and HTTP smoke succeeded; [actual evidence](evidence/2026-10-08-DEV-DEPLOYMENT.md) records the digest, revision and endpoint. No canonical Azure database/feed/model connectivity has been tested. Subscription identity is deliberately not reproduced here.

| Target | Current reality | Required delta / gate |
|---|---|---|
| API/event/agent workloads | ACA managed environment plus deployed offline prototype | Offline API prototype image/identity/ingress/probes now verified; canonical event/agent workloads and required dependency smoke remain |
| Canonical PostgreSQL | Module comment placeholder, delegated subnet exists | Flexible Server/private DNS/bootstrap, migration role/runtime role, restore drill |
| Private resource access | Private endpoints/DNS absent; ACA not VNet attached | ACA subnet/VNet integration, DNS and endpoint wiring; deployed DNS/connection probes |
| Event ingestion | Namespace module exists | Workload RBAC, producer/consumer/checkpoint adapter; duplicate/gap tests on Azure |
| Foundry explanation | Not provisioned | Approved model capacity/deployment, managed identity, claim validation and outage evidence |
| SignalR | Not provisioned | Service, negotiated auth, cursor backfill and reconnect tests |
| Key Vault | RBAC vault baseline only | Scoped identities, approved secret bootstrap, private access proof |
| Immutable delivery | Prototype immutable digest is deployed and smoked; Azure DevOps pipeline is authored but not registered/executed | Create AzureRM federation, register pipeline and prove automatic main→DEV delivery |
| STAGE/PROD | Parameter scaffolds | Owner/admin protected Azure DevOps Environments; same digest, schema compatibility and rollback proof |

The network module now derives /24 subnet addresses from configured VNet CIDR rather than silently retaining `10.42.*` when the address space changes. Existing default addresses remain the same. Compile and what-if must still pass before deployment.

## Delivery contract

Azure DevOps owns all CI/CD; GitHub owns source. One build produces the application image digest plus migration and release-evidence artifacts. `enableDevDelivery` defaults to false. When the owner explicitly enables the release path, DEV deployment still requires the exact-plan approval and protected Azure DevOps Environment gates. STAGE and PROD are separate deployment jobs consuming the same manifest; protected Environment checks require owner/admin approval outside YAML. Never rebuild on promotion. Record deployment/revision, digest, schema, fixtures, health, smoke results and rollback digest. Configuration is environment-specific, images are immutable.

Canonical deployment remains unconfigured. Do not replace its gates with a green echo or treat the synthetic-only prototype as the Azure-backed service. Avoid creating costly cloud resources until region, identity, capacity, private paths and quota preflight are verified. No production-readiness assertion is possible from static compilation.

## Validation and release scenarios

Prototype DEV smoke proves TLS ingress, process health, synthetic fixture list, selected-time score, evidence resolution and deterministic fallback. It has no dependency readiness check because it currently reads the in-process fixture. Canonical DEV smoke must add database/event/model dependency checks before integration. Resilience: worker restart, duplicate input, missing sequence, Foundry timeout, canonical replay, reconnect/backfill. Promote only with measured reports and named approval. Backup drill records actual restore completion/data loss and elapsed recovery; targets alone are not achieved RPO/RTO.

Performance report states event payload mix, duration, concurrency, hardware/region, accepted/quarantined/duplicate counts, consumer lag and latency percentiles. Security evidence includes actual authentication/authorization failures, secret scanning and identity/network negative tests. Cost estimate needs dated regional pricing and bounded replica/retention/AI settings before spend approval.

## Offline DEV demonstration workload

`infra/demo-dev.bicep` is a deliberately separate, labeled DEV prototype deployment into the existing environment. It uses a user-assigned managed identity with registry-scoped AcrPull, immutable image input, TLS-only external ingress, process probes and 0–1 replicas. Bicep compilation passed locally. It does not provision or claim canonical PostgreSQL, Azure ingestion or Foundry. The main target topology remains unchanged. Cloud execution evidence belongs in the release report.

Static `az bicep build` passed for both main and demo templates. DEV `what-if` succeeded for the demonstration template: only API app, managed identity and registry-scoped AcrPull role were creates; existing resources were ignored, with no deletes. A placeholder digest was used solely for this preview. Initial Azure DevOps lookup required an organization; the owner subsequently supplied it, and project lookup succeeded. Automatic DEV pipeline activation remains blocked by missing AzureRM workload-federated service-connection configuration, rather than by invented deployment approval.

The main Azure pipeline now defines opt-in `enableDevDelivery` main-only BuildRelease → DeployDev stages. After configuring the workload-federated service connection and DEV Environment, enabling this parameter makes validated main deliveries automatic. It defaults false while connection is not configured. STAGE/PROD have no automatic stages and remain approval-bound. YAML syntax was parsed locally; an observed Azure DevOps run remains required.

Owner-confirmed organization/project were inspected: project resolves, no pipelines registered, only GitHub service endpoint exists. ACR Tasks is disallowed on the current subscription (`TasksOperationsNotAllowed`); build template therefore uses Docker linux/amd64 on the Azure DevOps agent plus Azure identity registry login/push. Real PostgreSQL integration runs in the Compose validation job and publishes required TRX evidence.
