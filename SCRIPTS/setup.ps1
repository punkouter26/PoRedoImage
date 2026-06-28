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

# Stop any npm/global Azurite first — it binds 10000-10002 and would make `docker compose up`
# fail with a port conflict (both emulators fighting over the same ports + .azurite-data files).
$npmAzurite = Get-CimInstance Win32_Process -Filter "name='node.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*azurite*' }
if ($npmAzurite) {
    Write-Host "  Stopping non-Docker (npm) Azurite to free ports 10000-10002..." -ForegroundColor Yellow
    $npmAzurite | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

if (Assert-CommandExists 'docker' 'Install Docker Desktop from https://www.docker.com/products/docker-desktop/') {
    Push-Location $Root
    try {
        docker compose up -d
        Write-Host "  Azurite started on default ports (Blob: 10000, Queue: 10001, Table: 10002)" -ForegroundColor Green

        # Post-start health check: confirm the Table endpoint actually answers (a 400/403 proves
        # the listener is up; a connection failure means the container did not come healthy).
        Start-Sleep -Seconds 2
        try {
            Invoke-WebRequest -Uri 'http://127.0.0.1:10002/devstoreaccount1' -TimeoutSec 5 -SkipHttpErrorCheck |
                Out-Null
            Write-Host "  Azurite Table endpoint is responding on 127.0.0.1:10002." -ForegroundColor Green
        } catch {
            Write-Warning "  Azurite Table endpoint did not respond on 127.0.0.1:10002 — check 'docker compose logs azurite'."
        }
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

# ── 4. Playwright browsers (C# E2E suite) ───────────────────────────
# The browser installer (`playwright.ps1`) is emitted only after the E2E project is built.
# Build it (Release), then install Chromium so the E2EUI suite runs instead of self-skipping.
Write-Step "Installing Playwright browsers (Chromium) for the E2E UI suite"

if (Get-Command 'dotnet' -ErrorAction SilentlyContinue) {
    $e2eProj = Join-Path $Root 'tests/PoRedoImage.Tests.E2E/PoRedoImage.Tests.E2E.csproj'
    dotnet build $e2eProj -c Release --nologo | Out-Null
    $playwrightScript = Join-Path $Root 'tests/PoRedoImage.Tests.E2E/bin/Release/net10.0/playwright.ps1'
    if (Test-Path $playwrightScript) {
        pwsh $playwrightScript install chromium
        Write-Host "  Chromium installed for Playwright." -ForegroundColor Green
    } else {
        Write-Warning "  playwright.ps1 not found after build — run it manually: pwsh $playwrightScript install chromium"
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
    # Bootstrap a minimal placeholder so the app boots locally even before Key Vault access is set
    # up. Storage points at the Azurite container; AI keys are blank (set Mocks:UseMockAi=true to
    # run fully offline with the mock services + "USING MOCK DATA" banner).
    Write-Host "  Creating placeholder $devSettings ..." -ForegroundColor Yellow
    $placeholder = [ordered]@{
        Storage = [ordered]@{ ConnectionString = 'UseDevelopmentStorage=true' }
        Mocks   = [ordered]@{ UseMockAi = $false }
        Auth    = [ordered]@{ EnableFakeAuth = $false }
        OpenAI  = [ordered]@{ Endpoint = ''; Key = '' }
        ComputerVision = [ordered]@{ Endpoint = ''; ApiKey = '' }
        Google  = [ordered]@{ ApiKey = '' }
    }
    $placeholder | ConvertTo-Json -Depth 5 | Set-Content -Path $devSettings -Encoding utf8
    Write-Host "  Wrote placeholder dev settings. Fill in real secrets or set Mocks:UseMockAi=true." -ForegroundColor Green
}

# ── Done ─────────────────────────────────────────────────────────────
Write-Host "`n✓ Setup complete. Next steps:" -ForegroundColor Green
Write-Host "  1. Ensure Docker Desktop is running and Azurite container is healthy:" -ForegroundColor Gray
Write-Host "       docker compose ps" -ForegroundColor DarkGray
Write-Host "  2. Open PoRedoImage.slnx in VS Code and press F5 to launch." -ForegroundColor Gray
Write-Host "  3. Click 'Continue as GUEST' on the login page for a zero-config dev session." -ForegroundColor Gray
