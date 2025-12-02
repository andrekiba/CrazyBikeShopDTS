@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param cae_outputs_azure_container_apps_environment_default_domain string

param cae_outputs_azure_container_apps_environment_id string

param orchestrator_containerimage string

param identity_outputs_id string

param dts_bicep_outputs_dts_endpoint string

param ai_outputs_appinsightsconnectionstring string

param identity_outputs_clientid string

param environment string

param dts_bicep_outputs_taskhub_name string

param cae_outputs_azure_container_registry_endpoint string

param cae_outputs_azure_container_registry_managed_identity_id string

resource orchestrator 'Microsoft.App/containerApps@2025-02-02-preview' = {
  name: 'cbs-dts-${environment}-orchestrator'
  location: location
  properties: {
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: cae_outputs_azure_container_registry_endpoint
          identity: cae_outputs_azure_container_registry_managed_identity_id
        }
      ]
      runtime: {
        dotnet: {
          autoConfigureDataProtection: true
        }
      }
    }
    environmentId: cae_outputs_azure_container_apps_environment_id
    template: {
      containers: [
        {
          image: orchestrator_containerimage
          name: 'orchestrator'
          env: [
            {
              name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_EMIT_EXCEPTION_LOG_ATTRIBUTES'
              value: 'true'
            }
            {
              name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_EMIT_EVENT_LOG_ATTRIBUTES'
              value: 'true'
            }
            {
              name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY'
              value: 'in_memory'
            }
            {
              name: 'ConnectionStrings__dts'
              value: '${dts_bicep_outputs_dts_endpoint};TaskHub=default;Authentication=AzureDefault'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: ai_outputs_appinsightsconnectionstring
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: identity_outputs_clientid
            }
            {
              name: 'AZURE_TOKEN_CREDENTIALS'
              value: 'ManagedIdentityCredential'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 5
        rules: [
          {
            name: 'dts-orchestration-scaler'
            custom: {
              type: 'azure-durabletask-scheduler'
              metadata: {
                endpoint: dts_bicep_outputs_dts_endpoint
                taskhubName: dts_bicep_outputs_taskhub_name
                maxConcurrentWorkItemsCount: '1'
                workItemType: 'Orchestration'
              }
              identity: identity_outputs_id
            }
          }
        ]
      }
    }
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity_outputs_id}': { }
      '${cae_outputs_azure_container_registry_managed_identity_id}': { }
    }
  }
}