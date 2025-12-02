@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param environment string

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'cbs-${environment}-identity'
  location: location
}

output id string = identity.id

output clientId string = identity.properties.clientId

output principalId string = identity.properties.principalId

output principalName string = identity.name

output name string = identity.name