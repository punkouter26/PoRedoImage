// ─── PoRedoImage Infrastructure ──────────────────────────────────────────────
// Provisions the app-specific resources for PoRedoImage.
// Shared services (Key Vault, OpenAI, App Insights) live in the PoShared RG.
//
// Usage:
//   az deployment group create -g PoRedoImage -f infra/main.bicep
// ─────────────────────────────────────────────────────────────────────────────

targetScope = 'resourceGroup'

@description('Azure region for resources')
param location string = resourceGroup().location

@description('App Service name')
param appServiceName string = 'poredoimage-web'

@description('Storage account name (must be globally unique)')
param storageAccountName string = 'stporedoimage26'

@description('App Service Plan resource ID (shared plan in PoShared)')
param appServicePlanId string = '/subscriptions/bbb8dfbe-9169-432f-9b7a-fbf861b51037/resourceGroups/PoShared/providers/Microsoft.Web/serverfarms/asp-poshared-linux'

@description('Key Vault endpoint in the PoShared resource group')
param keyVaultEndpoint string = 'https://kv-poshared.vault.azure.net/'

// ─── Storage Account (Table Storage) ────────────────────────────────────────
// Standard_LRS is the lowest-cost tier for Table Storage.
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

// ─── Table Service (enabled on the storage account) ─────────────────────────
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

// ─── App Service ────────────────────────────────────────────────────────────
// Uses the shared Linux plan from PoShared (F1 free tier).
// System-assigned managed identity is enabled for Key Vault access.
resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: appServiceName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false // Free/shared tier does not support alwaysOn
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'AZURE_KEY_VAULT_ENDPOINT'
          value: keyVaultEndpoint
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
      ]
    }
  }
}

// ─── Outputs ────────────────────────────────────────────────────────────────
output appServiceDefaultHostName string = webApp.properties.defaultHostName
output appServicePrincipalId string = webApp.identity.principalId
output storageAccountName string = storageAccount.name
