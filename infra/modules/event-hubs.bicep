param location string
param prefix string
resource ns 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: '${prefix}-eh'
  location: location
  sku: { name: 'Standard', tier: 'Standard', capacity: 1 }
}
resource hub 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = {
  parent: ns
  name: 'match-telemetry'
  properties: { partitionCount: 4, messageRetentionInDays: 1 }
}
output namespaceName string = ns.name
