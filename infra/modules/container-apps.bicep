param location string
param prefix string
param logAnalyticsId string

// Container Apps environment only. Workload apps, identity, secrets and ingress
// are deployed after the application images and network design are ready.
resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-cae'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsId, '2022-10-01').customerId
        sharedKey: listKeys(logAnalyticsId, '2022-10-01').primarySharedKey
      }
    }
  }
}
output environmentId string = env.id
