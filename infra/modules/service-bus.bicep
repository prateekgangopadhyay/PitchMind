param location string
param name string
resource ns 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: name
  location: location
  sku: { name: 'Standard', tier: 'Standard' }
}
resource queue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: ns
  name: 'background-jobs'
  properties: { requiresDuplicateDetection: true, duplicateDetectionHistoryTimeWindow: 'PT10M' }
}
output namespaceName string = ns.name
