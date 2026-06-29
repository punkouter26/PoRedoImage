// ─── PoRedoImage Infrastructure ──────────────────────────────────────────────
// Provisions the app-specific resources for PoRedoImage.
// Shared services (Key Vault, OpenAI, App Insights) live in the PoShared RG.
//
// Usage:
//   az deployment group create -g PoRedoImage -f infra/main.bicep
// ─────────────────────────────────────────────────────────────────────────────

targetScope = 'resourceGroup'

@description('App Service name — must match AZURE_WEBAPP_NAME in .github/workflows/deploy.yml')
param appServiceName string = 'poredoimage-web'

@description('Storage account name (must be globally unique). Reuses the existing stporedoimage26 created 2026-02-07; change this only when migrating regions.')
param storageAccountName string = 'stporedoimage26'

@description('Storage account location — matches the existing stporedoimage26 account (eastus). Changing this fails with InvalidResourceLocation on an existing account.')
param storageLocation string = 'eastus'

@description('App Service Plan name — F1 Free Linux plan in this resource group. Must match EXPECTED_PLAN in .github/workflows/deploy.yml')
param appServicePlanName string = 'asp-poredoimage-f1'

@description('App Service Plan region — westus2. East US has no free-tier (F1) Linux VM quota on this subscription, so the plan and web app both live in westus2.')
param appServicePlanLocation string = 'westus2'

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

// ─── Blob Service + soft-delete retention ───────────────────────────────────
// 7-day soft-delete window guards against accidental deletes without bloating cost.
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

// ─── Storage lifecycle management (cloud-waste reduction) ────────────────────
// Audit item: "Auto-delete/archive blobs/logs older than 30 days." Generated user
// images and Kudu/app log blobs are regenerable, so we tier them to Cool after 7 days
// (cheaper storage) and delete after 30 days. This caps unbounded blob growth — the
// single clearest cost leak, since nothing previously expired stored images.
resource lifecyclePolicy 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'expire-generated-blobs-30d'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [ 'blockBlob' ]
            }
            actions: {
              baseBlob: {
                tierToCool: {
                  daysAfterModificationGreaterThan: 7
                }
                delete: {
                  daysAfterModificationGreaterThan: 30
                }
              }
            }
          }
        }
      ]
    }
  }
}

// ─── App Service Plan ───────────────────────────────────────────────────────
// F1 Free Linux plan, owned by this resource group. Lives in westus2 because the
// subscription has no free-tier Linux VM quota in eastus (the RG region).
// NOTE: F1 Free does NOT support Always On and caps CPU at 60 min/day — fine for a
// low-traffic app, but expect cold starts. Cold starts are an ACCEPTED trade-off (no keep-warm
// pinger — it would burn the 60-min CPU budget); see docs/ADR_Log.md ADR-015. The plan binding
// is asserted post-deploy in CI.
resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: appServicePlanLocation
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  properties: {
    reserved: true // reserved == true marks this as a Linux plan
  }
}

// ─── App Service ────────────────────────────────────────────────────────────
// Bound to the F1 Free Linux plan (asp-poredoimage-f1) above. System-assigned
// managed identity is enabled for Key Vault access.
resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: appServiceName
  location: appServicePlanLocation // must match the App Service Plan region
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false // F1 Free does not support Always On; must be false or deploy fails
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
        // Telemetry budget (audit item: "aggressive App Insights sampling"). Set EXPLICITLY here so the
        // production sampling ratio is auditable in the portal rather than relying on the 0.1 code default
        // in HostBootstrapExtensions.AddPoRedoImageTelemetry. ErrorPreservingSampler keeps all error spans
        // and heartbeat/exception telemetry regardless of this ratio.
        { name: 'ApplicationInsights__SamplingRatio', value: '0.1' }
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
// GitHub Actions workflow (deploy.yml → "Bind Key Vault Secrets User role"). The workflow
// derives the principalId from the freshly created web app and runs `az role assignment
// create` against the vault in the PoShared RG. A previous Bicep-module attempt was rolled
// back (PR #51) because a subscription-scoped Bicep deployment requires the CI service
// principal to hold Microsoft.Resources/deployments/validate/action at the subscription
// scope, which it does not — see ADR-023.
