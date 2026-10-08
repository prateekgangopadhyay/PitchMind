targetScope = 'resourceGroup'
@description('Must be an immutable digest. Offline synthetic demo only.')
param image string
param registryName string
param managedEnvironmentName string = 'pm-dev-cae'
param location string = resourceGroup().location
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = { name: managedEnvironmentName }
module demo './modules/demo-workload.bicep' = {
  name: 'offlineDemoWorkload'
  params: { location: location, environmentId: environment.id, image: image, registryName: registryName }
}
output fqdn string = demo.outputs.fqdn
