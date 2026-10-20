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
param entraTenantId string = tenant().tenantId
param entraClientId string = '4accaf35-1ae9-4e7d-999d-3f86fd7caec4'

param postgresServerName string = 'pitchmind-postgresql-dev'
@description('Object ID of the existing DEV migration identity; its PostgreSQL administrator mapping is removed after migration succeeds.')
param postgresMigratorObjectId string = 'dceb4835-bf2e-4794-88b8-cc9c23643c60'
param postgresMigratorIdentityName string = 'pm-dev-demo-identity'
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
param eventHubsNamespaceName string = 'pm-dev-eh'
param candidateEventHubName string = 'candidate-match-events'
param candidateEventHubConsumerGroup string = 'pitchmind-candidate-worker'
@description('Opt in to the Azure Event Hubs -> PostgreSQL outbox -> Redis/SignalR candidate simulation path.')
param enableCandidateFeed bool = false
param foundryAccountName string = 'pitchmind-ai-resource'
param foundryProjectName string = 'pitchmind-ai'
param foundryAgentName string = 'TacticalAnalyst'
param signalRName string = 'pm-dev-signalr'
param signalRAllowedOrigin string = 'https://pm-dev-api-vnet.icyglacier-6b4ac95d.centralindia.azurecontainerapps.io'
@allowed(['Standard_S1'])
param signalRSkuName string = 'Standard_S1'
param storageAccountName string = 'pmdev4iwntkatliy6sst'
param candidateCheckpointContainer string = 'candidate-checkpoints'

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

resource existingPostgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: postgresServerName
}

resource postgresMigratorIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: postgresMigratorIdentityName
}

// Candidate raw events are isolated from the existing projection-telemetry hub.
resource eventHubsNamespace 'Microsoft.EventHub/namespaces@2024-01-01' existing = {
  name: eventHubsNamespaceName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource checkpointContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2022-09-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  parent: blobService
  name: candidateCheckpointContainer
  properties: { publicAccess: 'None' }
}

resource blobPrivateDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  name: 'privatelink.blob.core.windows.net'
  location: 'global'
}

resource blobPrivateDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  parent: blobPrivateDnsZone
  name: '${environmentName}-blob-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: network.id }
  }
}

resource blobPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  name: 'pm-dev-blob-pe'
  location: location
  properties: {
    subnet: { id: privateEndpointSubnet.id }
    privateLinkServiceConnections: [
      {
        name: '${storageAccountName}-blob-connection'
        properties: {
          privateLinkServiceId: storageAccount.id
          groupIds: ['blob']
        }
      }
    ]
  }
}

resource blobPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  parent: blobPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      { name: 'blob', properties: { privateDnsZoneId: blobPrivateDnsZone.id } }
    ]
  }
  dependsOn: [blobPrivateDnsLink]
}

resource signalR 'Microsoft.SignalRService/signalR@2024-03-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  name: signalRName
  location: location
  sku: { name: signalRSkuName, capacity: 1 }
  properties: {
    features: [
      { flag: 'ServiceMode', value: 'Default' }
      { flag: 'EnableConnectivityLogs', value: 'True' }
    ]
    // Azure SignalR authorizes each connection token; CORS remains restricted to this browser origin.
    cors: { allowedOrigins: [signalRAllowedOrigin] }
    tls: { clientCertEnabled: false }
    publicNetworkAccess: 'Enabled'
  }
  tags: { application: 'PitchMind', environment: 'dev', story: 'EP06-S02' }
}

resource candidateEventHub 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = if (deployPrivateDevTopology) {
  parent: eventHubsNamespace
  name: candidateEventHubName
  properties: {
    partitionCount: 4
    messageRetentionInDays: 7
  }
}

resource candidateEventHubConsumerGroupResource 'Microsoft.EventHub/namespaces/eventhubs/consumergroups@2024-01-01' = if (deployPrivateDevTopology) {
  parent: candidateEventHub
  name: candidateEventHubConsumerGroup
  properties: {}
}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = if (deployPrivateDevTopology) {
  name: '${appName}-pull'
  location: location
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = if (deployPrivateDevTopology) {
  name: '${appName}-runtime'
  location: location
}

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = {
  name: foundryAccountName
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: foundryAccount
  name: foundryProjectName
}

resource apiFoundryAgentConsumer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  scope: foundryProject
  name: guid(foundryProject.id, apiIdentity.id, 'FoundryAgentConsumer')
  properties: {
    principalId: apiIdentity!.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'eed3b665-ab3a-47b6-8f48-c9382fb1dad6')
  }
}

resource workerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = if (deployPrivateDevTopology && enableCandidateFeed) {
  name: '${appName}-eventworker'
  location: location
}

resource candidateEventHubSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  scope: candidateEventHub
  name: guid(candidateEventHub.id, apiIdentity.id, 'EventHubsDataSender')
  properties: {
    principalId: apiIdentity!.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2b629674-e913-4c01-ae53-ef4638d8f975')
  }
}

resource candidateEventHubReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  scope: candidateEventHub
  name: guid(candidateEventHub.id, workerIdentity.id, 'EventHubsDataReceiver')
  properties: {
    principalId: workerIdentity!.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a638d3c7-ab3a-418d-83e6-5f17a39d4fde')
  }
}

resource workerBlobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  scope: checkpointContainer
  name: guid(checkpointContainer.id, workerIdentity.id, 'StorageBlobDataContributor')
  properties: {
    principalId: workerIdentity!.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

resource signalRAppServer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  scope: signalR
  name: guid(signalR.id, apiIdentity.id, 'SignalRAppServer')
  properties: {
    principalId: apiIdentity!.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '420fcaa2-552c-430f-98ca-3264be4806c7')
  }
}

// Used only by the migration job. The deployment script removes its PostgreSQL
// administrator mapping after a successful migration run.
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

resource existingPostgresAdministrator 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = if (deployPrivateDevTopology && !provisionPostgres) {
  parent: existingPostgres
  name: postgresMigratorObjectId
  properties: {
    principalName: postgresMigratorIdentity.name
    principalType: 'ServicePrincipal'
    tenantId: tenant().tenantId
  }
}

resource postgresMigrationAdministrator 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = if (deployPrivateDevTopology && provisionPostgres) {
  parent: postgres
  name: postgresMigratorObjectId
  properties: {
    principalName: postgresMigratorIdentity.name
    principalType: 'ServicePrincipal'
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

resource existingPitchMindDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = if (deployPrivateDevTopology && !provisionPostgres) {
  parent: existingPostgres
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

resource existingRedis 'Microsoft.Cache/redisEnterprise@2025-07-01' existing = {
  name: redisName
}

resource existingRedisDatabase 'Microsoft.Cache/redisEnterprise/databases@2025-07-01' existing = {
  parent: existingRedis
  name: 'default'
}

resource redisCandidateAccess 'Microsoft.Cache/redisEnterprise/databases/accessPolicyAssignments@2026-05-01-preview' = if (deployPrivateDevTopology && enableCandidateFeed && !provisionRedis) {
  parent: existingRedisDatabase
  name: 'pitchmind-api-cursor-cache'
  properties: {
    accessPolicyName: 'default'
    accessString: '+@read +@write ~candidate-live:v1:*'
    user: { objectId: apiIdentity!.properties.principalId }
  }
}

resource provisionedRedisCandidateAccess 'Microsoft.Cache/redisEnterprise/databases/accessPolicyAssignments@2026-05-01-preview' = if (deployPrivateDevTopology && enableCandidateFeed && provisionRedis) {
  parent: redisDatabase
  name: 'pitchmind-api-cursor-cache'
  properties: {
    accessPolicyName: 'default'
    accessString: '+@read +@write ~candidate-live:v1:*'
    user: { objectId: apiIdentity!.properties.principalId }
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
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
      '${apiIdentity.id}': {}
    }
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
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Development' }
            { name: 'Runtime__Profile', value: 'azure' }
            { name: 'CandidateFeed__Enabled', value: enableCandidateFeed ? 'true' : 'false' }
            { name: 'Identity__Authority', value: 'https://login.microsoftonline.com/${entraTenantId}/v2.0' }
            // Entra v2 access tokens use the API application's client ID GUID as aud.
            // The SPA still requests the exposed api://<client-id>/Match.Read scope.
            { name: 'Identity__Audience', value: entraClientId }
            { name: 'Azure__ManagedIdentityClientId', value: apiIdentity!.properties.clientId }
            { name: 'ConnectionStrings__PitchMind', value: 'Host=${postgresServerName}.postgres.database.azure.com;Database=pitchmind;Username=pitchmind_api;SSL Mode=VerifyFull;Timeout=15' }
            { name: 'EventHubs__Namespace', value: enableCandidateFeed ? '${eventHubsNamespace.name}.servicebus.windows.net' : '' }
            { name: 'EventHubs__CandidateHubName', value: enableCandidateFeed ? candidateEventHubName : '' }
            { name: 'Storage__BlobServiceUri', value: enableCandidateFeed ? 'https://${storageAccountName}.blob.core.windows.net' : '' }
            { name: 'Storage__CheckpointContainer', value: enableCandidateFeed ? candidateCheckpointContainer : '' }
            { name: 'Redis__Endpoint', value: enableCandidateFeed ? '${provisionRedis ? redis!.properties.hostName : existingRedis.properties.hostName}:${provisionRedis ? redisDatabase!.properties.port : existingRedisDatabase.properties.port}' : '' }
            { name: 'Realtime__AzureSignalR__Endpoint', value: enableCandidateFeed ? 'https://${signalR!.properties.hostName}' : '' }
            { name: 'Foundry__Endpoint', value: enableCandidateFeed ? 'https://${foundryAccountName}.services.ai.azure.com/api/projects/${foundryProjectName}' : '' }
            { name: 'Foundry__AgentName', value: enableCandidateFeed ? foundryAgentName : '' }
            // The DEV Container App is reachable outside the developer machine; never trust caller-supplied identity headers.
            { name: 'Identity__DevelopmentHeaderAuthEnabled', value: 'false' }
          ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          probes: [
            { type: 'Liveness', httpGet: { path: '/health', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/api/health', port: 8080 }, initialDelaySeconds: 5, periodSeconds: 10 }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
  tags: {
    application: 'PitchMind'
    environment: 'dev'
    story: 'EP02-S04'
    imageDigestDeployment: 'true'
  }
  dependsOn: [registryPull, existingPostgresAdministrator, postgresMigrationAdministrator, pitchMindDatabase, existingPitchMindDatabase, candidateEventHubSender, signalRAppServer, redisCandidateAccess, provisionedRedisCandidateAccess]
}

resource eventWorker 'Microsoft.App/containerApps@2024-03-01' = if (deployPrivateDevTopology && enableCandidateFeed) {
  name: '${appName}-eventworker'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
      '${workerIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        { server: registry.properties.loginServer, identity: pullIdentity.id }
      ]
    }
    template: {
      containers: [
        {
          name: 'eventworker'
          image: appImage
          command: ['dotnet', '/app/worker/PitchMind.EventWorker.dll']
          env: [
            { name: 'Runtime__Profile', value: 'azure' }
            { name: 'CandidateFeed__Enabled', value: 'true' }
            { name: 'EventHubs__OutboxDispatchEnabled', value: 'false' }
            { name: 'EventHubs__Namespace', value: '${eventHubsNamespace.name}.servicebus.windows.net' }
            { name: 'EventHubs__CandidateHubName', value: candidateEventHubName }
            { name: 'EventHubs__CandidateConsumerGroup', value: candidateEventHubConsumerGroup }
            { name: 'Storage__BlobServiceUri', value: 'https://${storageAccountName}.blob.core.windows.net' }
            { name: 'Storage__CheckpointContainer', value: candidateCheckpointContainer }
            { name: 'Azure__ManagedIdentityClientId', value: workerIdentity!.properties.clientId }
            { name: 'ConnectionStrings__PitchMind', value: 'Host=${postgresServerName}.postgres.database.azure.com;Database=pitchmind;Username=pitchmind_worker;SSL Mode=VerifyFull;Timeout=15' }
          ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
  tags: { application: 'PitchMind', environment: 'dev', story: 'EP03-S03' }
  dependsOn: [registryPull, candidateEventHubReceiver, workerBlobDataContributor, checkpointContainer, blobPrivateDnsZoneGroup, postgresMigrationAdministrator, existingPostgresAdministrator, pitchMindDatabase, existingPitchMindDatabase]
}

resource databaseMigrationJob 'Microsoft.App/jobs@2024-03-01' = if (deployPrivateDevTopology) {
  name: '${appName}-db-migrator'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
      '${postgresMigratorIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerEnvironment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 900
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
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
          name: 'db-migrator'
          image: appImage
          command: ['dotnet', '/app/migrator/PitchMind.DbMigrator.dll']
          env: [
            { name: 'Runtime__Profile', value: 'azure' }
            { name: 'Azure__ManagedIdentityClientId', value: postgresMigratorIdentity.properties.clientId }
            { name: 'PitchMind__ApiIdentityObjectId', value: apiIdentity!.properties.principalId }
            { name: 'PitchMind__WorkerIdentityObjectId', value: enableCandidateFeed ? workerIdentity!.properties.principalId : '' }
            { name: 'ConnectionStrings__Postgres', value: 'Host=${postgresServerName}.postgres.database.azure.com;Database=pitchmind;Username=${postgresMigratorIdentity.name};SSL Mode=VerifyFull;Timeout=15' }
          ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
        }
      ]
    }
  }
  dependsOn: [registryPull, existingPostgresAdministrator, postgresMigrationAdministrator, pitchMindDatabase, existingPitchMindDatabase]
}

output containerAppsEnvironmentId string = deployPrivateDevTopology ? containerEnvironment!.id : ''
output containerAppName string = deployPrivateDevTopology ? app!.name : ''
output containerAppFqdn string = deployPrivateDevTopology ? app!.properties.configuration.ingress.fqdn : ''
output postgresServerFqdn string = deployPrivateDevTopology && provisionPostgres ? postgres!.properties.fullyQualifiedDomainName : ''
output redisHostName string = deployPrivateDevTopology && provisionRedis ? redis!.properties.hostName : ''
output redisPort int = deployPrivateDevTopology && provisionRedis ? redisDatabase!.properties.port : 0
output redisEndpoint string = deployPrivateDevTopology ? '${provisionRedis ? redis!.properties.hostName : existingRedis.properties.hostName}:${provisionRedis ? redisDatabase!.properties.port : existingRedisDatabase.properties.port}' : ''
output azureSignalREndpoint string = deployPrivateDevTopology && enableCandidateFeed ? 'https://${signalR!.properties.hostName}' : ''
output eventWorkerName string = deployPrivateDevTopology && enableCandidateFeed ? eventWorker!.name : ''
output postgresMigrationJobName string = deployPrivateDevTopology ? databaseMigrationJob!.name : ''
output postgresMigrationAdminObjectId string = deployPrivateDevTopology ? postgresMigratorObjectId : ''
