<#
.SYNOPSIS
    PoRedoImage — one-command local development setup.

.DESCRIPTION
    Installs prerequisites via Winget, starts Azurite via Docker Compose,
    and writes placeholder local secrets to appsettings.Development.json.
    Run once after cloning on a new machine.

.PREREQUISITES
    - Windows 10/11 with Winget available (App Installer)
    - Internet access for Winget downloads
    - PowerShell 7+

.USAGE
    .\SCRIPTS\setup.ps1
#>

#Requires -Version 7

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path $PSScriptRoot -Parent

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Assert-CommandExists([string]$Command, [string]$InstallHint) {
    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) {
        Write-Warning "'$Command' not found. $InstallHint"
        return $false
    }
    return $true
}

# ── 1. Prerequisites via Winget ──────────────────────────────────────
Write-Step "Checking prerequisites"

$wingetPackages = @(
    @{ Id = 'Microsoft.DotNet.SDK.10'; Name = '.NET 10 SDK' },
    @{ Id = 'Docker.DockerDesktop';    Name = 'Docker Desktop' },
    @{ Id = 'Git.Git';                 Name = 'Git' },
    @{ Id = 'Microsoft.NodeJS.LTS';    Name = 'Node.js LTS' }
)

if (Assert-CommandExists 'winget' 'Install App Installer from the Microsoft Store.') {
    foreach ($pkg in $wingetPackages) {
        Write-Host "  Installing $($pkg.Name)..." -ForegroundColor Gray
        winget install --id $pkg.Id --silent --accept-source-agreements --accept-package-agreements 2>&1 |
            Where-Object { $_ -notmatch '^$' } |
            ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    }
} else {
    Write-Warning "Winget not available — install prerequisites manually."
}

# ── 2. Docker / Azurite ──────────────────────────────────────────────
Write-Step "Starting Azurite via Docker Compose"

if (Assert-CommandExists 'docker' 'Install Docker Desktop from https://www.docker.com/products/docker-desktop/') {
    Push-Location $Root
    try {
        docker compose up -d
        Write-Host "  Azurite started on default ports (Blob: 10000, Queue: 10001, Table: 10002)" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Warning "Docker not found — start Azurite manually before running the app."
}

# ── 3. Restore NuGet packages ────────────────────────────────────────
Write-Step "Restoring NuGet packages"

if (Assert-CommandExists 'dotnet' 'Install .NET 10 SDK from https://dotnet.microsoft.com/download') {
    dotnet restore "$Root/PoRedoImage.slnx"
}

# ── 4. Install Playwright browsers (E2E tests) ───────────────────────
Write-Step "Installing Playwright browsers"

if (Assert-CommandExists 'npx' 'Install Node.js LTS from https://nodejs.org/') {
    $playwrightDir = Join-Path $Root 'tests/playwright'
    if (Test-Path $playwrightDir) {
        Push-Location $playwrightDir
        try {
            npm install
            npx playwright install --with-deps chromium
        }
        finally {
            Pop-Location
        }
    }
}

# ── 4b. Azure CLI auth check (Key Vault access) ──────────────────────
Write-Step "Checking Azure CLI authentication"

if (Assert-CommandExists 'az' 'Install Azure CLI from https://aka.ms/installazurecli') {
    $account = az account show --query user.name -o tsv 2>$null
    if ([string]::IsNullOrWhiteSpace($account)) {
        Write-Warning "Not logged in to Azure CLI. Run 'az login' so the app can read secrets from Key Vault (kv-poshared)."
    } else {
        Write-Host "  Signed in as $account" -ForegroundColor Green
        Write-Host "  Verifying Key Vault access (kv-poshared)..." -ForegroundColor Gray
        az keyvault secret list --vault-name kv-poshared --query "length(@)" -o tsv 2>$null |
            ForEach-Object { Write-Host "    Key Vault reachable — $_ secrets visible." -ForegroundColor Green }
    }
}

# ── 5. Local secret placeholder keys ─────────────────────────────────
Write-Step "Verifying appsettings.Development.json"

$devSettings = Join-Path $Root 'src/PoRedoImage.Web/appsettings.Development.json'
if (Test-Path $devSettings) {
    Write-Host "  $devSettings already exists — not overwritten." -ForegroundColor Green
    Write-Host "  Populate secrets from Azure Key Vault (kv-poshared) or fill in manually." -ForegroundColor Gray
} else {
    Write-Warning "appsettings.Development.json not found at expected path: $devSettings"
}

# ── Done ─────────────────────────────────────────────────────────────
Write-Host "`n✓ Setup complete. Next steps:" -ForegroundColor Green
Write-Host "  1. Ensure Docker Desktop is running and Azurite container is healthy:" -ForegroundColor Gray
Write-Host "       docker compose ps" -ForegroundColor DarkGray
Write-Host "  2. Open PoRedoImage.slnx in VS Code and press F5 to launch." -ForegroundColor Gray
Write-Host "  3. Click 'Continue as GUEST' on the login page for a zero-config dev session." -ForegroundColor Gray
