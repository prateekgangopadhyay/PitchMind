#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# This script does not deploy. Requires authenticated Azure CLI and an existing resource group.
if [[ ${1:-} != "--resource-group" || -z ${2:-} ]]; then
  echo "Usage: $0 --resource-group <existing-dev-resource-group>" >&2
  exit 2
fi
rg="$2"
if ! command -v az >/dev/null 2>&1; then
  echo "Azure CLI is required; see docs/engineering/M03-AZURE-DEV.md" >&2
  exit 1
fi
az account show --query '{subscription:id,tenant:tenantId}' --output json
az bicep build --file infra/main.bicep --stdout >/dev/null
az bicep build-params --file infra/environments/dev.bicepparam --stdout >/dev/null
az deployment group what-if --resource-group "$rg" --template-file infra/main.bicep --parameters infra/environments/dev.bicepparam --result-format FullResourcePayloads
