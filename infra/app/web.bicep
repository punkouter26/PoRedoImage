param name string
param location string = resourceGroup().location
param tags object = {}
param appServicePlanId string
param managedIdentityId string
param managedIdentityPrincipalId string
param appSettings object = {}

resource web 'Microsoft.Web/sites@2022-09-01' = {
  name: name
  location: location
  tags: union(tags, { 'azd-service-name': 'api' })
  kind: 'app'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlanId
    siteConfig: {
      alwaysOn: false
      ftpsState: 'FtpsOnly'
      cors: {
        allowedOrigins: []
        supportCredentials: false
      }
      appSettings: [for item in items(appSettings): {
        name: item.key
        value: item.value
      }]
      netFrameworkVersion: 'v8.0'
      metadata: [
        {
          name: 'CURRENT_STACK'
          value: 'dotnet'
        }
      ]
    }
    httpsOnly: true
  }
}

output SERVICE_WEB_IDENTITY_PRINCIPAL_ID string = managedIdentityPrincipalId
output SERVICE_WEB_NAME string = web.name
output SERVICE_WEB_URI string = 'https://${web.properties.defaultHostName}'
