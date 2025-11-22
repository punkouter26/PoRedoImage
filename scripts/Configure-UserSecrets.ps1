# PoRedoImage User Secrets Configuration
# This script helps configure dotnet user-secrets for local development

Write-Host "Configuring user secrets for PoRedoImage..." -ForegroundColor Cyan
Write-Host ""

# Navigate to Server project
$serverPath = Join-Path $PSScriptRoot "..\src\PoRedoImage.Api"

if (-not (Test-Path $serverPath)) {
    # Fallback to current structure
    $serverPath = Join-Path $PSScriptRoot "..\Server"
}

Write-Host "Project Path: $serverPath" -ForegroundColor Yellow
Set-Location $serverPath

# Initialize user secrets if not already done
Write-Host "Initializing user secrets..." -ForegroundColor Green
dotnet user-secrets init

# Set Application Insights
Write-Host "`nConfiguring Application Insights..." -ForegroundColor Green
$appInsightsConnectionString = Read-Host "Enter Application Insights Connection String (or press Enter to skip)"
if ($appInsightsConnectionString) {
    dotnet user-secrets set "ApplicationInsights:ConnectionString" $appInsightsConnectionString
}

# Set Azure Table Storage
Write-Host "`nConfiguring Azure Table Storage..." -ForegroundColor Green
Write-Host "For local development, use: UseDevelopmentStorage=true" -ForegroundColor Yellow
$tableStorageConnection = Read-Host "Enter Table Storage Connection String [UseDevelopmentStorage=true]"
if (-not $tableStorageConnection) {
    $tableStorageConnection = "UseDevelopmentStorage=true"
}
dotnet user-secrets set "ConnectionStrings:AzureTableStorage" $tableStorageConnection

# Set Computer Vision
Write-Host "`nConfiguring Computer Vision..." -ForegroundColor Green
$cvEndpoint = Read-Host "Enter Computer Vision Endpoint"
$cvApiKey = Read-Host "Enter Computer Vision API Key" -AsSecureString
if ($cvEndpoint -and $cvApiKey) {
    dotnet user-secrets set "ComputerVision:Endpoint" $cvEndpoint
    $cvApiKeyPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($cvApiKey))
    dotnet user-secrets set "ComputerVision:ApiKey" $cvApiKeyPlain
}

# Set OpenAI
Write-Host "`nConfiguring OpenAI..." -ForegroundColor Green
$openAiEndpoint = Read-Host "Enter OpenAI Endpoint"
$openAiKey = Read-Host "Enter OpenAI Key" -AsSecureString
if ($openAiEndpoint -and $openAiKey) {
    dotnet user-secrets set "OpenAI:Endpoint" $openAiEndpoint
    $openAiKeyPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($openAiKey))
    dotnet user-secrets set "OpenAI:Key" $openAiKeyPlain
    dotnet user-secrets set "OpenAI:ChatModel" "gpt-4o"
    dotnet user-secrets set "OpenAI:ImageModel" "dall-e-3"
    dotnet user-secrets set "OpenAI:ChatCompletionsDeployment" "gpt-4o"
    dotnet user-secrets set "OpenAI:ImageGenerationDeployment" "dall-e-3"
}

Write-Host "`n✓ User secrets configured successfully!" -ForegroundColor Green
Write-Host "`nTo view all secrets, run: dotnet user-secrets list" -ForegroundColor Cyan
Write-Host "To remove a secret, run: dotnet user-secrets remove <key>" -ForegroundColor Cyan
Write-Host "To clear all secrets, run: dotnet user-secrets clear" -ForegroundColor Cyan
