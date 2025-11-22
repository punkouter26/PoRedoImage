# Secrets Management Guide

## Overview

PoRedoImage uses **Azure Key Vault** for production secrets and **dotnet user-secrets** for local development. This ensures secure secret management while maintaining developer productivity.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Local Development                                      │
│  ├─ dotnet user-secrets (encrypted on disk)            │
│  ├─ appsettings.Development.json (Azurite config)      │
│  └─ No secrets in source control                       │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  Production (Azure)                                     │
│  ├─ Azure Key Vault (RBAC-secured)                     │
│  ├─ Managed Identity (passwordless authentication)     │
│  ├─ App Service Environment Variables                  │
│  └─ No secrets in appsettings.json                     │
└─────────────────────────────────────────────────────────┘
```

## Local Development Setup

### Step 1: Configure User Secrets

Run the provided PowerShell script:

```powershell
.\scripts\Configure-UserSecrets.ps1
```

Or manually configure secrets:

```powershell
cd Server  # or src/PoRedoImage.Api after restructuring

# Initialize user secrets (only needed once)
dotnet user-secrets init

# Add secrets one by one
dotnet user-secrets set "ApplicationInsights:ConnectionString" "InstrumentationKey=..."
dotnet user-secrets set "ConnectionStrings:AzureTableStorage" "UseDevelopmentStorage=true"
dotnet user-secrets set "ComputerVision:Endpoint" "https://..."
dotnet user-secrets set "ComputerVision:ApiKey" "your-key"
dotnet user-secrets set "OpenAI:Endpoint" "https://..."
dotnet user-secrets set "OpenAI:Key" "your-key"
```

### Step 2: Verify Secrets

```powershell
dotnet user-secrets list
```

### Step 3: Start Azurite (Local Storage Emulator)

```powershell
azurite --location ./AzuriteData
```

### Step 4: Run the Application

Press **F5** in VS Code or run:

```powershell
dotnet run --project Server
```

## Production Setup

### Step 1: Deploy Infrastructure

The Bicep templates automatically create and configure Key Vault:

```powershell
azd up
```

This creates:
- Azure Key Vault with RBAC authorization
- Managed Identity with Key Vault Secrets User role
- App Service configured to read from Key Vault

### Step 2: Add Secrets to Key Vault

Run the provided PowerShell script:

```powershell
.\scripts\Add-SecretsToKeyVault.ps1 -KeyVaultName "kv-xxxxx"
```

Or use Azure CLI:

```powershell
# Login to Azure
az login

# Set your subscription
az account set --subscription "your-subscription-id"

# Add secrets (note: use -- instead of : for nested keys)
az keyvault secret set --vault-name "kv-xxxxx" --name "ApplicationInsights--ConnectionString" --value "InstrumentationKey=..."
az keyvault secret set --vault-name "kv-xxxxx" --name "ConnectionStrings--AzureTableStorage" --value "DefaultEndpointsProtocol=https;..."
az keyvault secret set --vault-name "kv-xxxxx" --name "ComputerVision--Endpoint" --value "https://..."
az keyvault secret set --vault-name "kv-xxxxx" --name "ComputerVision--ApiKey" --value "your-key"
az keyvault secret set --vault-name "kv-xxxxx" --name "OpenAI--Endpoint" --value "https://..."
az keyvault secret set --vault-name "kv-xxxxx" --name "OpenAI--Key" --value "your-key"
az keyvault secret set --vault-name "kv-xxxxx" --name "OpenAI--ImageEndpoint" --value "https://..."
az keyvault secret set --vault-name "kv-xxxxx" --name "OpenAI--ImageKey" --value "your-key"
```

### Step 3: Configure App Service

The deployment automatically sets the `AZURE_KEY_VAULT_ENDPOINT` environment variable. Verify in Azure Portal:

```
Configuration > Application settings
AZURE_KEY_VAULT_ENDPOINT = https://kv-xxxxx.vault.azure.net/
```

## Secret Naming Conventions

### Key Vault Secret Names

Azure Key Vault does not support `:` in secret names. Use `--` (double dash) instead:

| Configuration Path | Key Vault Secret Name |
|-------------------|-----------------------|
| `ApplicationInsights:ConnectionString` | `ApplicationInsights--ConnectionString` |
| `ConnectionStrings:AzureTableStorage` | `ConnectionStrings--AzureTableStorage` |
| `ComputerVision:Endpoint` | `ComputerVision--Endpoint` |
| `ComputerVision:ApiKey` | `ComputerVision--ApiKey` |
| `OpenAI:Endpoint` | `OpenAI--Endpoint` |
| `OpenAI:Key` | `OpenAI--Key` |
| `OpenAI:ImageEndpoint` | `OpenAI--ImageEndpoint` |
| `OpenAI:ImageKey` | `OpenAI--ImageKey` |

The .NET configuration system automatically converts `--` to `:` when reading from Key Vault.

### User Secrets (Local Development)

Use `:` for hierarchy:

```powershell
dotnet user-secrets set "ApplicationInsights:ConnectionString" "value"
dotnet user-secrets set "ComputerVision:Endpoint" "value"
```

## How It Works

### Program.cs Logic

```csharp
if (builder.Environment.IsProduction())
{
    var keyVaultEndpoint = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
    
    if (!string.IsNullOrEmpty(keyVaultEndpoint))
    {
        var credential = new DefaultAzureCredential();
        builder.Configuration.AddAzureKeyVault(new Uri(keyVaultEndpoint), credential);
    }
}
```

### Configuration Precedence (Highest to Lowest)

1. **Environment Variables** (App Service settings)
2. **Azure Key Vault** (Production only)
3. **User Secrets** (Development only)
4. **appsettings.{Environment}.json**
5. **appsettings.json**

## Security Best Practices

### ✅ DO

- Use **user-secrets** for local development
- Use **Azure Key Vault** for production
- Use **Managed Identity** for authentication
- Enable **RBAC** on Key Vault (not access policies)
- Enable **soft delete** and **purge protection**
- Set up **cost alerts** for Key Vault operations
- Rotate secrets regularly
- Use different secrets for dev/staging/prod

### ❌ DON'T

- **Never** commit secrets to source control
- **Never** put real secrets in appsettings.json or appsettings.Development.json
- **Never** share Key Vault access keys
- **Never** disable Key Vault logging
- **Never** use the same secrets across environments

## Troubleshooting

### "Access denied" when reading from Key Vault

1. Verify the App Service Managed Identity is assigned the **Key Vault Secrets User** role
2. Check RBAC settings in Azure Portal: Key Vault > Access control (IAM)
3. Ensure the Managed Identity is enabled on the App Service

### Secrets not loading locally

1. Verify user-secrets are initialized: `dotnet user-secrets init`
2. List secrets: `dotnet user-secrets list`
3. Check that you're in the correct project directory
4. Ensure `UserSecretsId` is set in the .csproj file

### Key Vault endpoint not found

1. Check the `AZURE_KEY_VAULT_ENDPOINT` environment variable in App Service
2. Verify the Key Vault exists: `az keyvault show --name kv-xxxxx`
3. Check the output of `azd up` for the Key Vault URL

## References

- [Azure Key Vault Documentation](https://learn.microsoft.com/azure/key-vault/)
- [Safe storage of app secrets in development](https://learn.microsoft.com/aspnet/core/security/app-secrets)
- [Use Key Vault from App Service](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references)
- [DefaultAzureCredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential)
