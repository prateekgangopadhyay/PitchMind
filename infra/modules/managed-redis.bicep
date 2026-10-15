// Azure Managed Redis uses Microsoft.Cache/redisEnterprise with Entra Workload Identity.
// Kept conditional and default-off to honor EP02-S05 local cost guardrails.

@description('Deployment region')
param location string = resourceGroup().location

@description('Name of the Azure Managed Redis Enterprise cluster')
param clusterName string

@description('Name of the default Redis database')
param databaseName string = 'default'

@description('SKU name for Redis Enterprise. Balanced_B0 is appropriate for MVP DEV.')
@allowed([
  'Balanced_B0'
  'Balanced_B1'
  'MemoryOptimized_M10'
])
param skuName string = 'Balanced_B0'

@description('Subnet ID for the private endpoint')
param subnetId string = ''

@description('Private DNS Zone ID for Redis private link')
param privateDnsZoneId string = ''

@description('Whether to provision the managed Redis cluster (default off)')
param deployManagedRedis bool = false

@description('Resource tags')
param tags object = {}

resource redisCluster 'Microsoft.Cache/redisEnterprise@2024-02-01' = if (deployManagedRedis) {
  name: clusterName
  location: location
  tags: tags
  sku: {
    name: skuName
    capacity: 2
  }
  properties: {
    minimumTlsVersion: '1.2'
    encryption: {
      customerManagedKeyEncryption: null
    }
  }
}

resource redisDatabase 'Microsoft.Cache/redisEnterprise/databases@2024-02-01' = if (deployManagedRedis) {
  parent: redisCluster
  name: databaseName
  properties: {
    clientProtocol: 'Encrypted'
    clusteringPolicy: 'OSSCluster'
    evictionPolicy: 'VolatileLRU'
    accessKeysAuthentication: 'Disabled'
    modules: []
  }
}

resource redisPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = if (deployManagedRedis && !empty(subnetId)) {
  name: '${clusterName}-pe'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${clusterName}-conn'
        properties: {
          privateLinkServiceId: deployManagedRedis ? redisCluster.id : ''
          groupIds: [
            'redisEnterprise'
          ]
        }
      }
    ]
  }
}

resource redisPrivateDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = if (deployManagedRedis && !empty(subnetId) && !empty(privateDnsZoneId)) {
  parent: redisPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-redisenterprise-cache-azure-net'
        properties: {
          privateDnsZoneId: privateDnsZoneId
        }
      }
    ]
  }
}

output clusterId string = deployManagedRedis ? redisCluster.id : ''
output hostName string = deployManagedRedis ? redisCluster.properties.hostName : ''
