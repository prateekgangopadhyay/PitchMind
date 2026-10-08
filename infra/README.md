# Bicep infrastructure

## Status
Modular starter skeleton. Not deployed or validated against an Azure subscription. Optional advanced services are intentionally omitted until justified.

## Workflow
1. Install Azure CLI and Bicep.
2. `az bicep build --file infra/main.bicep`
3. `az deployment group what-if -g <resource-group> -f infra/main.bicep -p infra/environments/dev.bicepparam`
4. Review network, private DNS, identity, provider registration, pricing and SKU availability before deployment.
5. Deploy only through protected Azure Pipelines stages.

No credentials belong in parameter files. Network and service private access must be added before production use.
