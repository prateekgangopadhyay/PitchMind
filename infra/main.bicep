targetScope = 'resourceGroup'

@allowed(['dev', 'stage', 'prod'])
param environment string
param location string = resourceGroup().location

@description('Cost-controlled optional analytics; not provisioned by M03.')
param enableAdvancedAnalytics bool = false

@description('Provision the DEV network and Key Vault baseline. Disabled for other environments until reviewed.')
param enableDevFoundation bool = false

@description('Address space for the DEV VNet. Must not overlap with connected networks.')
param virtualNetworkCidr string = '10.42.0.0/16'

var prefix = 'pm-${environment}'
var suffix = uniqueString(resourceGroup().id)

module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring'
  params: { location: location, prefix: prefix }
}

module storage './modules/storage.bicep' = {
  name: 'storage'
  params: { location: location, name: take(replace('${prefix}${suffix}st', '-', ''), 24) }
}

module registry './modules/registry.bicep' = {
  name: 'registry'
  params: { location: location, name: take(replace('${prefix}${suffix}acr', '-', ''), 50) }
}

module eventHubs './modules/event-hubs.bicep' = {
  name: 'eventHubs'
  params: { location: location, prefix: prefix }
}

module serviceBus './modules/service-bus.bicep' = {
  name: 'serviceBus'
  params: { location: location, name: take('${prefix}-${suffix}-sbus', 50) }
}

module apps './modules/container-apps.bicep' = {
  name: 'containerApps'
  params: { location: location, prefix: prefix, logAnalyticsId: monitoring.outputs.workspaceId }
}

module network './modules/networking.bicep' = if (enableDevFoundation && environment == 'dev') {
  name: 'devNetwork'
  params: {
    location: location
    prefix: prefix
    addressSpace: virtualNetworkCidr
  }
}

module vault './modules/key-vault.bicep' = if (enableDevFoundation && environment == 'dev') {
  name: 'devKeyVault'
  params: {
    location: location
    name: replace('${prefix}-${suffix}-kv', '-', '')
  }
}

module signalRService './modules/signalr.bicep' = if (enableDevFoundation && environment == 'dev') {
  name: 'devSignalR'
  params: {
    location: location
    name: take('${prefix}-${suffix}-sigr', 50)
  }
}

module aiFoundryService './modules/ai-foundry.bicep' = if (enableDevFoundation && environment == 'dev') {
  name: 'devAiFoundry'
  params: {
    location: location
    accountName: take(replace('${prefix}${suffix}ai', '-', ''), 24)
  }
}

output containerAppsEnvironmentId string = apps.outputs.environmentId
output registryLoginServer string = registry.outputs.loginServer
output eventHubNamespace string = eventHubs.outputs.namespaceName
output serviceBusNamespace string = serviceBus.outputs.namespaceName
output storageAccountName string = storage.outputs.accountName
output devNetworkId string = enableDevFoundation && environment == 'dev' ? network!.outputs.vnetId : ''
output devPostgresSubnetId string = enableDevFoundation && environment == 'dev' ? network!.outputs.postgresSubnetId : ''
output devKeyVaultName string = enableDevFoundation && environment == 'dev' ? vault!.outputs.vaultName : ''
output devSignalRHostName string = enableDevFoundation && environment == 'dev' ? signalRService!.outputs.hostName : ''
output devAiFoundryEndpoint string = enableDevFoundation && environment == 'dev' ? aiFoundryService!.outputs.endpoint : ''
output advancedAnalyticsEnabled bool = enableAdvancedAnalytics
