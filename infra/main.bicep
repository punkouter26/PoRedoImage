targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention')
param environmentName string = 'PoRedoImage'

@minLength(1)
@description('Primary location for all resources')
param location string = 'eastus2'

// The application is created within a new resource group called 'poredoimage'
param resourceGroupName string = 'poredoimage'

// Option 1: Use existing shared App Service Plan from PoShared resource group
param useSharedAppServicePlan bool = true
param sharedAppServicePlanName string = 'asp-poshared-eastus2-001'
param sharedAppServicePlanResourceGroup string = 'PoShared'

// Option 2: Create new App Service Plan in poredoimage resource group (if useSharedAppServicePlan = false)
param appServicePlanName string = 'asp-poredoimage-eastus2'
param appServicePlanSku string = 'B1' // Basic tier

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = toLower(uniqueString(subscription().id, location, environmentName))
var tags = { 'azd-env-name': environmentName }

// Organize resources in a resource group
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// Reference to PoShared resource group (for existing App Service Plan)
resource sharedRg 'Microsoft.Resources/resourceGroups@2021-04-01' existing = if (useSharedAppServicePlan) {
  name: sharedAppServicePlanResourceGroup
  scope: subscription()
}

// Reference existing App Service Plan from PoShared
resource existingAppServicePlan 'Microsoft.Web/serverfarms@2022-09-01' existing = if (useSharedAppServicePlan) {
  name: sharedAppServicePlanName
  scope: sharedRg
}

// Create new App Service Plan in poredoimage resource group (only if not using shared plan)
module appServicePlan './core/host/appserviceplan.bicep' = if (!useSharedAppServicePlan) {
  name: 'appserviceplan'
  scope: rg
  params: {
    name: appServicePlanName
    location: location
    tags: tags
    sku: {
      name: appServicePlanSku
      tier: 'Basic'
    }
  }
}

// Create managed identity for the app service
module managedIdentity './core/security/managed-identity.bicep' = {
  name: 'managed-identity'
  scope: rg
  params: {
    name: '${abbrs.managedIdentityUserAssignedIdentities}${resourceToken}'
    location: location
    tags: tags
  }
}

// The application frontend
module web './app/web.bicep' = {
  name: 'web'
  scope: rg
  params: {
    name: '${abbrs.webSitesAppService}PoRedoImage-${resourceToken}'
    location: location
    tags: tags
    appServicePlanId: useSharedAppServicePlan ? existingAppServicePlan.id : appServicePlan!.outputs.id
    managedIdentityId: managedIdentity.outputs.id
    managedIdentityPrincipalId: managedIdentity.outputs.principalId
    appSettings: {
      // Only essential Azure-specific settings
      APPLICATIONINSIGHTS_CONNECTION_STRING: monitoring.outputs.applicationInsightsConnectionString
    }
  }
}

// Monitoring - Log Analytics and Application Insights in the same resource group
module monitoring './core/monitor/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    location: location
    tags: tags
    applicationInsightsName: '${abbrs.insightsComponents}PoRedoImage-${resourceToken}'
    logAnalyticsName: '${abbrs.operationalInsightsWorkspaces}PoRedoImage-${resourceToken}'
  }
}

// Table Storage
module tableStorage './core/storage/storage-table.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    name: 'st${resourceToken}'
    location: location
    tags: tags
  }
}

// App outputs
output APPLICATIONINSIGHTS_CONNECTION_STRING string = monitoring.outputs.applicationInsightsConnectionString
output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output RESOURCE_GROUP_ID string = rg.id
output WEB_URI string = web.outputs.SERVICE_WEB_URI
