#!/usr/bin/env bash
set -euo pipefail

required=(
  PITCHMIND_DEV_RESOURCE_GROUP PITCHMIND_DEV_REGISTRY_NAME PITCHMIND_DEV_VNET_NAME
  PITCHMIND_DEV_ACA_SUBNET_NAME PITCHMIND_DEV_ACA_SUBNET_CIDR
  PITCHMIND_DEV_POSTGRES_SUBNET_NAME PITCHMIND_DEV_POSTGRES_SUBNET_CIDR
  PITCHMIND_DEV_REDIS_PE_SUBNET_NAME PITCHMIND_DEV_PROVISION_REDIS
  PITCHMIND_DEV_REDIS_NAME PITCHMIND_DEV_REDIS_SKU_NAME
  PITCHMIND_DEV_REDIS_DNS_ZONE_NAME PITCHMIND_DEV_REDIS_PE_NAME
  PITCHMIND_DEV_POSTGRES_DNS_ZONE_NAME PITCHMIND_DEV_APP_NAME PITCHMIND_DEV_ENVIRONMENT_NAME
  PITCHMIND_DEV_PROVISION_POSTGRES PITCHMIND_DEV_POSTGRES_SERVER_NAME
  PITCHMIND_RELEASE_MANIFEST PITCHMIND_BUILD_ID
  PITCHMIND_APPROVED_PLAN
)
for key in "${required[@]}"; do
  [[ -n "${!key:-}" ]] || { echo "Missing required input: $key" >&2; exit 2; }
done
manifest_image=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["image"])' "$PITCHMIND_RELEASE_MANIFEST")
[[ "$manifest_image" == *@sha256:* ]] || { echo 'Release manifest must contain an immutable sha256 image digest' >&2; exit 2; }

vnet_json=$(az network vnet show --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" --name "$PITCHMIND_DEV_VNET_NAME" --output json)
subnets_json=$(az network vnet subnet list --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" --vnet-name "$PITCHMIND_DEV_VNET_NAME" --output json)
location=$(az group show --name "$PITCHMIND_DEV_RESOURCE_GROUP" --query location --output tsv)
export PITCHMIND_DEV_VNET_JSON="$vnet_json" PITCHMIND_DEV_SUBNETS_JSON="$subnets_json" PITCHMIND_DEV_LOCATION="$location"
python3 scripts/validate_private_dev_network.py

current_plan=$(mktemp)
trap 'rm -f "$current_plan"' EXIT
az deployment group what-if \
  --name "pitchmind-private-dev-preapply-$PITCHMIND_BUILD_ID" \
  --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" \
  --template-file infra/private-dev-topology.bicep \
  --parameters deployPrivateDevTopology=true \
    provisionPostgres="$PITCHMIND_DEV_PROVISION_POSTGRES" \
    vnetName="$PITCHMIND_DEV_VNET_NAME" acaSubnetName="$PITCHMIND_DEV_ACA_SUBNET_NAME" \
    acaSubnetCidr="$PITCHMIND_DEV_ACA_SUBNET_CIDR" \
    postgresSubnetName="$PITCHMIND_DEV_POSTGRES_SUBNET_NAME" \
    postgresSubnetCidr="$PITCHMIND_DEV_POSTGRES_SUBNET_CIDR" \
    postgresDnsZoneName="$PITCHMIND_DEV_POSTGRES_DNS_ZONE_NAME" \
    registryName="$PITCHMIND_DEV_REGISTRY_NAME" appName="$PITCHMIND_DEV_APP_NAME" \
    environmentName="$PITCHMIND_DEV_ENVIRONMENT_NAME" appImage="$manifest_image" \
    postgresServerName="$PITCHMIND_DEV_POSTGRES_SERVER_NAME" \
    postgresSkuTier="${PITCHMIND_DEV_POSTGRES_SKU_TIER:-Burstable}" \
    postgresSkuName="${PITCHMIND_DEV_POSTGRES_SKU_NAME:-}" \
    postgresAdminObjectId="${PITCHMIND_DEV_POSTGRES_ADMIN_OBJECT_ID:-}" \
    postgresAdminPrincipalName="${PITCHMIND_DEV_POSTGRES_ADMIN_PRINCIPAL_NAME:-}" \
    postgresAdminPrincipalType="${PITCHMIND_DEV_POSTGRES_ADMIN_PRINCIPAL_TYPE:-User}" \
    provisionRedis="$PITCHMIND_DEV_PROVISION_REDIS" \
    redisName="$PITCHMIND_DEV_REDIS_NAME" redisSkuName="$PITCHMIND_DEV_REDIS_SKU_NAME" \
    redisPrivateEndpointSubnetName="$PITCHMIND_DEV_REDIS_PE_SUBNET_NAME" \
    redisPrivateDnsZoneName="$PITCHMIND_DEV_REDIS_DNS_ZONE_NAME" \
    redisPrivateEndpointName="$PITCHMIND_DEV_REDIS_PE_NAME" \
  --result-format FullResourcePayloads --output json > "$current_plan"
python3 scripts/validate_private_dev_whatif.py "$current_plan" "$PITCHMIND_APPROVED_PLAN"

deployment_name="pitchmind-private-dev-$PITCHMIND_BUILD_ID"
deployment=$(az deployment group create \
  --name "$deployment_name" \
  --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" \
  --mode Incremental \
  --template-file infra/private-dev-topology.bicep \
  --parameters deployPrivateDevTopology=true \
    provisionPostgres="$PITCHMIND_DEV_PROVISION_POSTGRES" \
    vnetName="$PITCHMIND_DEV_VNET_NAME" acaSubnetName="$PITCHMIND_DEV_ACA_SUBNET_NAME" \
    acaSubnetCidr="$PITCHMIND_DEV_ACA_SUBNET_CIDR" \
    postgresSubnetName="$PITCHMIND_DEV_POSTGRES_SUBNET_NAME" \
    postgresSubnetCidr="$PITCHMIND_DEV_POSTGRES_SUBNET_CIDR" \
    postgresDnsZoneName="$PITCHMIND_DEV_POSTGRES_DNS_ZONE_NAME" \
    registryName="$PITCHMIND_DEV_REGISTRY_NAME" appName="$PITCHMIND_DEV_APP_NAME" \
    environmentName="$PITCHMIND_DEV_ENVIRONMENT_NAME" appImage="$manifest_image" \
    postgresServerName="$PITCHMIND_DEV_POSTGRES_SERVER_NAME" \
    postgresSkuTier="${PITCHMIND_DEV_POSTGRES_SKU_TIER:-Burstable}" \
    postgresSkuName="${PITCHMIND_DEV_POSTGRES_SKU_NAME:-}" \
    postgresAdminObjectId="${PITCHMIND_DEV_POSTGRES_ADMIN_OBJECT_ID:-}" \
    postgresAdminPrincipalName="${PITCHMIND_DEV_POSTGRES_ADMIN_PRINCIPAL_NAME:-}" \
    postgresAdminPrincipalType="${PITCHMIND_DEV_POSTGRES_ADMIN_PRINCIPAL_TYPE:-User}" \
    provisionRedis="$PITCHMIND_DEV_PROVISION_REDIS" \
    redisName="$PITCHMIND_DEV_REDIS_NAME" redisSkuName="$PITCHMIND_DEV_REDIS_SKU_NAME" \
    redisPrivateEndpointSubnetName="$PITCHMIND_DEV_REDIS_PE_SUBNET_NAME" \
    redisPrivateDnsZoneName="$PITCHMIND_DEV_REDIS_DNS_ZONE_NAME" \
    redisPrivateEndpointName="$PITCHMIND_DEV_REDIS_PE_NAME" \
  --output json)
fqdn=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["properties"]["outputs"]["containerAppFqdn"]["value"])' <<< "$deployment")
test -n "$fqdn"
echo "##vso[task.setvariable variable=PITCHMIND_DEV_FQDN]$fqdn"
echo "Provisioned Container App FQDN: $fqdn"
