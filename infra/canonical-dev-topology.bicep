targetScope = 'resourceGroup'

@description('Must remain false outside an explicitly approved, reviewed deployment change.')
param deployCanonicalTopology bool = false

@allowed(['dev'])
param environment string = 'dev'

param location string = resourceGroup().location
@description('Name prefix for the isolated canonical DEV topology.')
@minLength(3)
@maxLength(24)
param resourcePrefix string = 'pm-dev-canonical'

@description('Address space must be checked for overlap with every connected network before deployment.')
param virtualNetworkCidr string
@description('Dedicated subnet for the default workload-profiles ACA environment. Use /27 or larger and check connected/reserved ranges before any deployment.')
param containerAppsSubnetCidr string
param postgresSubnetCidr string
param privateEndpointsSubnetCidr string

@description('Subscription ID containing the existing Storage account. The account and its network policy must be reviewed before this candidate is used.')
@minLength(36)
@maxLength(36)
param storageSubscriptionId string
@description('Resource group containing the existing Storage account.')
@minLength(1)
@maxLength(90)
param storageResourceGroupName string
@description('Name of the existing Storage account to connect for Blob feed artifacts.')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Subscription ID containing the existing Key Vault. The vault and its network policy must be reviewed before this candidate is used.')
@minLength(36)
@maxLength(36)
param keyVaultSubscriptionId string
@description('Resource group containing the existing Key Vault.')
@minLength(1)
@maxLength(90)
param keyVaultResourceGroupName string
@description('Name of the existing Key Vault used by the canonical workload.')
@minLength(3)
@maxLength(24)
param keyVaultName string

@description('Subscription ID containing the existing Event Hubs namespace. The namespace and its network policy must be reviewed before this candidate is used.')
@minLength(36)
@maxLength(36)
param eventHubsSubscriptionId string
@description('Resource group containing the existing Event Hubs namespace.')
@minLength(1)
@maxLength(90)
param eventHubsResourceGroupName string
@description('Name of the existing Event Hubs namespace used by the telemetry workload.')
@minLength(6)
@maxLength(50)
param eventHubsNamespaceName string

@description('Existing Log Analytics workspace created by infra/main.bicep.')
param logAnalyticsWorkspaceName string = 'pm-dev-logs'

@description('Select only after dated region capacity and FinOps review. No resource is deployed by default.')
param postgresSkuName string
@allowed(['Burstable', 'GeneralPurpose', 'MemoryOptimized'])
param postgresSkuTier string
@allowed(['16', '17'])
param postgresVersion string = '16'
@minValue(32)
@maxValue(16384)
param postgresStorageSizeGB int = 32
@minValue(7)
@maxValue(35)
param postgresBackupRetentionDays int = 7

@description('Microsoft Entra server administrator. Runtime and migration identities are separate follow-up work.')
param postgresAdminObjectId string
param postgresAdminPrincipalName string
@allowed(['User', 'Group', 'ServicePrincipal'])
param postgresAdminPrincipalType string

var suffix = uniqueString(resourceGroup().id)
var vnetName = '${resourcePrefix}-vnet'
var postgresName = replace('${resourcePrefix}-${suffix}-pg', '_', '-')
var postgresDnsZoneName = '${resourcePrefix}-${suffix}.postgres.database.azure.com'
var containerAppsEnvironmentName = '${resourcePrefix}-cae'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: logAnalyticsWorkspaceName
}

// Existing resources are declared with fixed Azure resource types so callers
// provide scope/name inputs instead of opaque, unchecked resource IDs. This
// does not verify resource existence or disable the target's public access.
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
  scope: resourceGroup(storageSubscriptionId, storageResourceGroupName)
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
  scope: resourceGroup(keyVaultSubscriptionId, keyVaultResourceGroupName)
}

resource eventHubsNamespace 'Microsoft.EventHub/namespaces@2024-01-01' existing = {
  name: eventHubsNamespaceName
  scope: resourceGroup(eventHubsSubscriptionId, eventHubsResourceGroupName)
}

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2024-01-01' = if (deployCanonicalTopology) {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [virtualNetworkCidr]
    }
    subnets: [
      {
        name: 'container-apps'
        properties: {
          addressPrefix: containerAppsSubnetCidr
          delegations: [
            {
              name: 'container-apps-environment'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'postgres'
        properties: {
          addressPrefix: postgresSubnetCidr
          delegations: [
            {
              name: 'postgres-flexible-server'
              properties: {
                serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: privateEndpointsSubnetCidr
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' existing = if (deployCanonicalTopology) {
  parent: virtualNetwork
  name: 'postgres'
}

resource containerAppsSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' existing = if (deployCanonicalTopology) {
  parent: virtualNetwork
  name: 'container-apps'
}

resource privateEndpointsSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' existing = if (deployCanonicalTopology) {
  parent: virtualNetwork
  name: 'private-endpoints'
}

resource postgresDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (deployCanonicalTopology) {
  name: postgresDnsZoneName
  location: 'global'
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (deployCanonicalTopology) {
  parent: postgresDnsZone
  name: '${resourcePrefix}-vnet-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: virtualNetwork.id
    }
  }
}

resource blobPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (deployCanonicalTopology) {
  // Derive the Blob Private Link suffix from the active Azure environment; sovereign-cloud behavior remains unvalidated.
  name: 'privatelink.blob.${az.environment().suffixes.storage}'
  location: 'global'
}

resource blobPrivateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (deployCanonicalTopology) {
  parent: blobPrivateDnsZone
  name: '${resourcePrefix}-vnet-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: virtualNetwork.id }
  }
}

resource keyVaultPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (deployCanonicalTopology) {
  name: 'privatelink.vaultcore.azure.net'
  location: 'global'
}

resource keyVaultPrivateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (deployCanonicalTopology) {
  parent: keyVaultPrivateDnsZone
  name: '${resourcePrefix}-vnet-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: virtualNetwork.id }
  }
}

resource eventHubsPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (deployCanonicalTopology) {
  name: 'privatelink.servicebus.windows.net'
  location: 'global'
}

resource eventHubsPrivateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (deployCanonicalTopology) {
  parent: eventHubsPrivateDnsZone
  name: '${resourcePrefix}-vnet-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: virtualNetwork.id }
  }
}

resource storagePrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = if (deployCanonicalTopology) {
  name: '${resourcePrefix}-blob-pe'
  location: location
  properties: {
    subnet: { id: privateEndpointsSubnet.id }
    privateLinkServiceConnections: [
      {
        name: 'storage-blob'
        properties: {
          privateLinkServiceId: storageAccount.id
          groupIds: ['blob']
        }
      }
    ]
  }
  tags: {
    application: 'PitchMind'
    environment: environment
    story: 'EP02-S01'
    service: 'storage-blob'
  }
}

resource storagePrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = if (deployCanonicalTopology) {
  parent: storagePrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'blob'
        properties: { privateDnsZoneId: blobPrivateDnsZone.id }
      }
    ]
  }
}

resource keyVaultPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = if (deployCanonicalTopology) {
  name: '${resourcePrefix}-key-vault-pe'
  location: location
  properties: {
    subnet: { id: privateEndpointsSubnet.id }
    privateLinkServiceConnections: [
      {
        name: 'key-vault'
        properties: {
          privateLinkServiceId: keyVault.id
          groupIds: ['vault']
        }
      }
    ]
  }
  tags: {
    application: 'PitchMind'
    environment: environment
    story: 'EP02-S01'
    service: 'key-vault'
  }
}

resource keyVaultPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = if (deployCanonicalTopology) {
  parent: keyVaultPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'key-vault'
        properties: { privateDnsZoneId: keyVaultPrivateDnsZone.id }
      }
    ]
  }
}

resource eventHubsPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = if (deployCanonicalTopology) {
  name: '${resourcePrefix}-event-hubs-pe'
  location: location
  properties: {
    subnet: { id: privateEndpointsSubnet.id }
    privateLinkServiceConnections: [
      {
        name: 'event-hubs'
        properties: {
          privateLinkServiceId: eventHubsNamespace.id
          groupIds: ['namespace']
        }
      }
    ]
  }
  tags: {
    application: 'PitchMind'
    environment: environment
    story: 'EP02-S01'
    service: 'event-hubs'
  }
}

resource eventHubsPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = if (deployCanonicalTopology) {
  parent: eventHubsPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'namespace'
        properties: { privateDnsZoneId: eventHubsPrivateDnsZone.id }
      }
    ]
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = if (deployCanonicalTopology) {
  name: postgresName
  location: location
  sku: {
    name: postgresSkuName
    tier: postgresSkuTier
  }
  properties: {
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Enabled'
      tenantId: tenant().tenantId
    }
    backup: {
      backupRetentionDays: postgresBackupRetentionDays
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      delegatedSubnetResourceId: postgresSubnet.id
      privateDnsZoneArmResourceId: postgresDnsZone.id
      publicNetworkAccess: 'Disabled'
    }
    storage: {
      storageSizeGB: postgresStorageSizeGB
      autoGrow: 'Disabled'
    }
    version: postgresVersion
  }
  tags: {
    application: 'PitchMind'
    environment: environment
    story: 'EP02-S01'
    topology: 'canonical-private'
  }
  dependsOn: [postgresDnsLink]
}

resource postgresAdministrator 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = if (deployCanonicalTopology) {
  parent: postgres
  name: postgresAdminObjectId
  properties: {
    principalName: postgresAdminPrincipalName
    principalType: postgresAdminPrincipalType
    tenantId: tenant().tenantId
  }
}

resource pitchMindDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = if (deployCanonicalTopology) {
  parent: postgres
  name: 'pitchmind'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource canonicalContainerAppsEnvironment 'Microsoft.App/managedEnvironments@2025-07-01' = if (deployCanonicalTopology) {
  name: containerAppsEnvironmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: containerAppsSubnet.id
      internal: true
    }
    publicNetworkAccess: 'Disabled'
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
  tags: {
    application: 'PitchMind'
    environment: environment
    story: 'EP02-S01'
    topology: 'canonical-private'
  }
}

output canonicalTopologyEnabled bool = deployCanonicalTopology
output postgresServerId string = deployCanonicalTopology ? postgres!.id : ''
output postgresServerName string = deployCanonicalTopology ? postgres!.name : ''
output postgresFqdn string = deployCanonicalTopology ? postgres!.properties.fullyQualifiedDomainName : ''
output containerAppsEnvironmentId string = deployCanonicalTopology ? canonicalContainerAppsEnvironment!.id : ''
output storagePrivateEndpointId string = deployCanonicalTopology ? storagePrivateEndpoint!.id : ''
output keyVaultPrivateEndpointId string = deployCanonicalTopology ? keyVaultPrivateEndpoint!.id : ''
output eventHubsPrivateEndpointId string = deployCanonicalTopology ? eventHubsPrivateEndpoint!.id : ''
