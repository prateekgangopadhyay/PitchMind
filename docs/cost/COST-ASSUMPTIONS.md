# DEV cost notes

This is a resource-based cost map, not a quote or invoice. The checked inventory is dated 2026-10-28. Actual cost depends on region, uptime, consumption, retention, discounts, free grants, and the subscription billing scope. Use Azure Cost Management for actual charges and the [Azure Pricing Calculator](https://azure.microsoft.com/pricing/calculator/) before approving a topology change.

## Current resources likely to incur charges

| Resource/service | Billing shape | Cost-control note |
| --- | --- | --- |
| PostgreSQL Flexible Server `pitchmind-postgresql-dev` | Provisioned compute, storage, and backups | Stopping the server can stop compute charges; storage/backup data can remain billable. |
| Event Hubs `pm-dev-eh` Standard | Throughput capacity and retention/usage | Standard is a paid tier; review throughput units and retention. |
| Azure Container Registry Basic | Registry tier and storage | Includes a tier allowance; excess storage/transfer can add cost. |
| Storage account | Stored capacity, transactions, and redundancy | Standard LRS is observed; operations and stored bytes are metered. |
| Azure AI Foundry model calls | Model/token or inference consumption | A resource/project can exist without calls; inference is usage-metered. |
| Azure AI Search Serverless | Search compute and index storage | Serverless is not equivalent to a free persistent service. Check if the app actually queries it. |
| Private endpoint and private DNS | Endpoint-hours/data processing; zone/query charges | Count endpoints and private DNS zones/queries when estimating changes. |
| Key Vault | Secret/key operation-based billing | Estimate operation volume rather than treating provisioned vault as unlimited free storage. |
| Redis Container App `pm-dev-redis-cache` | Container Apps Consumption CPU/memory while replica runs | Current min/max replicas are 1, so it stays allocated. It consumes the shared ACA free grant first, then may be billed. |
| EventWorker Container App | Container Apps Consumption CPU/memory while replica runs | The Bicep worker scale is pinned to one replica for continuous Event Hubs consumption; include it when estimating deployed usage. |
| Log Analytics `pm-dev-logs` | Ingestion and retention | The free ingestion allowance is billing-scope dependent; excess ingestion/retention may be charged. |

## Free or limited-free resources

- Azure SignalR `pm-dev-signalr` is currently Free F1 and subject to its connection/message limits.
- Container Apps Consumption has a subscription-level monthly free grant shared across qualifying apps. It does not make each app separately free, and the Redis/EventWorker replicas consume usage.
- Managed identities and the VNet itself have no direct resource-hour charge. Network features and transferred data may still incur costs.

Free grants, quotas, and prices can change. Read current regional pricing before approval.

## Optional services and changes

- `provisionRedis=true` changes DEV from the current Redis Container App to a separate Azure Managed Redis resource/private endpoint and changes the cost profile. It is false by default in Bicep.
- Service Bus Standard is present in the DEV resource group but is not used by the candidate feed path described in the current architecture. Review whether it is needed before retaining or provisioning it.
- Azure AI Search is present, but resource existence does not establish application use. Do not budget expected search usage without confirming its endpoint/index configuration and call path.
- VPN Gateway was not present in the 2026-10-28 inventory; do not include a current VPN Gateway charge based solely on an earlier setup discussion.

For free-tier details and limits, see the official [Container Apps](https://azure.microsoft.com/pricing/details/container-apps/), [SignalR](https://azure.microsoft.com/pricing/details/signalr-service/), [PostgreSQL](https://azure.microsoft.com/pricing/details/postgresql/flexible-server/), and [Event Hubs](https://azure.microsoft.com/pricing/details/event-hubs/) pricing pages.
