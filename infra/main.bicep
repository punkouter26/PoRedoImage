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

@description('Storage account location — should match the App Service plan region to avoid cross-region latency')
param storageLocation string = 'westus2'

@description('App Service Plan resource ID (shared plan in PoShared)')
param appServicePlanId string = '/subscriptions/bbb8dfbe-9169-432f-9b7a-fbf861b51037/resourceGroups/PoShared/providers/Microsoft.Web/serverfarms/asp-poshared-linux'

@description('Key Vault endpoint in the PoShared resource group')
param keyVaultEndpoint string = 'https://kv-poshared.vault.azure.net/'

// ─── Storage Account (Table Storage) ────────────────────────────────────────
// Standard_LRS is the lowest-cost tier for Table Storage.
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: storageLocation
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
      alwaysOn: true // B2 Basic tier supports Always On — prevents cold-start crashes
      appCommandLine: 'dotnet /home/site/wwwroot/PoRedoImage.Web.dll'
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
        {
          // Tell App Service which port .NET listens on — speeds up warmup probe
          name: 'WEBSITES_PORT'
          value: '8080'
        }
        {
          // Allow up to 10 minutes for cold starts (F1 with cert updates can be slow)
          name: 'WEBSITE_CONTAINER_START_TIME_LIMIT'
          value: '600'
        }
      ]
    }
  }
}

// ─── Outputs ────────────────────────────────────────────────────────────────
output appServiceDefaultHostName string = webApp.properties.defaultHostName
output appServicePrincipalId string = webApp.identity.principalId
output storageAccountName string = storageAccount.name
output webAppPrincipalId string = webApp.identity.principalId
output webAppDefaultHostName string = webApp.properties.defaultHostName

// NOTE: Key Vault role assignment (Key Vault Secrets User) is applied post-deploy via:
//   az role assignment create --role "Key Vault Secrets User" \
//     --assignee <webAppPrincipalId> \
//     --scope /subscriptions/bbb8dfbe-9169-432f-9b7a-fbf861b51037/resourceGroups/PoShared/providers/Microsoft.KeyVault/vaults/kv-poshared
