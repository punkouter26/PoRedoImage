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

@description('Name of the App Service Plan to bind the web app to. Dedicated F1 (Free) plan for this app, living in the PoShared RG. It is in PoShared rather than PoRedoImage because this site’s webSpace is PoShared-WestUS2webspace-Linux — stamped at creation, unchanged by resource-group moves — and a site can only bind a plan in its own webspace (see ADR-031). Relocating the plan into PoRedoImage requires destroying and recreating the site.')
param appServicePlanName string = 'asp-PoRedoImage-f1'

@description('Resource group that owns the shared App Service Plan. Defaults to PoShared (the consolidation target).')
param appServicePlanResourceGroup string = 'PoShared'

@description('Region the web app is deployed into. Must match the home stamp of the shared App Service Plan. New plan is westus2; if the shared plan moves regions, update this.')
param webAppLocation string = 'westus2'

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

// ─── App Service Plan (existing, shared) ───────────────────────────────────
// The web app is bound to a SHARED App Service Plan (asp-PoShared-b1) owned by the
// PoShared resource group — consolidating the per-app B1 plans into one shared
// capacity pool (see ADR-031). This template only references the existing plan; it
// does NOT create, update, or delete it. The plan's home stamp is fixed by Azure at
// creation time, so the live poredoimage-web instance (still bound to its original
// in-RG plan on a different stamp) cannot be migrated without either an `az webapp
// clone` (new hostname) or a destroy+recreate. New deploys of a fresh web app to
// this template land on the shared plan.
resource sharedAppServicePlan 'Microsoft.Web/serverfarms@2024-04-01' existing = {
  name: appServicePlanName
  scope: resourceGroup(appServicePlanResourceGroup)
}

// ─── App Service ────────────────────────────────────────────────────────────
// Bound to the SHARED App Service Plan (asp-PoShared-b1 in RG PoShared) referenced
// above. System-assigned managed identity is enabled for Key Vault access. Note:
// for the existing live site, ARM keeps the existing serverFarmId if the live site
// is already bound to a different plan — see the shared-plan comment above for the
// stamp-affinity caveat that blocks automated re-parenting.
resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: appServiceName
  location: webAppLocation
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: sharedAppServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      // Must stay false: the plan is F1 (Free), which has no Always On, and ARM rejects the
      // whole deployment with Conflict/01020 rather than ignoring the flag. The cost is a cold
      // start after ~20 min idle.
      alwaysOn: false
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
