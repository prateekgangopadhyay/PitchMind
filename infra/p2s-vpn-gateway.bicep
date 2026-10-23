targetScope = 'resourceGroup'

@description('Existing DEV virtual network containing PostgreSQL and GatewaySubnet.')
param vnetName string = 'pm-dev-vnet'

@description('Existing subnet dedicated to the virtual network gateway.')
param gatewaySubnetName string = 'GatewaySubnet'

@description('Existing Standard static public IP resource for the gateway.')
param publicIpName string = 'pm-dev-vpn-gateway-pip'

@description('Point-to-site VPN gateway resource name.')
param gatewayName string = 'pm-dev-vpn-gateway'

@description('Address pool assigned to VPN clients. Keep it non-overlapping with the VNet and client networks.')
param clientAddressPool string = '172.30.240.0/24'

@description('Microsoft Entra tenant ID.')
param entraTenantId string = '723b667a-983d-46f3-9b90-a9b2580db639'

@description('Microsoft-registered Azure VPN Client audience for Azure public cloud.')
param entraAudience string = 'c632b3df-fb67-4d84-bdcf-b95ad541b5c8'

@description('Microsoft Entra issuer URL. Preserve the trailing slash.')
param entraIssuer string = 'https://sts.windows.net/723b667a-983d-46f3-9b90-a9b2580db639/'

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: vnetName
}

resource gatewaySubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: vnet
  name: gatewaySubnetName
}

resource publicIp 'Microsoft.Network/publicIPAddresses@2024-05-01' existing = {
  name: publicIpName
}

// This declaration configures the existing DEV VPN gateway. It is not invoked by CI/CD.
// Deploying it may create or update the gateway and incurs ongoing VPN Gateway charges.
resource gateway 'Microsoft.Network/virtualNetworkGateways@2024-07-01' = {
  name: gatewayName
  location: resourceGroup().location
  properties: {
    activeActive: false
    enableBgp: false
    gatewayType: 'Vpn'
    vpnType: 'RouteBased'
    vpnGatewayGeneration: 'Generation1'
    sku: {
      name: 'VpnGw1AZ'
      tier: 'VpnGw1AZ'
    }
    ipConfigurations: [
      {
        name: 'vnetGatewayConfig'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          publicIPAddress: {
            id: publicIp.id
          }
          subnet: {
            id: gatewaySubnet.id
          }
        }
      }
    ]
    vpnClientConfiguration: {
      vpnClientAddressPool: {
        addressPrefixes: [
          clientAddressPool
        ]
      }
      vpnClientProtocols: [
        'OpenVPN'
      ]
      vpnAuthenticationTypes: [
        'AAD'
      ]
      aadTenant: 'https://login.microsoftonline.com/${entraTenantId}/'
      aadAudience: entraAudience
      aadIssuer: entraIssuer
    }
  }
}

output gatewayId string = gateway.id
output gatewayProvisioningState string = gateway.properties.provisioningState
