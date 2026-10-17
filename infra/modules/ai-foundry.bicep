// Azure AI Foundry / OpenAI Cognitive Services module for grounded match explanations.
// Kept conditional and default-off to honor EP02-S05 local cost guardrails.

@description('Deployment region')
param location string = resourceGroup().location

@description('Name of the Azure AI Foundry account')
param accountName string

@description('SKU name for the Azure AI Foundry / Cognitive Services account.')
param skuName string = 'S0'

@description('Name of the chat completions model deployment')
param deploymentName string = 'gpt-4o-mini'

@description('Model format name')
param modelFormat string = 'OpenAI'

@description('Model identifier')
param modelName string = 'gpt-4o-mini'

@description('Model version')
param modelVersion string = '2024-07-18'

@description('Model capacity in tokens-per-minute units')
param modelCapacity int = 10

@description('Subnet ID for the private endpoint')
param subnetId string = ''

@description('Private DNS Zone ID for Cognitive Services private link')
param privateDnsZoneId string = ''

@description('Whether to provision Azure AI Foundry')
param deployFoundry bool = false

@description('Resource tags')
param tags object = {}

resource aiAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = if (deployFoundry) {
  name: accountName
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: skuName
  }
  properties: {
    customSubDomainName: toLower(accountName)
    publicNetworkAccess: empty(subnetId) ? 'Enabled' : 'Disabled'
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (deployFoundry) {
  parent: aiAccount
  name: deploymentName
  sku: {
    name: 'Standard'
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: modelFormat
      name: modelName
      version: modelVersion
    }
  }
}

resource aiPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = if (deployFoundry && !empty(subnetId)) {
  name: '${accountName}-pe'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${accountName}-conn'
        properties: {
          privateLinkServiceId: deployFoundry ? aiAccount.id : ''
          groupIds: [
            'account'
          ]
        }
      }
    ]
  }
}

resource aiPrivateDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = if (deployFoundry && !empty(subnetId) && !empty(privateDnsZoneId)) {
  parent: aiPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-cognitiveservices-azure-com'
        properties: {
          privateDnsZoneId: privateDnsZoneId
        }
      }
    ]
  }
}

output accountId string = deployFoundry ? aiAccount!.id : ''
output endpoint string = deployFoundry ? aiAccount!.properties.endpoint : ''
