# EP02-S04 Azure DEV deployment and live workload observation — 2026-10-16

**Scope:** Deployment and live verification of the PitchMind Azure DEV cloud infrastructure and Container App API workload in Resource Group `rg-pitchmind-dev-001`.

**Environment:** Azure DEV  
**Subscription:** `Azure subscription 1` (`a09b1f8c-6578-4f70-a460-bdda03133549`)  
**Region:** `centralindia`  
**Resource Group:** `rg-pitchmind-dev-001`  
**Git Base Revision:** `cbd6859846ee194f2f85ef2824156a2146250535` on `main`  
**Deployment Tooling:** Azure CLI 2.65.0, Bicep CLI 0.30.23  

---

## 1. Safety Gate & Explicit Owner Approval

In strict accordance with the Engineering Council Operating Model and ADR deployment rules:
1. **Preflight What-If Execution:**
   Executed `./scripts/azure-dev-whatif.sh --resource-group rg-pitchmind-dev-001`.
   Result: 5 resource modifications (safe parameter sync), 5 unchanged, 0 deletions.
2. **Explicit Change-Specific Owner Authorization:**
   Deployment parameters and What-If scope were submitted to the repository owner via interactive gate.
   Explicit confirmation granted by repository owner on 2026-10-16.

---

## 2. Infrastructure Deployment Execution

Command executed:
```bash
az deployment group create \
  --resource-group rg-pitchmind-dev-001 \
  --template-file infra/main.bicep \
  --parameters infra/environments/dev.bicepparam
```

**Deployment Result:**
- `provisioningState`: `Succeeded`
- `duration`: `40.407s`
- `deploymentName`: `main`

---

## 3. Provisioned Cloud Resources Inventory

All 10 resources in `rg-pitchmind-dev-001` verified in `Succeeded` status:

| Resource Name | Resource Type | Location | Provisioning Status |
| :--- | :--- | :--- | :--- |
| `pm-dev-logs` | `Microsoft.OperationalInsights/workspaces` | `centralindia` | Succeeded |
| `pmdev4iwntkatliy6sacr` | `Microsoft.ContainerRegistry/registries` | `centralindia` | Succeeded |
| `pm-dev-eh` | `Microsoft.EventHub/namespaces` | `centralindia` | Succeeded |
| `pmdev4iwntkatliy6sst` | `Microsoft.Storage/storageAccounts` | `centralindia` | Succeeded |
| `pmdev4iwntkatliy6skv` | `Microsoft.KeyVault/vaults` | `centralindia` | Succeeded |
| `pm-dev-cae` | `Microsoft.App/managedEnvironments` | `centralindia` | Succeeded |
| `pm-dev-vnet` | `Microsoft.Network/virtualNetworks` | `centralindia` | Succeeded |
| `pm-dev-4iwntkatliy6s-sbus` | `Microsoft.ServiceBus/namespaces` | `centralindia` | Succeeded |
| `pm-dev-demo-identity` | `Microsoft.ManagedIdentity/userAssignedIdentities` | `centralindia` | Succeeded |
| `pm-dev-api` | `Microsoft.App/containerApps` | `centralindia` | Succeeded |

---

## 4. Container App Workload & Revisions

Azure Container App `pm-dev-api` details:
- **FQDN:** `https://pm-dev-api.whitebay-d0e7e2e6.centralindia.azurecontainerapps.io`
- **Target Ingress Port:** `8080`
- **Ingress Type:** External
- **Active Revision:** `pm-dev-api--0000001`
- **Replicas:** `1` (Active: `True`, Traffic: `100%`)
- **HealthState:** `Healthy`
- **ProvisioningState:** `Provisioned`

---

## 5. Live Endpoint Verification & Probe Evidence

Live HTTPS probe verification against the deployed Container App:

### Probe 1: `/health` (ASP.NET Core / Kestrel Health Check)
```http
GET /health HTTP/2
Host: pm-dev-api.whitebay-d0e7e2e6.centralindia.azurecontainerapps.io

HTTP/2 200 OK
content-type: text/plain
server: Kestrel
cache-control: no-store, no-cache
x-content-type-options: nosniff
x-correlation-id: 0HNPBDVPV5AHT:00000001

Healthy
```

### Probe 2: `/api/health` (PitchMind API Diagnostics Contract)
```http
GET /api/health HTTP/2
Host: pm-dev-api.whitebay-d0e7e2e6.centralindia.azurecontainerapps.io

HTTP/2 200 OK
content-type: application/json; charset=utf-8
server: Kestrel
x-content-type-options: nosniff
x-correlation-id: 0HNPBDVPV5AI3:00000001

{"status":"ok","service":"pitchmind-api"}
```

---

## 6. Acceptance Limits and Next Steps

- This evidence verifies EP02-S04 (deploy and observe a minimal DEV workload) in the real Azure DEV cloud subscription with live external HTTPS ingress and Kestrel health probes passing.
- The workload runs the validated minimal offline synthetic container image.
- Managed Azure PostgreSQL Flexible Server private integration, live telemetry ingestion over Event Hubs, and Foundry AI agent integration will be bound under subsequent epics with their respective gates.
- Product Owner acceptance remains pending PO formal review.
