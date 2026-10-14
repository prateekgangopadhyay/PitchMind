using '../canonical-dev-topology.bicep'

// Non-deployable placeholders. Replace every scope/name/SKU/admin value and
// complete target-resource, network, DNS, security and cost preflight first.
// The topology remains disabled in this example.
param deployCanonicalTopology = false
param environment = 'dev'
param virtualNetworkCidr = '10.99.0.0/16'
param containerAppsSubnetCidr = '10.99.0.0/27'
param postgresSubnetCidr = '10.99.1.0/24'
param privateEndpointsSubnetCidr = '10.99.2.0/24'

param storageSubscriptionId = '00000000-0000-0000-0000-000000000000'
param storageResourceGroupName = 'REPLACE_WITH_STORAGE_RESOURCE_GROUP'
param storageAccountName = 'pitchmindstorageexample'

param keyVaultSubscriptionId = '00000000-0000-0000-0000-000000000000'
param keyVaultResourceGroupName = 'REPLACE_WITH_KEY_VAULT_RESOURCE_GROUP'
param keyVaultName = 'pitchmindkvexample'

param eventHubsSubscriptionId = '00000000-0000-0000-0000-000000000000'
param eventHubsResourceGroupName = 'REPLACE_WITH_EVENT_HUBS_RESOURCE_GROUP'
param eventHubsNamespaceName = 'pitchmindhubsample'

param postgresSkuName = 'REPLACE_WITH_REGION_SUPPORTED_SKU'
param postgresSkuTier = 'Burstable'
param postgresAdminObjectId = '00000000-0000-0000-0000-000000000000'
param postgresAdminPrincipalName = 'REPLACE_WITH_ENTRA_ADMIN'
param postgresAdminPrincipalType = 'User'
