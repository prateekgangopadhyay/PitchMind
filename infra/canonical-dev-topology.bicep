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
      passwordAuth: 'Disabled'
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

resource canonicalContainerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = if (deployCanonicalTopology) {
  name: containerAppsEnvironmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: listKeys(logAnalytics.id, '2022-10-01').primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: containerAppsSubnet.id
      internal: true
    }
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
