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
  PITCHMIND_DEV_ESTIMATED_MONTHLY_COST PITCHMIND_RELEASE_MANIFEST PITCHMIND_PLAN_DIRECTORY
  PITCHMIND_BUILD_ID
)
for key in "${required[@]}"; do
  [[ -n "${!key:-}" ]] || { echo "Missing required input: $key" >&2; exit 2; }
done

manifest_image=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["image"])' "$PITCHMIND_RELEASE_MANIFEST")
manifest_commit=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["commit"])' "$PITCHMIND_RELEASE_MANIFEST")
[[ "$manifest_image" == *@sha256:* ]] || { echo 'Release manifest must contain an immutable sha256 image digest' >&2; exit 2; }
[[ "$PITCHMIND_DEV_ESTIMATED_MONTHLY_COST" != 'UNESTIMATED' ]] || { echo 'Provide a FinOps-reviewed monthly estimate before planning' >&2; exit 2; }
export PITCHMIND_DEV_RELEASE_COMMIT="$manifest_commit" PITCHMIND_DEV_RELEASE_IMAGE="$manifest_image"

vnet_json=$(az network vnet show --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" --name "$PITCHMIND_DEV_VNET_NAME" --output json)
subnets_json=$(az network vnet subnet list --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" --vnet-name "$PITCHMIND_DEV_VNET_NAME" --output json)
location=$(az group show --name "$PITCHMIND_DEV_RESOURCE_GROUP" --query location --output tsv)
export PITCHMIND_DEV_VNET_JSON="$vnet_json" PITCHMIND_DEV_SUBNETS_JSON="$subnets_json" PITCHMIND_DEV_LOCATION="$location"
python3 scripts/validate_private_dev_network.py

if [[ "$PITCHMIND_DEV_ENABLE_CANDIDATE_FEED" == 'True' || "$PITCHMIND_DEV_ENABLE_CANDIDATE_FEED" == 'true' ]]; then
  signalr_provider_state=$(az provider show --namespace Microsoft.SignalRService --query registrationState --output tsv)
  if [[ "$signalr_provider_state" != 'Registered' ]]; then
    echo "Microsoft.SignalRService is $signalr_provider_state. Register the resource provider in the subscription before reviewing or applying the DEV plan." >&2
    exit 2
  fi
fi

if [[ "$PITCHMIND_DEV_PROVISION_POSTGRES" == 'True' || "$PITCHMIND_DEV_PROVISION_POSTGRES" == 'true' ]]; then
  required_pg=(PITCHMIND_DEV_POSTGRES_SKU_TIER PITCHMIND_DEV_POSTGRES_SKU_NAME PITCHMIND_DEV_POSTGRES_ADMIN_OBJECT_ID PITCHMIND_DEV_POSTGRES_ADMIN_PRINCIPAL_NAME PITCHMIND_DEV_POSTGRES_ADMIN_PRINCIPAL_TYPE)
  for key in "${required_pg[@]}"; do
    [[ -n "${!key:-}" ]] || { echo "Missing PostgreSQL provisioning input: $key" >&2; exit 2; }
  done
  postgres_servers=$(az postgres flexible-server list --resource-group "$PITCHMIND_DEV_RESOURCE_GROUP" --output json)
  if python3 -c 'import json,sys; name=sys.argv[1].casefold(); raise SystemExit(0 if any(item.get("name", "").casefold() == name for item in json.load(sys.stdin)) else 1)' \
      "$PITCHMIND_DEV_POSTGRES_SERVER_NAME" <<< "$postgres_servers"
  then
    echo 'Refusing to overwrite an existing PostgreSQL server; use a new explicitly reviewed server name or manage the existing server separately.' >&2
    exit 2
  fi
fi

mkdir -p "$PITCHMIND_PLAN_DIRECTORY"
az deployment group what-if \
  --name "pitchmind-private-dev-plan-$PITCHMIND_BUILD_ID" \
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
  --no-pretty-print --result-format FullResourcePayloads --output json \
  > "$PITCHMIND_PLAN_DIRECTORY/what-if.json"

python3 scripts/validate_private_dev_whatif.py "$PITCHMIND_PLAN_DIRECTORY/what-if.json"
python3 - "$PITCHMIND_PLAN_DIRECTORY/plan-summary.json" <<'PY'
import json, os, sys
from pathlib import Path
Path(sys.argv[1]).write_text(json.dumps({
    'commit': os.environ['PITCHMIND_DEV_RELEASE_COMMIT'],
    'image': os.environ['PITCHMIND_DEV_RELEASE_IMAGE'],
    'resourceGroup': os.environ['PITCHMIND_DEV_RESOURCE_GROUP'],
    'vnet': os.environ['PITCHMIND_DEV_VNET_NAME'],
    'app': os.environ['PITCHMIND_DEV_APP_NAME'],
    'estimatedMonthlyCost': os.environ['PITCHMIND_DEV_ESTIMATED_MONTHLY_COST'],
    'provisionPostgres': os.environ['PITCHMIND_DEV_PROVISION_POSTGRES'],
    'provisionRedis': os.environ['PITCHMIND_DEV_PROVISION_REDIS'],
    'enableCandidateFeed': os.environ['PITCHMIND_DEV_ENABLE_CANDIDATE_FEED'],
    'redisSkuName': os.environ['PITCHMIND_DEV_REDIS_SKU_NAME'] if os.environ['PITCHMIND_DEV_PROVISION_REDIS'].lower() == 'true' else None,
}, indent=2) + '\n')
PY
echo "##vso[task.uploadsummary]$PITCHMIND_PLAN_DIRECTORY/plan-summary.json"
