# Azure DEV inventory and integration status

**Scope:** repository implementation plus the read-only resource inventory checked on **2026-10-28** in subscription `a09b1f8c-6578-4f70-a460-bdda03133549`, resource group `rg-pitchmind-dev-001`. An inventory proves resource existence and selected configuration only; it does not prove workload identity grants, reachability, successful processing, or current health.

## DEV service inventory

| Resource | Observed state | Application role / caveat |
| --- | --- | --- |
| `pm-dev-private-cae` | Azure Container Apps environment, Consumption profile | Hosts API, EventWorker, and Redis app workloads. |
| `pm-dev-api-vnet` | Container App | API and React assets. Current readiness and active revision should be checked per release. |
| `pm-dev-api-vnet-eventworker` | Container App resource | Event Hubs consumer host. Resource presence alone does not prove a ready replica or active consumption. |
| `pm-dev-redis-cache` | Container App, `redis:7-alpine`, min/max replicas 1 | This is the current Redis cache. It is not Azure Managed Redis and the replica remains allocated. |
| `pm-dev-eh` | Event Hubs Standard | Candidate event transport. |
| `pitchmind-postgresql-dev` | Flexible Server, Burstable `Standard_B1ms` | Candidate outbox/history and canonical schema/read models. Candidate records remain non-canonical. |
| `pmdev4iwntkatliy6sst` | Standard LRS Storage account | Blob checkpoint storage. |
| `pm-dev-signalr` | Azure SignalR Free F1, capacity 1 | Cursor-only UI notifications; F1 quotas apply. |
| `pitchmind-ai-resource` / `pitchmind-ai` | Azure AI / Microsoft Foundry project | Project-agent endpoint configured in the application path. Recent analyst calls returned 503. |
| `pitchmind-ai-srch-s3d9` | Azure AI Search Serverless | Resource is present; current candidate analyst 503 does not establish Search use. |
| `pmdev4iwntkatliy6sacr` | Azure Container Registry Basic | Container image registry. |
| `pmdev4iwntkatliy6skv` | Azure Key Vault | Resource is present; effective secret retrieval requires separate verification. |
| `pm-dev-logs` | Log Analytics workspace | Container Apps diagnostics. |
| `pm-dev-4iwntkatliy6-sbus` | Service Bus Standard | Present, but not part of the current candidate live-feed path. |
| `pm-dev-vnet`, private DNS zones/links, `pm-dev-blob-pe` | Network resources | Presence does not prove private DNS, routing, firewall, or service connectivity. |
| Managed identities | API runtime, EventWorker, image pull, and demo/migration identities | Role assignments and effective permissions require separate verification. |

No Azure Managed Redis instance was found in the checked resource group. A separate Foundry resource also exists in the group, but the current application setting and Bicep default name `pitchmind-ai-resource`; do not conflate resource presence with which endpoint the app calls. No VPN Gateway was present in this inventory.

## Implemented candidate-feed route

The current source path is:

1. The API reads the packaged synthetic JSON source and publishes source-order candidate events to Event Hubs.
2. EventWorker consumes the candidate hub, writes raw candidate envelopes or poison records to the PostgreSQL candidate outbox, then advances the Blob checkpoint.
3. The API candidate-outbox dispatcher writes candidate event history, updates the Redis cursor, sends SignalR a cursor-only notification, then acknowledges the dispatch item.
4. The UI retrieves event pages from the API. SignalR prompts refresh; it does not transport event facts.
5. Guest access is limited to the DEV synthetic candidate feed. Guest-session enrollment issues a short-lived signed fan token; candidate questions require that bearer token and are limited to the fan audience. The same token can negotiate and subscribe to candidate-feed SignalR updates only; it does not authorize canonical match realtime or analyst/admin actions. Guest mode is not Entra authentication. The clear-data route is anonymous only in the explicitly guest-enabled DEV profile.

The candidate tables are separate from canonical football tables. The source's clock, event mapping, coordinate units/orientation, player metrics, and football realism remain unvalidated. Do not describe the stream as canonical match truth.

The reset endpoint's code clears candidate PostgreSQL records and Redis cache state, resets in-memory candidate state, and starts a new replay from sequence zero. A recent owner-supplied DEV reset call returned a dependency-unavailable error. Therefore the deployed clear/cache/restart transaction and subsequent visible replay are **not verified end to end**.

## AI Analyst status

The API has a direct Foundry project-agent adapter. It sends a bounded packet of persisted candidate events to one of ten allowlisted project agents and validates response shape and cited event-ID membership. The answer, if generated, is a hypothesis; the API does not persist a durable event-report ledger. In the current source, the browser's tactical-discussion helper computes illustrative text locally and is not a response from Azure AI agents.

Recent owner-supplied requests to `/api/v1/simulation/candidate/analyst/questions` returned `503 ANALYST_UNAVAILABLE`, including requests selecting `TacticalAnalyst`. Therefore live agent invocation is **not currently verified as working**, despite Foundry resources existing. The application returns an unavailable error rather than substituting a generated sample answer. The browser errors identify the downstream call failure but do not expose its upstream root cause. Check the API revision's configuration, managed identity token acquisition, role assignment to the project agent, project/agent name, and API logs correlated with the supplied request ID.

## Source/configuration versus cloud evidence

- **Source and Bicep:** API/EventWorker Azure profiles, Event Hubs publishing/consuming, PostgreSQL candidate persistence, Blob checkpointing, Redis cursor cache, SignalR notification, guest candidate-feed option, and Foundry project-agent invocation are implemented or declared.
- **Azure inventory:** resources listed above were observed on 2026-10-28. The Redis app was confirmed to run `redis:7-alpine` with one minimum replica.
- **Runtime health:** the resource inventory does not prove a full event traversed Event Hubs → EventWorker → PostgreSQL → Redis → SignalR → UI. The latest AI failure is directly evidenced by owner-supplied 503 responses. A successful live Foundry response is not available as current evidence.
- **Cost model:** Container Apps Consumption has a shared free grant; PostgreSQL, Event Hubs Standard, ACR Basic, storage, Search, AI inference, private endpoints/DNS, and other metered services may incur charges. SignalR currently uses Free F1. See [cost assumptions](../cost/COST-ASSUMPTIONS.md).

## Deployment configuration

`pipelines/azure-pipelines.yml` currently defaults `enableDevDelivery=true` and `enableCandidateFeed=true`. `infra/private-dev-topology.bicep` defaults the topology/feed off when invoked independently. Its `provisionRedis` default is false, which selects the Redis Container App; setting it true declares the optional Azure Managed Redis resource. Pipeline YAML is not proof of configured service connections or server-side approval checks. Before applying a change, review the exact what-if, image digest, cost estimate, and configured Azure DevOps gates. See [pipeline guide](../../pipelines/README.md), [deployment view](../architecture/04-deployment.md), and [infrastructure guide](../../infra/README.md).

## Checks required after a release

1. Confirm active API/EventWorker revisions, image digest, health, and readiness.
2. Check managed identity assignments, PostgreSQL migration/schema/grants, and service network resolution.
3. Start one source replay and correlate a source sequence across Event Hubs, PostgreSQL outbox/history, Blob checkpoint, Redis cursor, SignalR notification, and UI page response.
4. Test clear/restart and pause/resume from the UI/API.
5. Invoke a project agent and retain the upstream response status/request ID plus API correlation ID; do not call it fixed until a successful answer is received and validated.
