<#
.SYNOPSIS
    Moves Azure Computer Vision to a region that supports Caption and DenseCaptions.

.DESCRIPTION
    The Image Analysis 4.0 Caption and DenseCaptions features are only available in a subset of
    regions. Outside them the API returns 400 and AzureVisionService falls back to synthesising a
    description from the top eight tags — "A photo showing person, clothing, food, man, indoor,
    fast food, wall, meal" — which is a keyword list, not a scene.

    This provisions a Computer Vision resource in a supporting region and repoints the app at it.

    No application code or bicep change is needed: ComputerVision__Endpoint and
    ComputerVision__ApiKey are Key Vault references (infra/main.bicep), so updating the two secrets
    is the entire migration. The old resource is left intact so a rollback is just re-running the
    secret update with the previous values.

.PARAMETER Location
    Target region. Must be one that supports Caption/DenseCaptions.

.PARAMETER ResourceGroup
    Resource group for the new account. Defaults to the app's group.

.PARAMETER AccountName
    Name for the new Computer Vision account.

.PARAMETER KeyVaultName
    Key Vault holding the app's secrets.

.PARAMETER WhatIf
    Print the actions without performing them.

.EXAMPLE
    pwsh SCRIPTS/migrate-vision-region.ps1 -Location eastus

.EXAMPLE
    pwsh SCRIPTS/migrate-vision-region.ps1 -Location westeurope -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet('eastus', 'eastus2', 'westus', 'westus2', 'westeurope', 'northeurope',
                 'southeastasia', 'eastasia', 'japaneast', 'australiaeast')]
    [string]$Location,

    [string]$ResourceGroup = 'PoRedoImage',
    [string]$AccountName = "cv-poredoimage-$Location",
    [string]$KeyVaultName = 'kv-poshared'
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string]$Message) Write-Host "==> $Message" -ForegroundColor Cyan }

Write-Step "Checking Azure CLI login"
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) { throw "Not logged in. Run 'az login' first." }
Write-Host "    Subscription: $($account.name)"

Write-Step "Ensuring resource group '$ResourceGroup' exists"
if (-not (az group exists --name $ResourceGroup | ConvertFrom-Json)) {
    throw "Resource group '$ResourceGroup' not found. Create it or pass -ResourceGroup."
}

Write-Step "Creating Computer Vision account '$AccountName' in $Location"
if ($PSCmdlet.ShouldProcess($AccountName, "Create ComputerVision account in $Location")) {
    # S1 is required: the free F0 tier does not serve Caption/DenseCaptions.
    az cognitiveservices account create `
        --name $AccountName `
        --resource-group $ResourceGroup `
        --kind ComputerVision `
        --sku S1 `
        --location $Location `
        --yes `
        --output none
    Write-Host "    Created."
}

Write-Step "Reading endpoint and key"
$endpoint = $null
$key = $null
if ($PSCmdlet.ShouldProcess($AccountName, "Read endpoint and key")) {
    $endpoint = az cognitiveservices account show `
        --name $AccountName --resource-group $ResourceGroup `
        --query 'properties.endpoint' --output tsv
    $key = az cognitiveservices account keys list `
        --name $AccountName --resource-group $ResourceGroup `
        --query 'key1' --output tsv
    Write-Host "    Endpoint: $endpoint"
    Write-Host "    Key: $($key.Substring(0,4))$('*' * 28)"
}

Write-Step "Updating Key Vault secrets in '$KeyVaultName'"
if ($PSCmdlet.ShouldProcess($KeyVaultName, "Update ComputerVision secrets")) {
    az keyvault secret set --vault-name $KeyVaultName `
        --name 'PoRedoImage-ComputerVision-Endpoint' --value $endpoint --output none
    az keyvault secret set --vault-name $KeyVaultName `
        --name 'PoRedoImage-ComputerVision-ApiKey' --value $key --output none
    Write-Host "    Both secrets updated. Previous versions remain in Key Vault for rollback."
}

Write-Step "Verifying Caption support in the new region"
if ($PSCmdlet.ShouldProcess($endpoint, "Probe Caption feature")) {
    # 1x1 PNG. A 400 mentioning the feature means the region still does not support it; a 200 or a
    # size-related 400 means Caption itself was accepted.
    $pixel = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==')
    $uri = "$($endpoint.TrimEnd('/'))/computervision/imageanalysis:analyze?api-version=2024-02-01&features=caption,denseCaptions"
    try {
        Invoke-RestMethod -Uri $uri -Method Post -Body $pixel `
            -ContentType 'application/octet-stream' `
            -Headers @{ 'Ocp-Apim-Subscription-Key' = $key } -ErrorAction Stop | Out-Null
        Write-Host "    Caption + DenseCaptions accepted." -ForegroundColor Green
    } catch {
        $body = $_.ErrorDetails.Message
        if ($body -and $body -match 'InvalidImageSize|image is too small') {
            # The feature was accepted; only the 1x1 probe image was rejected.
            Write-Host "    Caption + DenseCaptions accepted (probe image too small, as expected)." -ForegroundColor Green
        } elseif ($body -and $body -match 'NotSupported|not supported') {
            Write-Warning "    This region rejected Caption/DenseCaptions. Pick a different -Location."
        } else {
            Write-Warning "    Probe inconclusive: $body"
        }
    }
}

Write-Step "Done"
Write-Host @"
    Restart the app so it re-resolves the Key Vault references:
      az webapp restart -g $ResourceGroup -n poredoimage-web

    Then confirm the fallback is gone — this should return NOTHING:
      az webapp log tail -g $ResourceGroup -n poredoimage-web | Select-String 'Caption not supported'

    To roll back, set the two secrets back to the previous resource's values;
    old versions are retained in Key Vault.
"@ -ForegroundColor Gray
