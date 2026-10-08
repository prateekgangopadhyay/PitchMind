// Explicit DEV-only offline demonstration; not canonical Azure integration.
param location string
param environmentId string
param image string
param registryName string
param identityName string = 'pm-dev-demo-identity'
param appName string = 'pm-dev-api'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}
resource pull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, 'AcrPull')
  scope: registry
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}
resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  tags: { environment: 'dev', workload: 'offline-synthetic-demo', project: 'pitchmind' }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
      registries: [{ server: registry.properties.loginServer, identity: identity.id }]
    }
    template: {
      containers: [{
        name: 'api'
        image: image
        env: [{ name: 'ASPNETCORE_URLS', value: 'http://+:8080' }]
        resources: { cpu: json('0.25'), memory: '0.5Gi' }
        probes: [
          { type: 'Liveness', httpGet: { path: '/health', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 30 }
          { type: 'Readiness', httpGet: { path: '/api/health', port: 8080 }, initialDelaySeconds: 5, periodSeconds: 10 }
        ]
      }]
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
  dependsOn: [pull]
}
output fqdn string = app.properties.configuration.ingress.fqdn
