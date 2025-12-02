@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param userPrincipalId string = ''

param tags object = { }

param acr_outputs_name string

param log_outputs_name string

param environment string

resource cae_mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: take('cae_mi-${uniqueString(resourceGroup().id)}', 128)
  location: location
  tags: tags
}

resource acr 'Microsoft.ContainerRegistry/registries@2025-04-01' existing = {
  name: acr_outputs_name
}

resource acr_cae_mi_AcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, cae_mi.id, subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d'))
  properties: {
    principalId: cae_mi.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalType: 'ServicePrincipal'
  }
  scope: acr
}

resource log 'Microsoft.OperationalInsights/workspaces@2025-02-01' existing = {
  name: log_outputs_name
}

resource cae 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cbs-${environment}-cae'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: log.properties.customerId
        sharedKey: log.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
  tags: tags
}

resource aspireDashboard 'Microsoft.App/managedEnvironments/dotNetComponents@2024-10-02-preview' = {
  name: 'aspire-dashboard'
  properties: {
    componentType: 'AspireDashboard'
  }
  parent: cae
}

output AZURE_LOG_ANALYTICS_WORKSPACE_NAME string = log_outputs_name

output AZURE_LOG_ANALYTICS_WORKSPACE_ID string = log.id

output AZURE_CONTAINER_REGISTRY_NAME string = acr_outputs_name

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = acr.properties.loginServer

output AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = cae_mi.id

output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = cae.name

output AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = cae.id

output AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = cae.properties.defaultDomain