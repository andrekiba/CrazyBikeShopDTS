@description('Durable Task Scheduler name')
param dtsName string
@description('Task Hub name')
param taskHubName string = 'default'
@description('Location')
param location string = resourceGroup().location
@description('IP Allow List')
param ipAllowlist array = ['0.0.0.0/0']
@description('Sku Name')
param skuName string = 'Consumption'
@description('Sku Capacity') 
param skuCapacity int = 1
@description('Tags') 
param tags object = {}

resource dts 'Microsoft.DurableTask/schedulers@2025-11-01' = {
  location: location
  tags: tags
  name: dtsName
  properties: {
    ipAllowlist: ipAllowlist
    sku: {
      name: skuName
      capacity: skuCapacity
    }
  }
}

resource taskhub 'Microsoft.DurableTask/schedulers/taskhubs@2025-11-01' = {
  parent: dts
  name: taskHubName
}

output dts_name string = dts.name
output dts_endpoint string = dts.properties.endpoint
output taskhub_name string = taskhub.name


