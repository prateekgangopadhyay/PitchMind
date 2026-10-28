# PitchMind Azure infrastructure

The Bicep files describe deployment options; they do not prove a resource exists, a pipeline applied it, or the app can reach it. Current DEV inventory and runtime limitations are recorded in [Azure DEV integration status](../docs/engineering/AZURE-DEV-LIVE-INTEGRATION-STATUS.md).

## Templates

- `main.bicep`, `canonical-dev-topology.bicep`, and `demo-dev.bicep` describe other canonical or demo topologies. Check each template's scope, parameters, and defaults before using it.
- `private-dev-topology.bicep` is the guarded DEV release topology. `deployPrivateDevTopology` and `enableCandidateFeed` default to false in the module; the pipeline can pass true after its gates.
- The topology targets `pm-dev-vnet`, adds/uses a delegated Container Apps subnet, and references the existing `private-endpoints` subnet. It can reuse the existing PostgreSQL server by default.
- `provisionRedis=false` (default) declares the Redis Container App `pm-dev-redis-cache`. `provisionRedis=true` instead opts into an Azure Managed Redis instance and private endpoint. Current DEV inventory reports the Container App, not Azure Managed Redis.
- Candidate feed resources include Event Hubs, a candidate hub and consumer group, Blob checkpoint storage, SignalR, app/worker identities and scoped role assignments. Template declarations do not establish successful data-plane access.

## Validate and release

Build the relevant Bicep file and parameter files with Azure CLI/Bicep. Use `scripts/validate_iac_dependency_contract.py` to check the repository resource-dependency contract. Neither static check calls Azure nor verifies cloud connectivity.

The Azure DevOps pipeline currently defaults `enableDevDelivery=true` and `enableCandidateFeed=true`. A DEV change requires the configured service connections, exact what-if review, cost estimate, immutable image digest, and deployment Environment checks. Confirm those controls in Azure DevOps; they cannot be inferred from this repository. Follow [the pipeline guide](../pipelines/README.md) and [M03 runbook](../docs/engineering/M03-AZURE-DEV.md).

## Current deployment notes

- DEV uses Azure services; LOCAL uses separate Docker adapters.
- Current DEV Redis is a Redis container app on the Container Apps Consumption environment, with one minimum replica. This differs from the optional Managed Redis Bicep path.
- PostgreSQL Flexible Server, Event Hubs Standard, SignalR F1, Foundry, and other metered resources may incur charges. Review actual Azure Cost Management data before approval.
- Keep migrations on a separate privileged identity; runtime identities should receive only scoped data-plane roles.
