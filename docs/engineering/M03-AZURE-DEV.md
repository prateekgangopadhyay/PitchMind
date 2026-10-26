# M03 — Azure DEV runtime and delivery

**Purpose:** describe the Azure-only DEV runtime defined by the application and guarded release path. This is not an Azure resource inventory or deployment approval. For the latest evidence, see [Azure DEV live integration status](AZURE-DEV-LIVE-INTEGRATION-STATUS.md).

## Runtime topology

DEV uses Azure services only. The API and React static assets run in Azure Container Apps; a separate EventWorker Container App consumes Azure Event Hubs. PostgreSQL Flexible Server stores candidate outbox/history and canonical schemas. Azure Blob Storage holds Event Hubs processor checkpoints; Azure Managed Redis stores rebuildable candidate cursors; Azure SignalR sends cursor-only notifications. Microsoft Entra ID handles user tokens and workload identity. The candidate AI Analyst API can call an allowlisted agent in the existing Microsoft Foundry project with bounded selected-match source evidence.

The candidate feed is read from `data/simulations/manu-liv-match-dataset.json` packaged in the application image and published in source order to Event Hubs. EventWorker stages each raw envelope or poison record in the PostgreSQL candidate outbox before advancing the Blob checkpoint. The API dispatcher persists candidate event history and updates the Redis cursor; SignalR carries a cursor hint, and the UI retrieves facts from API event pages. Candidate records are not canonical match facts. The candidate analyst response is a hypothesis; the code does not establish an automatic durable hot-event reporting workflow.

## Azure resources and identity boundaries

`infra/private-dev-topology.bicep` declares the guarded private DEV topology and conditional candidate-feed components. Depending on parameters and existing resource IDs, the path can configure/reuse PostgreSQL Flexible Server and Azure Managed Redis, and declares Azure Container Apps, a Standard Event Hubs namespace with one throughput unit, the candidate event hub and consumer group, a Blob checkpoint container/private endpoint, Azure SignalR, managed identities and scoped role assignments. The candidate hub is separate from projection telemetry. The API and worker receive `Runtime:Profile=azure`; the Bicep module itself defaults candidate provisioning off, while the release template parameter defaults it on inside the separately gated release path.

- API identity: Event Hubs sender, PostgreSQL candidate/read access, Redis data access, Azure SignalR server access, and Foundry project-agent invocation as declared by the template.
- EventWorker identity: Event Hubs receiver, Blob checkpoint access, and restricted PostgreSQL candidate-outbox access.
- Migration identity: applies schema changes separately from runtime principals. Runtime identities must not own schema objects.
- Browser identity: Entra access token validated by API issuer, audience, expiry and role policy. A service connection is not a user identity and never belongs in browser configuration.

Role assignment declarations do not prove that the principal has the effective role or can reach the resource. Validate VNet/subnet delegation, private endpoints, private DNS, routing, service firewall, identity object IDs, PostgreSQL principal/grants, Redis access policy, SignalR endpoint and Foundry role before claiming runtime readiness.

## CI/CD and deployment controls

Azure DevOps owns delivery. CI validates code, tests, frontend, pipeline contracts and Bicep without applying infrastructure. The release parameter `enableDevDelivery` is default-off. When explicitly enabled through the approved pipeline, the release builds one immutable image digest, validates the topology, captures what-if and estimated cost, waits for exact-change owner approval and the protected `pitchmind-dev` environment, applies the approved plan, then runs post-deployment smoke checks. A green CI run alone does not deploy.

The 2026-10-26 release log showed that `pm-dev-eh` was absent from the target resource group, so the prior existing-resource declaration failed when creating the candidate hub. The template now provisions the namespace when candidate Event Hubs are enabled. The EventWorker has no ingress and must stay active to consume Event Hubs, so its scale is pinned to one replica instead of allowing the default zero-replica state with no wake-up path. These changes add a billable Standard namespace and always-on Container Apps compute; review and update the monthly estimate before approval. They are source changes only: they did not provision Azure resources, apply Bicep, alter environment settings, or deploy an image. Do not deploy from a local shell. Any deployment requires review of the exact what-if, image digest, cost and rollback before the protected approval gates.

## Verification status and remaining evidence

Owner-provided DEV screenshots dated 2026-10-20 show builds labelled 68/69, with candidate source events and player positions rendered. A separate captured network response shows candidate status failing with `401` and `audience (null)`. The screenshots do not prove the Event Hubs → EventWorker → PostgreSQL → Redis → SignalR chain or a successful Foundry response. The last repository-recorded Azure resource inventory is dated 2026-10-19 and must be treated as historical; a current inventory and runtime probe are still required.

Before calling DEV end to end, retain evidence for each of the following:

1. Current Azure Resource Graph inventory and active ACA revisions/image digest.
2. API and worker app settings, attached identities, role assignments, VNet and private DNS.
3. PostgreSQL migration job status, schema version and narrowly scoped runtime grants.
4. Authenticated Event Hubs publish/consume and Blob checkpoint advance for a source event.
5. PostgreSQL candidate history, Redis cursor update, SignalR negotiate/notification and UI page refresh for the same match/sequence.
6. Entra browser login and API audience/role checks for the intended user.
7. Successful live Foundry request and response validation, if generated explanations are in scope.
8. Health/readiness and rollback evidence from the immutable deployed revision.

See [C4 deployment view](../architecture/04-deployment.md), [Azure HLD](../architecture/06-target-azure-hld.md), [API contract](../api/API.md), and [delivery pipeline guide](../../pipelines/README.md).
