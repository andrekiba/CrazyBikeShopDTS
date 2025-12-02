@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param environment string

resource acr 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: 'cbs-dts${environment}cr'
  location: location
  sku: {
    name: 'Basic'
  }
  tags: {
    'aspire-resource-name': 'acr'
  }
}

output name string = acr.name

output loginServer string = acr.properties.loginServer