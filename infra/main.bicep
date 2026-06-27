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

@description('App Service name — must match AZURE_WEBAPP_NAME in .github/workflows/deploy.yml')
param appServiceName string = 'poredoimage-web'

@description('Storage account name (must be globally unique). Reuses the existing stporedoimage26 created 2026-02-07; change this only when migrating regions.')
param storageAccountName string = 'stporedoimage26'

@description('Storage account location — matches the existing stporedoimage26 account (eastus). Changing this fails with InvalidResourceLocation on an existing account.')
param storageLocation string = 'eastus'

@description('Azure subscription ID hosting the shared PoShared App Service Plan')
param subscriptionId string = subscription().subscriptionId

@description('App Service Plan resource ID — shared Basic B1 Linux plan in PoShared (westus2). Plan name must match EXPECTED_PLAN in .github/workflows/deploy.yml')
param appServicePlanId string = '/subscriptions/${subscriptionId}/resourceGroups/PoShared/providers/Microsoft.Web/serverfarms/asp-PoShared-b1'

@description('Key Vault endpoint in the PoShared resource group')
param keyVaultEndpoint string = 'https://kv-poshared.vault.azure.net/'

@description('Key Vault name in the PoShared resource group (used for Key Vault reference app settings)')
param keyVaultName string = 'kv-poshared'

@description('Azure OpenAI chat deployment name — must match a live deployment on po-aiservices-shared')
param openAiChatDeployment string = 'gpt-5.4-nano'

// Builds an App Service Key Vault reference for a secret in the shared vault.
// Resolved by the platform via the app's managed identity (which holds a get/list
// access policy on the access-policy-mode vault), populating config directly. This
// is the reliable secret path; the in-app AddAzureKeyVault provider is a fallback.
func kvRef(vaultName string, secretName string) string =>
  '@Microsoft.KeyVault(VaultName=${vaultName};SecretName=${secretName})'

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
// Uses the shared Basic B1 Linux plan (asp-PoShared-b1) from PoShared. A prior drift
// onto an F1 Free plan hit QuotaExceeded and disabled the site, so the plan binding is
// asserted post-deploy in CI. System-assigned managed identity is enabled for Key Vault access.
resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: appServiceName
  location: 'westus2' // must match the shared App Service Plan region
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
          // Allow up to 10 minutes for cold starts (cert updates can be slow)
          name: 'WEBSITE_CONTAINER_START_TIME_LIMIT'
          value: '600'
        }
        // ─── Secrets via Key Vault references ─────────────────────────────────
        // Names use '__' which the .NET config provider maps to ':' (e.g.
        // OpenAI__Endpoint → OpenAI:Endpoint). The startup secret validator
        // fail-fasts in Production if OpenAI:* or Google:ApiKey are missing.
        { name: 'OpenAI__Endpoint', value: kvRef(keyVaultName, 'PoRedoImage-OpenAI-Endpoint') }
        { name: 'OpenAI__Key', value: kvRef(keyVaultName, 'PoRedoImage-OpenAI-ApiKey') }
        // The chat deployment NAME is not a secret — a plain literal avoids Key Vault
        // reference caching (a stale 'gpt-4.1-nano' returned 404 DeploymentNotFound).
        // Must match a live deployment on po-aiservices-shared (currently gpt-5.4-nano).
        { name: 'OpenAI__ChatCompletionsDeployment', value: openAiChatDeployment }
        { name: 'Google__ApiKey', value: kvRef(keyVaultName, 'PoRedoImage-Google-ApiKey') }
        { name: 'Google__Imagen3Model', value: kvRef(keyVaultName, 'PoRedoImage-Google-Imagen3Model') }
        { name: 'ComputerVision__ApiKey', value: kvRef(keyVaultName, 'PoRedoImage-ComputerVision-ApiKey') }
        { name: 'ComputerVision__Endpoint', value: kvRef(keyVaultName, 'PoRedoImage-ComputerVision-Endpoint') }
        { name: 'ApplicationInsights__ConnectionString', value: kvRef(keyVaultName, 'PoRedoImage-ApplicationInsights-ConnectionString') }
        { name: 'Storage__ConnectionString', value: kvRef(keyVaultName, 'PoRedoImage-StorageConnectionString') }
        { name: 'AzureAd__ClientId', value: kvRef(keyVaultName, 'PoRedoImage-AzureAd-ClientId') }
        { name: 'AzureAd__ClientSecret', value: kvRef(keyVaultName, 'PoRedoImage-AzureAd-ClientSecret') }
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

// NOTE: Key Vault role assignment (Key Vault Secrets User) is applied post-deploy via the
// GitHub Actions workflow (deploy.yml). The pipeline derives the principalId from the freshly
// created web app and grants access at the vault scope, so a manual step is never required.
