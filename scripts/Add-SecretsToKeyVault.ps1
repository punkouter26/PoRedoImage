# Add Secrets to Azure Key Vault
# This script uploads all application secrets to Azure Key Vault

param(
    [Parameter(Mandatory=$true)]
    [string]$KeyVaultName,
    
    [Parameter(Mandatory=$false)]
    [string]$ResourceGroup = "PoRedoImage"
)

Write-Host "Adding secrets to Azure Key Vault: $KeyVaultName" -ForegroundColor Cyan
Write-Host ""

# Check if logged in to Azure
$context = az account show 2>$null
if (-not $context) {
    Write-Host "Not logged in to Azure. Logging in..." -ForegroundColor Yellow
    az login
}

# Function to add secret to Key Vault
function Add-KeyVaultSecret {
    param(
        [string]$SecretName,
        [string]$SecretValue,
        [string]$Description
    )
    
    if ($SecretValue) {
        Write-Host "Adding secret: $SecretName" -ForegroundColor Green
        az keyvault secret set --vault-name $KeyVaultName --name $SecretName --value $SecretValue --description $Description
    } else {
        Write-Host "Skipping $SecretName (no value provided)" -ForegroundColor Yellow
    }
}

# Application Insights
Write-Host "`nApplication Insights Secrets:" -ForegroundColor Cyan
$appInsightsConnectionString = Read-Host "Enter Application Insights Connection String"
Add-KeyVaultSecret -SecretName "ApplicationInsights--ConnectionString" -SecretValue $appInsightsConnectionString -Description "Application Insights connection string for telemetry"

# Azure Table Storage
Write-Host "`nAzure Table Storage Secrets:" -ForegroundColor Cyan
$tableStorageConnection = Read-Host "Enter Azure Table Storage Connection String"
Add-KeyVaultSecret -SecretName "ConnectionStrings--AzureTableStorage" -SecretValue $tableStorageConnection -Description "Azure Table Storage connection string"

# Computer Vision
Write-Host "`nComputer Vision Secrets:" -ForegroundColor Cyan
$cvEndpoint = Read-Host "Enter Computer Vision Endpoint"
$cvApiKey = Read-Host "Enter Computer Vision API Key" -AsSecureString
$cvApiKeyPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($cvApiKey))

Add-KeyVaultSecret -SecretName "ComputerVision--Endpoint" -SecretValue $cvEndpoint -Description "Computer Vision API endpoint"
Add-KeyVaultSecret -SecretName "ComputerVision--ApiKey" -SecretValue $cvApiKeyPlain -Description "Computer Vision API key"

# OpenAI
Write-Host "`nOpenAI Secrets:" -ForegroundColor Cyan
$openAiEndpoint = Read-Host "Enter OpenAI Endpoint"
$openAiKey = Read-Host "Enter OpenAI Key" -AsSecureString
$openAiKeyPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($openAiKey))

Add-KeyVaultSecret -SecretName "OpenAI--Endpoint" -SecretValue $openAiEndpoint -Description "Azure OpenAI endpoint"
Add-KeyVaultSecret -SecretName "OpenAI--Key" -SecretValue $openAiKeyPlain -Description "Azure OpenAI API key"

# OpenAI Image Generation (if different)
$useImageEndpoint = Read-Host "Use separate Image Generation endpoint? (y/n)"
if ($useImageEndpoint -eq 'y') {
    $openAiImageEndpoint = Read-Host "Enter OpenAI Image Endpoint"
    $openAiImageKey = Read-Host "Enter OpenAI Image Key" -AsSecureString
    $openAiImageKeyPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($openAiImageKey))
    
    Add-KeyVaultSecret -SecretName "OpenAI--ImageEndpoint" -SecretValue $openAiImageEndpoint -Description "Azure OpenAI image generation endpoint"
    Add-KeyVaultSecret -SecretName "OpenAI--ImageKey" -SecretValue $openAiImageKeyPlain -Description "Azure OpenAI image generation key"
}

Write-Host "`n✓ Secrets added to Key Vault successfully!" -ForegroundColor Green
Write-Host "`nTo view secrets, run: az keyvault secret list --vault-name $KeyVaultName" -ForegroundColor Cyan
Write-Host "To get a secret value, run: az keyvault secret show --vault-name $KeyVaultName --name <secret-name>" -ForegroundColor Cyan
