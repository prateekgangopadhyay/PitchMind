// Azure SignalR Service module for real-time match event cursors.
// Kept conditional and default-off to honor EP02-S05 local cost guardrails.

@description('Deployment region')
param location string = resourceGroup().location

@description('Name of the Azure SignalR Service instance')
param name string

@description('SKU name for Azure SignalR. Standard_S1 supports private endpoints.')
@allowed([
  'Free_F1'
  'Standard_S1'
  'Premium_P1'
])
param skuName string = 'Standard_S1'

@description('Capacity units for Azure SignalR.')
param capacity int = 1

@description('Service mode for Azure SignalR.')
@allowed([
  'Default'
  'Serverless'
  'Classic'
])
param serviceMode string = 'Default'

@description('Subnet ID for the private endpoint')
param subnetId string = ''

@description('Private DNS Zone ID for SignalR private link')
param privateDnsZoneId string = ''

@description('Whether to provision Azure SignalR Service')
param deploySignalR bool = false

@description('Resource tags')
param tags object = {}

resource signalR 'Microsoft.SignalRService/signalR@2024-03-01' = if (deploySignalR) {
  name: name
  location: location
  tags: tags
  sku: {
    name: skuName
    capacity: capacity
  }
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: serviceMode
      }
      {
        flag: 'EnableConnectivityLogs'
        value: 'True'
      }
    ]
    cors: {
      allowedOrigins: [
        '*'
      ]
    }
    tls: {
      clientCertEnabled: false
    }
    publicNetworkAccess: empty(subnetId) ? 'Enabled' : 'Disabled'
  }
}

resource signalRPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = if (deploySignalR && !empty(subnetId)) {
  name: '${name}-pe'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${name}-conn'
        properties: {
          privateLinkServiceId: deploySignalR ? signalR.id : ''
          groupIds: [
            'signalr'
          ]
        }
      }
    ]
  }
}

resource signalRPrivateDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = if (deploySignalR && !empty(subnetId) && !empty(privateDnsZoneId)) {
  parent: signalRPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-service-signalr-net'
        properties: {
          privateDnsZoneId: privateDnsZoneId
        }
      }
    ]
  }
}

output signalRId string = deploySignalR ? signalR!.id : ''
output hostName string = deploySignalR ? signalR!.properties.hostName : ''
