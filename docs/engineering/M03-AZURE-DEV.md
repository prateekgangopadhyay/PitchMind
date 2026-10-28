# M03 — Azure DEV runtime and delivery

This document describes the current Azure DEV candidate-feed runtime. It is not a deployment approval or a guarantee of runtime health. The most recent resource inventory is in [Azure DEV integration status](AZURE-DEV-LIVE-INTEGRATION-STATUS.md).

## Runtime topology

The API and React static assets run in Azure Container Apps. The API publishes packaged synthetic candidate source records to Azure Event Hubs; a separate EventWorker Container App stages raw envelopes or poison records in PostgreSQL before advancing a Blob checkpoint. The API candidate outbox dispatcher writes candidate history, updates a Redis cursor, and sends a cursor-only SignalR notification. The UI reads event pages from the API. Candidate records are not admitted into canonical match tables.

The current Redis deployment is `pm-dev-redis-cache`, a Container App running `redis:7-alpine` with minimum and maximum replica count 1. It is not Azure Managed Redis. The Bicep topology has an optional managed Redis provisioning path, controlled by `provisionRedis` (default false); enabling it changes the service and cost model.

Microsoft Entra ID supports signed-in access. When DEV guest access is enabled, the guest-session endpoint exchanges a public demo enrollment for a short-lived server-signed bearer token with only the `fan` role and `candidate:question` / `candidate:realtime` scopes. Candidate questions require a valid bearer token and guests can request fan-audience answers only. The token can negotiate and subscribe to the configured synthetic candidate feed’s SignalR updates; it cannot access canonical-match realtime or analyst/admin actions. Guest enrollment is rate limited. Clear-data remains anonymous only in the explicitly enabled DEV demo. The Foundry candidate analyst call has a server-side adapter but recent DEV requests returned `503 ANALYST_UNAVAILABLE`; a current successful AI response is not verified. Browser-computed tactical discussion text is illustrative, not Foundry output.

## Resource and identity boundaries

The checked DEV inventory includes Azure Container Apps, Event Hubs Standard, PostgreSQL Flexible Server, Blob Storage, the Redis Container App, SignalR Free F1, Foundry, AI Search Serverless, ACR Basic, Key Vault, Log Analytics, Service Bus Standard, VNet/private DNS, and managed identities. See the [dated inventory](AZURE-DEV-LIVE-INTEGRATION-STATUS.md) for names and caveats. Service Bus, AI Search, and AgentWorker capabilities exist in the wider architecture but are not the candidate event-processing path.

The topology uses separate runtime, EventWorker, image-pull, and migration identities. Bicep role-assignment declarations are not proof of effective permissions. Verify Event Hubs producer/consumer roles, Blob checkpoint access, PostgreSQL Entra principals/grants, Redis access, SignalR app-server access, Key Vault secret access, and Foundry agent invocation access against the currently deployed identities.

## Release controls

The pipeline YAML currently defaults `enableDevDelivery=true` and `enableCandidateFeed=true`; the Bicep module defaults candidate provisioning off when used separately. The guarded release builds an immutable application image, validates the network and Bicep plan, publishes a what-if/cost artifact, and uses its configured Azure DevOps approval/deployment gates. A successful validation stage is not evidence that an image or infrastructure was deployed. Repository code cannot confirm external service connections, agent-pool availability, or protected Environment approvers.

Before applying a change, verify the exact resource group, subnet CIDR, service connection identities, what-if output, image digest, cost estimate, and approvals. Never reuse an old approval after the what-if changes.

## Post-release verification

For the same source event/sequence, confirm:

1. API reports candidate feed enabled and starts the replay.
2. Event Hubs receives the event; EventWorker logs show consumption.
3. PostgreSQL contains the corresponding candidate outbox/history record and Blob checkpoint advances only after durable staging.
4. Redis cursor advances and SignalR emits the cursor-only notification.
5. API event pages return the event and the UI updates the match timeline/pitch.
6. Pause/resume and clear/restart behave as documented in the API contract.
7. A direct AI analyst request returns a valid, evidence-bounded answer. A 503 means this item is not passed even if the agent resource exists.

Capture image digest, revision, schema version, source hash, sequence, correlation ID, service request IDs, and health status without logging tokens or source secrets. Local Docker success is not Azure connectivity evidence.

See [C4 deployment](../architecture/04-deployment.md), [Azure HLD](../architecture/06-target-azure-hld.md), [API contract](../api/API.md), [pipeline guide](../../pipelines/README.md), and [cost assumptions](../cost/COST-ASSUMPTIONS.md).
