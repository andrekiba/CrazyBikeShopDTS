targetScope = 'subscription'

param resourceGroupName string

param location string

param principalId string

param environment string

param userPrincipalId string

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

module identity 'identity/identity.bicep' = {
  name: 'identity'
  scope: rg
  params: {
    location: location
    environment: environment
  }
}

module log 'log/log.bicep' = {
  name: 'log'
  scope: rg
  params: {
    location: location
    environment: environment
  }
}

module ai 'ai/ai.bicep' = {
  name: 'ai'
  scope: rg
  params: {
    location: location
    log_outputs_loganalyticsworkspaceid: log.outputs.logAnalyticsWorkspaceId
    environment: environment
  }
}

module acr 'acr/acr.bicep' = {
  name: 'acr'
  scope: rg
  params: {
    location: location
    environment: environment
  }
}

module cae 'cae/cae.bicep' = {
  name: 'cae'
  scope: rg
  params: {
    location: location
    acr_outputs_name: acr.outputs.name
    log_outputs_name: log.outputs.name
    environment: environment
    userPrincipalId: principalId
  }
}

module dts_bicep 'dts-bicep/dts-bicep.bicep' = {
  name: 'dts-bicep'
  scope: rg
  params: {
    location: location
    dtsName: 'cbs-${environment}-dts'
  }
}

module identityAssignDTS 'identityAssignDTS/identityAssignDTS.bicep' = {
  name: 'identityAssignDTS'
  scope: rg
  params: {
    location: location
    principalId: identity.outputs.principalId
    roleDefinitionId: '0ad04412-c4d5-4796-b79c-f76d14c8d402'
    principalType: 'ServicePrincipal'
  }
}

module identityAssignDTSDash 'identityAssignDTSDash/identityAssignDTSDash.bicep' = {
  name: 'identityAssignDTSDash'
  scope: rg
  params: {
    location: location
    principalId: userPrincipalId
    roleDefinitionId: '0ad04412-c4d5-4796-b79c-f76d14c8d402'
    principalType: 'User'
  }
}

output cae_AZURE_CONTAINER_REGISTRY_NAME string = cae.outputs.AZURE_CONTAINER_REGISTRY_NAME

output cae_AZURE_CONTAINER_REGISTRY_ENDPOINT string = cae.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT

output cae_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = cae.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID

output cae_AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = cae.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN

output cae_AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = cae.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID

output identity_id string = identity.outputs.id

output dts_bicep_dts_endpoint string = dts_bicep.outputs.dts_endpoint

output ai_appInsightsConnectionString string = ai.outputs.appInsightsConnectionString

output identity_clientId string = identity.outputs.clientId

output dts_bicep_taskhub_name string = dts_bicep.outputs.taskhub_name