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
  PITCHMIND_DEV_ENABLE_CANDIDATE_FEED PITCHMIND_DEV_SIGNALR_NAME PITCHMIND_DEV_SIGNALR_ALLOWED_ORIGIN PITCHMIND_DEV_STORAGE_ACCOUNT_NAME
  PITCHMIND_DEV_CANDIDATE_CHECKPOINT_CONTAINER
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

if [[ "$PITCHMIND_DEV_ENABLE_CANDIDATE_FEED" == 'True' || "$PITCHMIND_DEV_ENABLE_CANDIDATE_FEED" == 'true' ]]; then
  signalr_provider_state=$(az provider show --namespace Microsoft.SignalRService --query registrationState --output tsv)
  if [[ "$signalr_provider_state" != 'Registered' ]]; then
    echo "Microsoft.SignalRService is $signalr_provider_state. Register the resource provider in the subscription before applying the DEV plan." >&2
    exit 2
  fi
fi

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
    enableCandidateFeed="$PITCHMIND_DEV_ENABLE_CANDIDATE_FEED" \
    signalRName="$PITCHMIND_DEV_SIGNALR_NAME" \
    signalRAllowedOrigin="$PITCHMIND_DEV_SIGNALR_ALLOWED_ORIGIN" \
    storageAccountName="$PITCHMIND_DEV_STORAGE_ACCOUNT_NAME" \
    candidateCheckpointContainer="$PITCHMIND_DEV_CANDIDATE_CHECKPOINT_CONTAINER" \
    redisName="$PITCHMIND_DEV_REDIS_NAME" redisSkuName="$PITCHMIND_DEV_REDIS_SKU_NAME" \
    redisPrivateEndpointSubnetName="$PITCHMIND_DEV_REDIS_PE_SUBNET_NAME" \
    redisPrivateDnsZoneName="$PITCHMIND_DEV_REDIS_DNS_ZONE_NAME" \
    redisPrivateEndpointName="$PITCHMIND_DEV_REDIS_PE_NAME" \
  --no-pretty-print --result-format FullResourcePayloads --output json > "$current_plan"
python3 scripts/validate_private_dev_whatif.py "$current_plan" "$PITCHMIND_APPROVED_PLAN"

deployment_name="pitchmind-private-dev-$PITCHMIND_BUILD_ID"
migrator_object_id="${PITCHMIND_DEV_POSTGRES_MIGRATOR_OBJECT_ID:-dceb4835-bf2e-4794-88b8-cc9c23643c60}"
cleanup_migration_admin() {
  local status=$?
  trap - EXIT
  if [[ -n "$migrator_object_id" ]]; then
    if ! az postgres flexible-server microsoft-entra-admin delete \
      --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" \
      --server-name "$PITCHMIND_DEV_POSTGRES_SERVER_NAME" \
      --object-id "$migrator_object_id" \
      --yes --only-show-errors; then
      echo "Could not remove the temporary PostgreSQL migration administrator mapping" >&2
      [[ "$status" -ne 0 ]] || status=1
    fi
  fi
  exit "$status"
}
# The ARM deployment may fail after adding the temporary administrator, so
# schedule cleanup before starting any Azure mutation.
trap cleanup_migration_admin EXIT
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
    enableCandidateFeed="$PITCHMIND_DEV_ENABLE_CANDIDATE_FEED" \
    signalRName="$PITCHMIND_DEV_SIGNALR_NAME" \
    signalRAllowedOrigin="$PITCHMIND_DEV_SIGNALR_ALLOWED_ORIGIN" \
    storageAccountName="$PITCHMIND_DEV_STORAGE_ACCOUNT_NAME" \
    candidateCheckpointContainer="$PITCHMIND_DEV_CANDIDATE_CHECKPOINT_CONTAINER" \
    redisName="$PITCHMIND_DEV_REDIS_NAME" redisSkuName="$PITCHMIND_DEV_REDIS_SKU_NAME" \
    redisPrivateEndpointSubnetName="$PITCHMIND_DEV_REDIS_PE_SUBNET_NAME" \
    redisPrivateDnsZoneName="$PITCHMIND_DEV_REDIS_DNS_ZONE_NAME" \
    redisPrivateEndpointName="$PITCHMIND_DEV_REDIS_PE_NAME" \
  --output json)
fqdn=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["properties"]["outputs"]["containerAppFqdn"]["value"])' <<< "$deployment")
database_job=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["properties"]["outputs"]["postgresMigrationJobName"]["value"])' <<< "$deployment")
migrator_object_id=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["properties"]["outputs"]["postgresMigrationAdminObjectId"]["value"])' <<< "$deployment")
test -n "$fqdn"
test -n "$database_job"
test -n "$migrator_object_id"

execution_name=$(az containerapp job start \
  --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" \
  --name "$database_job" \
  --query name --output tsv)
test -n "$execution_name"
echo "Started private PostgreSQL migration execution: $execution_name"
migration_status=''
for attempt in $(seq 1 90); do
  migration_status=$(az containerapp job execution show \
    --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" \
    --name "$database_job" \
    --job-execution-name "$execution_name" \
    --query properties.status --output tsv)
  case "$migration_status" in
    Succeeded) break ;;
    Failed|Stopped|Degraded)
      echo "PostgreSQL schema migration job finished with status: $migration_status" >&2
      exit 1
      ;;
  esac
  sleep 10
done
[[ "$migration_status" == Succeeded ]] || {
  echo "Timed out waiting for PostgreSQL schema migration; last status: $migration_status" >&2
  exit 1
}

# Cleanup runs on success, migration failure, timeout, and deployment errors.
# Keep the identity attached to the job for the next explicitly gated release.
az postgres flexible-server microsoft-entra-admin delete \
  --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" \
  --server-name "$PITCHMIND_DEV_POSTGRES_SERVER_NAME" \
  --object-id "$migrator_object_id" \
  --yes --only-show-errors
migrator_object_id=''
trap - EXIT

echo "##vso[task.setvariable variable=PITCHMIND_DEV_FQDN]$fqdn"
echo "Provisioned Container App FQDN: $fqdn"
