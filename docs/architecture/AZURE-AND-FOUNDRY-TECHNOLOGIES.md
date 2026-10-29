# Azure and Microsoft Foundry technologies

This guide explains the Azure services used by PitchMind DEV, what each does, and how the AI Analyst is connected. It describes the implementation and the last recorded Azure inventory; it does not certify current runtime health. Inventory was reviewed on 2026-10-28. See [DEV integration status](../engineering/AZURE-DEV-LIVE-INTEGRATION-STATUS.md) for release-specific evidence.

## Azure technology map

| Technology | How PitchMind uses it | Authority and current caveat |
| --- | --- | --- |
| **Azure Container Apps (ACA)** | Hosts the API and React assets, the EventWorker, and the current Redis container app. | The API and worker are separate workloads. Resource existence does not prove healthy replicas or a complete event path. |
| **Azure Event Hubs Standard** | Transports ordered synthetic candidate-event envelopes from the API to EventWorker. | Streaming transport; PostgreSQL is the durable event store. Delivery can repeat, so handlers use idempotency. |
| **Azure Database for PostgreSQL Flexible Server** | Stores candidate outbox/history and the separate canonical schema/read models. | Raw candidate events are not canonical football facts. Runtime readiness and schema/grants need release verification. |
| **Azure Blob Storage** | Stores the Event Hubs processor checkpoint. | Checkpoint advances after the candidate envelope or poison record is durably staged. It is processing progress, not match truth. |
| **Redis on Azure Container Apps** | Holds the latest candidate-feed cursor/cache projection. | DEV inventory found `pm-dev-redis-cache` running `redis:7-alpine`, minimum/maximum one replica. It is not Azure Managed Redis and is rebuildable. |
| **Azure SignalR Service (Free F1)** | Sends cursor-only notifications that tell browsers to refresh. | Event payloads are fetched from the API. F1 quotas apply; SignalR does not hold or transmit authoritative event history. |
| **Microsoft Entra ID and managed identities** | Entra validates signed-in user tokens; workload identities let API and worker access Azure services. | The public guest demo uses a separate short-lived, limited fan token. Role assignments in templates must be verified against deployed identities. |
| **Microsoft Foundry** | Hosts the project agents used for source-bounded candidate-match analysis. | One selected agent is invoked per request. The current API does not orchestrate a multi-agent discussion. Responses are hypotheses, not canonical insights. |
| **Azure Container Registry Basic** | Stores application container images used by the Container Apps release. | Deployments should use the immutable image digest from the release manifest. |
| **Azure Key Vault** | Provides the configured secret-management resource boundary. | Resource existence does not demonstrate that every application secret is retrieved from it; verify identity access and deployed settings. |
| **Azure Monitor / Log Analytics** | Receives Container Apps diagnostics in the observed DEV topology. | This is operational telemetry; do not log bearer tokens, Foundry credentials, or raw sensitive payloads. |
| **Azure AI Search Serverless** | Present in the DEV resource group and documented as a knowledge/RAG capability. | It is not in the current candidate analyst request path unless explicitly configured; its presence does not prove current use. |
| **Azure Service Bus Standard** | Provisioned as an additional messaging resource. | It is not part of the current candidate event path described here. |
| **Azure Virtual Network, private DNS, and private endpoints** | Provide private connectivity boundaries for selected services. | Resource presence alone does not prove DNS resolution, routing, firewall rules, or application reachability. |
| **Azure DevOps Pipelines** | Runs validation and guarded DEV delivery. | The pipeline uses the self-hosted `Default` pool. Its Mac agent is currently stopped; start it before queuing a run. External approvals and service connections are managed in Azure DevOps. |

The current architecture and cost notes are in the [C4 deployment view](04-deployment.md), [DEV inventory](../engineering/AZURE-DEV-LIVE-INTEGRATION-STATUS.md), [pipeline guide](../../pipelines/README.md), and [cost assumptions](../cost/COST-ASSUMPTIONS.md). Some services are metered or have quotas; consult current Azure pricing before changing SKUs or enabling optional resources.

## Microsoft Foundry request flow

1. The browser submits a match ID, selected source-clock value, audience, allowlisted agent name, and question to `POST /api/v1/simulation/candidate/analyst/questions`.
2. The API checks authorization, loads persisted events for that match at or before the selected source time, and creates a bounded evidence packet. It never forwards the browser's Microsoft token to Foundry.
3. In Azure, the API obtains an Entra token for `https://ai.azure.com/.default` using its workload identity and calls the configured Foundry project agent Responses endpoint over HTTPS.
4. The API reads a bounded Foundry response envelope, parses the answer and claims, and rejects malformed output or citations to event IDs outside the supplied packet.
5. The UI labels accepted output as a hypothesis. It is not persisted as a canonical insight; the local tactical-discussion helper is client-computed illustrative text, not a Foundry response.

The API allowlists ten agent names: `CoachFrameworkAnalyst`, `ChanceCreationAnalyst`, `GoalkeeperAnalyst`, `MatchStateStrategist`, `SetPieceStrategist`, `TransitionStrategist`, `DefensiveStructureAnalyst`, `HighPressAnalyst`, `PositionalPlayArchitect`, and `TacticalAnalyst`. `TacticalAnalyst` is the default. The client selects one agent; a business-owner/product-owner/analyst debate or parallel multi-agent orchestration is not part of the current live API implementation.

### Resolved Foundry response-size incident

The Foundry Responses API envelope includes agent instructions and tool metadata in addition to the generated text. A TacticalAnalyst response measured 30,076 bytes even though its generated answer was much shorter. The API previously capped the complete response at 16,384 bytes, causing a valid successful upstream response to be rejected and surfaced as `503 ANALYST_UNAVAILABLE`.

The source fix raises the bounded response limit to 65,536 bytes and retains the output-token and response-schema validation. It is recorded on `main` as `dbd04f1f` (2026-10-29), and the reported 503 issue is resolved. Continue normal DEV health checks after releases. See [API analyst contract](../api/API.md) and [integration status](../engineering/AZURE-DEV-LIVE-INTEGRATION-STATUS.md).

The application returns a sanitized 503 when authentication, transport, upstream status, response size, parsing, or validation prevents an answer. Logs should retain safe status/type and provider request identifiers for diagnosis, not response secrets or the full prompt. Citation membership validates that IDs were supplied; it does not prove semantic entailment or football correctness.

## AI prompt and domain references

- [Foundry tactical agent pack](../ai/FOUNDRY-TACTICAL-AGENT-PACK.md)
- [Detailed tactical-agent prompts](../ai/FOUNDRY-TACTICAL-AGENTS-DETAILED.md)
- [Per-agent instruction files](../ai/foundry-agent-instructions/)
- [Football tactical knowledge base](../ai/FOOTBALL-TACTICAL-KNOWLEDGE-BASE.md)
- [Football domain knowledge atlas](../ai/FOOTBALL-DOMAIN-KNOWLEDGE-ATLAS.md)
- [Knowledge and Azure AI Search design](../ai/FOOTBALL-KNOWLEDGE-RAG-PIPELINE.md)

Prompt files are operational guidance for configuring Foundry agents; they do not imply that the application dynamically deploys or updates agent instructions. The deployed Foundry project and agent configuration must be managed and verified separately.
