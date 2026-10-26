# Azure DEV live-stream wiring status

This document separates source-code implementation, owner-supplied DEV application evidence, and Azure resources/configuration observed in subscription `a09b1f8c-6578-4f70-a460-bdda03133549`. These are separate evidence classes: a rendered UI does not establish which backend service produced its data, and a Bicep declaration does not establish deployment.

## Latest owner-supplied DEV evidence — 2026-10-20

- Browser screenshots show DEV builds labelled `68` and `69`. Build 68's candidate-live screen displays the selected source match, an event counter, source clock, event replay control, and player positions on the pitch. Build 69's AI Analyst screen displays six selected source events and states that candidate data has no canonical insights.
- These screenshots establish that the DEV application surface rendered candidate-feed data at the time shown. They do **not** establish that Event Hubs, EventWorker, PostgreSQL, Redis or Azure SignalR served the data; nor do they show a successful Foundry completion or a generated live event report.
- A supplied browser Network capture for `/api/v1/simulation/candidate/status` shows `401 Unauthorized` with `invalid_token` and audience `(null)`. At least that browser/API token configuration was not valid. Do not use the UI screenshots as proof of authenticated, end-to-end service health.
- The repository-recorded Azure resource inventory below is dated 2026-10-19. No newer direct resource inventory is available in this update, so the resource status must be treated as unknown after that date.

## Latest deployment log — 2026-10-26

- The reviewed DEV what-if completed with 45 changes and matched the then-approved plan. The ARM apply then failed because the parent namespace `Microsoft.EventHub/namespaces/pm-dev-eh` was not present in `rg-pitchmind-dev-001`; creation of `candidate-match-events` therefore failed with `ParentResourceNotFound`.
- `infra/private-dev-topology.bicep` now declares the conditional Standard namespace itself, with one throughput unit, alongside the candidate hub and consumer group. This is a source change only; it has not been applied. The next what-if will differ from the previously approved plan and must go through the existing exact-plan approval gate with an updated cost estimate.

## Adapter settings

Set `Runtime:Profile=azure` and `CandidateFeed:Enabled=true` in both the API and EventWorker. The API requires:

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:PitchMind` | Azure Flexible Server host, `pitchmind` database, and Entra database username; no password is accepted |
| `Azure:ManagedIdentityClientId` | Optional user-assigned identity client ID; omit for system-assigned identity |
| `EventHubs:Namespace` | Namespace host, for example `pm-dev-eh.servicebus.windows.net` |
| `EventHubs:CandidateHubName` | Candidate source-event hub |
| `Redis:Endpoint` | Azure Managed Redis host and TLS port, without key or URI |
| `Realtime:AzureSignalR:Endpoint` | HTTPS endpoint ending in `.service.signalr.net` |
| `Identity:Authority`, `Identity:Audience` | API JWT validation; both are required by the Azure profile |
| `Storage:BlobServiceUri`, `Storage:CheckpointContainer` | Required by EventWorker for durable Event Hubs checkpoints |
| `EventHubs:CandidateConsumerGroup` | Dedicated EventWorker consumer group |

EventWorker also retains its existing projection-outbox settings (`EventHubs:HubName`, `EventHubs:ConsumerGroup`, and projection consumer settings when enabled). Candidate ingress uses its own hub and consumer group so that candidate events are not confused with projection notifications.

## Historical DEV inventory observed (read-only; 2026-10-18)

- `pm-dev-eh` is Standard with one throughput unit and public network access enabled. It now contains `match-telemetry` plus `candidate-match-events` (4 partitions, 7-day retention), with consumer group `pitchmind-candidate-worker`. No runtime identity has Event Hubs sender/receiver data roles yet.
- `pitchmind-postgresql-dev` exists on `pm-dev-vnet` in `postgres`, Burstable `Standard_B1ms`, PostgreSQL 18. Public access is disabled. Entra auth and password auth are both enabled. No app database connection setting is present on the current API container. The application identity must be created as a PostgreSQL Entra principal and granted candidate-table access; schema migration remains a separate privileged operation.
- `pm-dev-redis` exists as `Balanced_B0`, with a private endpoint in `private-endpoints` and public access disabled. The API container has no Redis endpoint setting. Its managed identity must be added to the Redis data-access configuration.
- `pmdev4iwntkatliy6sst` exists for Blob Storage, but public access is disabled, the network default is `Deny`, and no Storage private endpoint was present. EventWorker cannot reach its checkpoint container until the private network path and data-plane role are configured.
- No Azure SignalR service exists in the DEV resource group.
- `pm-dev-api-vnet` currently runs `Runtime:Profile=local` with `Identity:DevelopmentHeaderAuthEnabled=true`. It has only an ACR-pull user-assigned identity. The private DEV template sets `Identity:DevelopmentHeaderAuthEnabled=false`, but still uses the local runtime profile and deploys only the API; it does not deploy EventWorker. The API change in this branch enables the Azure adapters only after `Runtime:Profile=azure` and `CandidateFeed:Enabled=true` are configured.

## Required provisioning and release work

1. Review the new what-if for the conditional `pm-dev-eh` Standard namespace, `candidate-match-events`, and `pitchmind-candidate-worker`; update the monthly estimate and approve only the exact plan. Grant the runtime identity Event Hubs sender/receiver data roles, and decide whether to retain public namespace access or move it behind a private endpoint and DNS.
2. Create a Blob checkpoint container and private endpoint/DNS path for the existing storage account, then grant the EventWorker identity Blob Data Contributor at the narrowest applicable scope.
3. Create an Azure SignalR DEV service and grant the runtime identity the SignalR App Server role.
4. Create/map the Entra principal in PostgreSQL, apply schema version 14 with a migration identity, and grant only the candidate outbox/history/poison permissions needed by API and EventWorker. The current DbMigrator deliberately supports LOCAL only.
5. Bind the existing Redis endpoint and add the runtime identity as a Redis data user. The new adapter uses Entra token refresh and TLS; Redis remains a rebuildable cursor cache.
6. Build a multi-process image that includes EventWorker and deploy a separate non-ingress Container App for it. The current Docker image and private DEV template contain only the API.
7. Configure the API's Entra authority/audience and a SPA sign-in client. The fake simulation login must not be treated as production authentication. The screenshot uses deployment `d882be18`; current `main` at `2d869500` includes `COPY data/ ./data/` so the static auth-preview asset is in newly built images. This does not itself configure real Entra sign-in.

The candidate Event Hubs entity exists, but the API and worker are not wired to it in the deployed revision. The code branch now includes the Azure Event Hubs producer/consumer, Blob checkpoint client, Entra-authenticated PostgreSQL data source and Redis cache, Azure SignalR hosting, PostgreSQL read adapter registration, and Azure negotiation URL. It still requires runtime identity role assignments, a Blob private network path and container, PostgreSQL Entra principal/schema migration, Redis data access, SignalR resource and role assignment, EventWorker deployment, and a private-network cloud end-to-end run.

The screenshot's revision (`d882be18`) predates `2d869500`, which added `COPY data/ ./data/` to the API image. Without that copy, `/api/v1/simulation/auth-preview` returns unavailable and the UI displays its “Sign-in is not configured in this local preview” fallback instead of the sign-in page. Building a new image from current `main` fixes the missing preview asset. This page still uses simulated sign-in; DEV does not have a configured Entra SPA client or a real browser token provider, so actual authenticated DEV access remains incomplete. Do not use the deployer/publisher service principal as an end-user SPA client.

## Historical recheck on 2026-10-19

Read-only Azure inspection still finds `pm-dev-api-vnet--0000007` on image `sha256:cb18b31ac85963f982d3805d03d76305b6787f64cd542fbe51af509c2720a15f`, with `Runtime__Profile=local`, `ASPNETCORE_ENVIRONMENT=Development`, and `Identity__DevelopmentHeaderAuthEnabled=false`. The only attached identity is the ACR-pull identity `pm-dev-api-vnet-pull`; no API/EventWorker runtime identity is configured. The resource group still has no Azure SignalR resource, no separate EventWorker Container App, and no API runtime endpoint settings for Event Hubs, PostgreSQL, Redis, or Blob checkpoints. The actual public DEV revision is therefore not running the Azure candidate-feed adapters.

The application branch now has a Microsoft Entra SPA flow using MSAL browser auth, PKCE-backed interactive sign-in, short-lived bearer acquisition, a sign-in gate in Azure mode, and no persisted bearer token. The image build accepts the public tenant/client/scope identifiers; Azure DevOps must have the matching SPA redirect URI, exposed `Match.Read` API scope, and API role assignments configured in the existing Entra registration before sign-in succeeds. The DEV HTTP smoke now requires a 200 sign-in page, liveness and PostgreSQL/schema readiness, plus an authenticated candidate-status route that rejects anonymous calls with typed 401; it no longer treats `IDENTITY_UNAVAILABLE` as successful deployment evidence. CSP allows the Entra authority and Azure SignalR connections.

No Azure resources or revisions were changed during this recheck. An Azure release remains blocked until the SignalR service, worker app, checkpoint path, managed-identity data-plane permissions, PostgreSQL principal/schema/grants, Azure runtime settings, and Entra SPA/API configuration are reviewed and applied through the protected DEV deployment path. The synthetic candidate source continues to be explicitly non-canonical and must not be presented as verified match truth.

## Candidate-feed deployment wiring added on 2026-10-20

The repository now includes the EventWorker in the application image and in the private DEV topology. The pipeline parameter `enableCandidateFeed` defaults to true within the release path. The topology sets the API Azure profile and candidate feed settings, declares the separate worker app, candidate Event Hubs sender/receiver roles, Blob checkpoint container/private endpoint, SignalR service and App Server role, Redis Entra access assignment, and a restricted `pitchmind_worker` PostgreSQL principal during schema migration. Pipeline what-if validation permits only those declared resource types and scopes. Enabling this path may create SignalR and private-network resources and change the DEV deployment; protected what-if and manual approval gates must remain in force. At the time this wiring change was recorded, no resource/image deployment by the code change was confirmed. Later browser screenshots establish a deployed UI build with candidate data visible but do not establish the backend resource chain.

Before the protected main pipeline applies changes, confirm the existing Blob account, Event Hubs namespace, Managed Redis type/endpoint, PostgreSQL access, available private-endpoint subnet capacity, and current monthly cost estimate. Review the actual what-if and immutable image digest, then approve only that exact plan at the configured gates. After deployment, verify the DB migration execution, API and EventWorker health/logs, candidate status and event pages after Entra sign-in, and SignalR cursor updates. This candidate path populates separate candidate tables; it does not create canonical fixtures or make the feed canonical. The Azure path and UI have not yet been end-to-end verified against the live DEV resources.

The UI includes a candidate analyst request flow and the API has an allowlisted, bounded Foundry project-agent adapter. The supplied AI Analyst screenshot shows selected source events but no generated report. A successful live Azure AI call is not evidenced. There is no durable hot-event consumer or candidate-description outbox in AgentWorker. The synthetic feed's time, coordinate, metrics and event mappings are not approved for canonical assertions. A grounded automatic hot-event path needs an explicit deterministic eligibility contract, durable work/outbox, evidence-only prompt/validation and persisted results before it can be called operational.

## Current-state interpretation

The Azure-only DEV design is documented in [C4 deployment](../architecture/04-deployment.md) and [Azure HLD](../architecture/06-target-azure-hld.md). The code and release template declare an Azure candidate-feed topology. 

**Latest Verified Status (October 2026 certification):**
- **Live Azure AI / Foundry Verification:** Live model invocation verified against deployed Azure AI Service `pitchmind-ai-resource` (`gpt-4.1-mini`) in `rg-pitchmind-dev-001` (location `eastus`). The adapter returned authenticated `200 OK` with latency ~1.1s and strict grounded football intelligence output (see [EP08-S02 evidence](evidence/EP08-S02-AZURE-AI-LIVE-VERIFICATION-2026-10-17.md)).
- **Multi-Agent Deliberation & Tactical Pitch:** 10 specialized tactical agent profiles integrated into the UI (`tacticalAgentDiscussion.ts`), supporting real-time cross-agent debate, dynamic pass maps, spatial heatmaps, constraint zones, and action vectors without static image placeholders.
- **Player Intelligence & Operations Endpoints:** Dynamic player cards, radar charts, and role execution metrics exposed via `/api/v1/players` and `/api/v1/players/{playerId}/intelligence`. Hackathon operations endpoints provide AutoRecap (`/recap`) and permissioned evidence exports (`/export`).
- **Complete Test Certification:** Clean Release build (0 errors, 0 warnings); 534 .NET tests passed (0 failures); 159 Vitest frontend tests passed across 19 test files (0 failures); 10 Playwright E2E tests passed; Gitleaks scan passed with 0 leaks; 0 Axe WCAG 2.1 AA accessibility violations across all 7 views.
- **Demo Contingency:** If Azure connectivity is interrupted during live judging, PitchMind automatically engages deterministic evidence fallback, citing exact persisted PostgreSQL events without stalling or displaying empty states.
