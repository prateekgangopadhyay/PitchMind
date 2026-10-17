targetScope = 'resourceGroup'

@description('Must remain false unless the repository owner approves the exact DEV plan and immutable image digest.')
param deployPrivateDevTopology bool = false

@description('Deploy a new Entra-only PostgreSQL Flexible Server in the delegated subnet. Leave false when PostgreSQL is managed separately.')
param provisionPostgres bool = false

param location string = resourceGroup().location
param vnetName string = 'pm-dev-vnet'
param acaSubnetName string = 'container-apps'
@description('Must be a free, non-overlapping range in the existing VNet. Workload profiles require /27 or larger.')
param acaSubnetCidr string
param postgresSubnetName string = 'postgres'
param postgresSubnetCidr string = '10.42.1.0/24'
param postgresDnsZoneName string = 'pm-dev-private.postgres.database.azure.com'
param logAnalyticsWorkspaceName string = 'pm-dev-logs'
param registryName string = 'pmdev4iwntkatliy6sacr'
param appName string = 'pm-dev-api-vnet'
param environmentName string = 'pm-dev-private-cae'
param appImage string

param postgresServerName string = 'pitchmind-postgresql-dev'
@allowed(['Burstable', 'GeneralPurpose', 'MemoryOptimized'])
param postgresSkuTier string = 'Burstable'
@description('Choose only after dated region availability and cost review. Required when provisionPostgres is true.')
param postgresSkuName string = ''
@allowed(['16', '17'])
param postgresVersion string = '16'
@minValue(32)
@maxValue(16384)
param postgresStorageSizeGB int = 32
@minValue(7)
@maxValue(35)
param postgresBackupRetentionDays int = 7
param postgresAdminObjectId string = ''
param postgresAdminPrincipalName string = ''
@allowed(['User', 'Group', 'ServicePrincipal'])
param postgresAdminPrincipalType string = 'User'
@description('Create a new Azure Managed Redis instance and private endpoint. Leave false until SKU and cost are approved.')
param provisionRedis bool = false
param redisName string = 'pm-dev-redis'
@description('Azure Managed Redis SKU; verify regional availability and current cost before opting in.')
@allowed(['Balanced_B0'])
param redisSkuName string = 'Balanced_B0'
param redisPrivateEndpointSubnetName string = 'private-endpoints'
param redisPrivateDnsZoneName string = 'privatelink.redis.azure.net'
param redisPrivateEndpointName string = 'pm-dev-redis-pe'

resource network 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: vnetName
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = if (deployPrivateDevTopology && provisionPostgres) {
  parent: network
  name: postgresSubnetName
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

resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: network
  name: redisPrivateEndpointSubnetName
}

resource containerAppsSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = if (deployPrivateDevTopology) {
  parent: network
  name: acaSubnetName
  properties: {
    addressPrefix: acaSubnetCidr
    defaultOutboundAccess: true
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

resource postgresDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (deployPrivateDevTopology) {
  name: postgresDnsZoneName
  location: 'global'
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (deployPrivateDevTopology) {
  parent: postgresDnsZone
  name: '${environmentName}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: network.id }
  }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = if (deployPrivateDevTopology) {
  name: '${appName}-pull'
  location: location
}

resource registryPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployPrivateDevTopology) {
  name: guid(registry.id, pullIdentity.id, 'AcrPull')
  scope: registry
  properties: {
    principalId: pullIdentity!.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = if (deployPrivateDevTopology && provisionPostgres) {
  name: postgresServerName
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
    highAvailability: { mode: 'Disabled' }
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
    environment: 'dev'
    story: 'EP02-S01'
    topology: 'existing-vnet-private'
  }
  dependsOn: [postgresDnsLink]
}

resource postgresAdministrator 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = if (deployPrivateDevTopology && provisionPostgres) {
  parent: postgres
  name: postgresAdminObjectId
  properties: {
    principalName: postgresAdminPrincipalName
    principalType: postgresAdminPrincipalType
    tenantId: tenant().tenantId
  }
}

resource pitchMindDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = if (deployPrivateDevTopology && provisionPostgres) {
  parent: postgres
  name: 'pitchmind'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource redis 'Microsoft.Cache/redisEnterprise@2025-07-01' = if (deployPrivateDevTopology && provisionRedis) {
  name: redisName
  location: location
  sku: { name: redisSkuName }
  properties: {
    encryption: {}
    highAvailability: 'Disabled'
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
  }
  tags: {
    application: 'PitchMind'
    environment: 'dev'
    topology: 'private-endpoint'
  }
}

resource redisDatabase 'Microsoft.Cache/redisEnterprise/databases@2025-07-01' = if (deployPrivateDevTopology && provisionRedis) {
  parent: redis
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    clusteringPolicy: 'OSSCluster'
    evictionPolicy: 'VolatileLRU'
    modules: []
    port: 10000
  }
}

resource redisPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (deployPrivateDevTopology && provisionRedis) {
  name: redisPrivateDnsZoneName
  location: 'global'
}

resource redisPrivateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (deployPrivateDevTopology && provisionRedis) {
  parent: redisPrivateDnsZone
  name: '${environmentName}-redis-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: network.id }
  }
}

resource redisPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = if (deployPrivateDevTopology && provisionRedis) {
  name: redisPrivateEndpointName
  location: location
  properties: {
    subnet: { id: privateEndpointSubnet.id }
    privateLinkServiceConnections: [
      {
        name: '${redisName}-connection'
        properties: {
          privateLinkServiceId: redis.id
          groupIds: ['redisEnterprise']
        }
      }
    ]
  }
}

resource redisPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = if (deployPrivateDevTopology && provisionRedis) {
  parent: redisPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'redis'
        properties: { privateDnsZoneId: redisPrivateDnsZone.id }
      }
    ]
  }
  dependsOn: [redisPrivateDnsLink]
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2025-07-01' = if (deployPrivateDevTopology) {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: containerAppsSubnet.id
      internal: false
    }
    publicNetworkAccess: 'Enabled'
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
  tags: {
    application: 'PitchMind'
    environment: 'dev'
    story: 'EP02-S04'
    topology: 'existing-vnet-public-app-private-data'
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = if (deployPrivateDevTopology) {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${pullIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: pullIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: appImage
          env: [
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
          ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          probes: [
            { type: 'Liveness', httpGet: { path: '/health', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/api/health', port: 8080 }, initialDelaySeconds: 5, periodSeconds: 10 }
          ]
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
  tags: {
    application: 'PitchMind'
    environment: 'dev'
    story: 'EP02-S04'
    imageDigestDeployment: 'true'
  }
  dependsOn: [registryPull]
}

output containerAppsEnvironmentId string = deployPrivateDevTopology ? containerEnvironment!.id : ''
output containerAppName string = deployPrivateDevTopology ? app!.name : ''
output containerAppFqdn string = deployPrivateDevTopology ? app!.properties.configuration.ingress.fqdn : ''
output postgresServerFqdn string = deployPrivateDevTopology && provisionPostgres ? postgres!.properties.fullyQualifiedDomainName : ''
output redisHostName string = deployPrivateDevTopology && provisionRedis ? redis!.properties.hostName : ''
output redisPort int = deployPrivateDevTopology && provisionRedis ? redisDatabase!.properties.port : 0
