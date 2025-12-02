@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param cae_outputs_azure_container_apps_environment_default_domain string

param cae_outputs_azure_container_apps_environment_id string

param api_containerimage string

param identity_outputs_id string

param api_containerport string

param dts_bicep_outputs_dts_endpoint string

param ai_outputs_appinsightsconnectionstring string

param identity_outputs_clientid string

param environment string

param cae_outputs_azure_container_registry_endpoint string

param cae_outputs_azure_container_registry_managed_identity_id string

resource api 'Microsoft.App/containerApps@2025-02-02-preview' = {
  name: 'cbs-dts-${environment}-api'
  location: location
  properties: {
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: int(api_containerport)
        transport: 'http'
      }
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
          image: api_containerimage
          name: 'api'
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
              name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
              value: 'true'
            }
            {
              name: 'HTTP_PORTS'
              value: api_containerport
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
        minReplicas: 1
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