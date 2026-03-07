<#
.SYNOPSIS
    Provisions Microsoft OAuth (App Registration) for PoRedoImage and stores credentials
    in Key Vault, optionally configuring the App Service.

.DESCRIPTION
    1. Creates an Azure AD App Registration (personal + work Microsoft accounts).
    2. Generates a client secret and stores it + the client ID in Key Vault (kv-poshared).
    3. Registers localhost and App Service redirect URIs.
    4. Optionally sets AzureAd:* app settings on the target App Service.

.PARAMETER WebAppName
    Optional: The App Service name (e.g. "app-poredoimage-prod").
    If omitted, App Service configuration is skipped.

.PARAMETER ResourceGroup
    Optional: Resource group for the App Service. Defaults to "rg-PoRedoImage-prod".

.PARAMETER KeyVaultName
    Key Vault name. Defaults to "kv-poshared".

.PARAMETER SecretExpiryYears
    How many years the client secret should be valid. Defaults to 2.

.EXAMPLE
    .\setup-azure-auth.ps1
    .\setup-azure-auth.ps1 -WebAppName "app-poredoimage-prod" -ResourceGroup "rg-PoRedoImage-prod"
#>
[CmdletBinding()]
param (
    [string]$WebAppName       = "",
    [string]$ResourceGroup    = "rg-PoRedoImage-prod",
    [string]$KeyVaultName     = "kv-poshared",
    [int]   $SecretExpiryYears = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ─── Helpers ────────────────────────────────────────────────────────────────────
function Write-Step { param([string]$msg) Write-Host "`n⟩ $msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$msg) Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn { param([string]$msg) Write-Host "  ⚠ $msg" -ForegroundColor Yellow }

# ─── 1. Verify Azure CLI login ───────────────────────────────────────────────────
Write-Step "Checking Azure CLI login..."
$account = az account show 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Warn "Not logged in. Running 'az login'..."
    az login
    if ($LASTEXITCODE -ne 0) { throw "Azure login failed." }
}
$accountInfo = $account | ConvertFrom-Json
$subscriptionId = $accountInfo.id
$subscriptionName = $accountInfo.name
Write-Ok "Logged in to: $subscriptionName ($subscriptionId)"

# ─── 2. Create App Registration ──────────────────────────────────────────────────
Write-Step "Creating App Registration 'PoRedoImage'..."

$appName = "PoRedoImage"
$existingApp = az ad app list --filter "displayName eq '$appName'" --query "[0].appId" -o tsv 2>$null
if (-not [string]::IsNullOrWhiteSpace($existingApp)) {
    Write-Warn "App Registration '$appName' already exists (appId: $existingApp). Reusing it."
    $appId = $existingApp
} else {
    $appJson = az ad app create `
        --display-name $appName `
        --sign-in-audience "AzureADandPersonalMicrosoftAccount" `
        --query "{appId:appId}" `
        -o json
    $app = $appJson | ConvertFrom-Json
    $appId = $app.appId
    Write-Ok "Created App Registration (appId: $appId)"
}

# ─── 3. Set redirect URIs ────────────────────────────────────────────────────────
Write-Step "Configuring redirect URIs..."
$redirectUris = @(
    "https://localhost:5001/signin-oidc",
    "http://localhost:5000/signin-oidc"
)
if (-not [string]::IsNullOrWhiteSpace($WebAppName)) {
    $redirectUris += "https://$WebAppName.azurewebsites.net/signin-oidc"
}
$uriList = $redirectUris -join " "

az ad app update --id $appId --web-redirect-uris $redirectUris | Out-Null
foreach ($uri in $redirectUris) { Write-Ok "  → $uri" }

# ─── 4. Generate client secret ───────────────────────────────────────────────────
Write-Step "Generating client secret (valid $SecretExpiryYears year(s))..."
$endDate = (Get-Date).AddYears($SecretExpiryYears).ToString("yyyy-MM-dd")
$credJson = az ad app credential reset `
    --id $appId `
    --end-date $endDate `
    --query "{password:password,tenant:tenant}" `
    -o json
$cred = $credJson | ConvertFrom-Json
$clientSecret = $cred.password
$tenantId = $cred.tenant
Write-Ok "Client secret generated (expires: $endDate)"
Write-Ok "Tenant ID: $tenantId"

# ─── 5. Store secrets in Key Vault ───────────────────────────────────────────────
Write-Step "Storing secrets in Key Vault '$KeyVaultName'..."
az keyvault secret set `
    --vault-name $KeyVaultName `
    --name "PoRedoImage-AzureAd-ClientId" `
    --value $appId `
    --output none
Write-Ok "Stored 'PoRedoImage-AzureAd-ClientId'"

az keyvault secret set `
    --vault-name $KeyVaultName `
    --name "PoRedoImage-AzureAd-ClientSecret" `
    --value $clientSecret `
    --output none
Write-Ok "Stored 'PoRedoImage-AzureAd-ClientSecret'"

# ─── 6. App Service configuration (optional) ─────────────────────────────────────
if (-not [string]::IsNullOrWhiteSpace($WebAppName)) {
    Write-Step "Configuring App Service '$WebAppName' in '$ResourceGroup'..."
    az webapp config appsettings set `
        --name $WebAppName `
        --resource-group $ResourceGroup `
        --settings `
            "AzureAd__Instance=https://login.microsoftonline.com/" `
            "AzureAd__TenantId=common" `
            "AzureAd__ClientId=$appId" `
            "AzureAd__CallbackPath=/signin-oidc" `
            "AzureAd__SignedOutCallbackPath=/signout-oidc" `
        --output none
    Write-Ok "App Service settings updated (ClientSecret is loaded from Key Vault at runtime)"
} else {
    Write-Warn "No -WebAppName specified — App Service configuration skipped."
}

# ─── 7. Local dev secrets ────────────────────────────────────────────────────────
Write-Step "Setting local dotnet user-secrets (for dev testing with real OAuth)..."
$projectPath = Join-Path $PSScriptRoot ".." "src" "PoRedoImage.Web"
if (Test-Path $projectPath) {
    dotnet user-secrets set "AzureAd:ClientId"     $appId         --project $projectPath | Out-Null
    dotnet user-secrets set "AzureAd:ClientSecret" $clientSecret  --project $projectPath | Out-Null
    dotnet user-secrets set "AzureAd:TenantId"     $tenantId      --project $projectPath | Out-Null
    Write-Ok "user-secrets updated in $projectPath"
} else {
    Write-Warn "Project path not found: $projectPath — user-secrets not set locally."
}

# ─── Summary ─────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host "  Setup complete!" -ForegroundColor White
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host "  App ID     : $appId" -ForegroundColor White
Write-Host "  Tenant ID  : $tenantId" -ForegroundColor White
Write-Host "  Key Vault  : $KeyVaultName" -ForegroundColor White
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Yellow
Write-Host "    1. In dev (ASPNETCORE_ENVIRONMENT=Development), the app uses" -ForegroundColor Gray
Write-Host "       cookie-only auth with /dev-login — no OAuth needed locally." -ForegroundColor Gray
Write-Host "    2. To test real Microsoft login locally, set ASPNETCORE_ENVIRONMENT=" -ForegroundColor Gray
Write-Host "       'Production' and ensure user-secrets are configured above." -ForegroundColor Gray
Write-Host "    3. In production, secrets are loaded from Key Vault at startup." -ForegroundColor Gray
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
