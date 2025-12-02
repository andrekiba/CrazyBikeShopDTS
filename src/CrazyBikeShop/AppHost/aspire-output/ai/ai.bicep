@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param applicationType string = 'web'

param kind string = 'web'

param log_outputs_loganalyticsworkspaceid string

param environment string

resource ai 'Microsoft.Insights/components@2020-02-02' = {
  name: 'cbs-dts-${environment}-ai'
  kind: kind
  location: location
  properties: {
    Application_Type: applicationType
    IngestionMode: 'LogAnalytics'
    RetentionInDays: 30
    WorkspaceResourceId: log_outputs_loganalyticsworkspaceid
  }
  tags: {
    'aspire-resource-name': 'ai'
  }
}

output appInsightsConnectionString string = ai.properties.ConnectionString

output name string = ai.name