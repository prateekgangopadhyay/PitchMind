targetScope = 'resourceGroup'

@allowed(['dev', 'stage', 'prod'])
param environment string
param location string = resourceGroup().location

@description('Deploy optional expensive data services only after review')
param enableAdvancedAnalytics bool = false

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
  params: { location: location, name: take('${prefix}-${suffix}-sb', 50) }
}

module apps './modules/container-apps.bicep' = {
  name: 'containerApps'
  params: { location: location, prefix: prefix, logAnalyticsId: monitoring.outputs.workspaceId }
}

output containerAppsEnvironmentId string = apps.outputs.environmentId
output registryLoginServer string = registry.outputs.loginServer
output eventHubNamespace string = eventHubs.outputs.namespaceName
output serviceBusNamespace string = serviceBus.outputs.namespaceName
output storageAccountName string = storage.outputs.accountName
output advancedAnalyticsEnabled bool = enableAdvancedAnalytics
