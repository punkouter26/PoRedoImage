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
# Idempotent: each package declares a probe (a command + optional version test). If the probe
# already passes, the winget install is skipped entirely — so a re-run on a provisioned machine
# is a fast series of no-ops instead of four silent reinstall attempts.
Write-Step "Checking prerequisites"

# Returns $true when a .NET 10.x SDK is already installed.
function Test-DotNet10 {
    if (-not (Get-Command 'dotnet' -ErrorAction SilentlyContinue)) { return $false }
    return [bool]((dotnet --list-sdks 2>$null) -match '^10\.')
}

$wingetPackages = @(
    @{ Id = 'Microsoft.DotNet.SDK.10'; Name = '.NET 10 SDK'; Probe = { Test-DotNet10 } },
    @{ Id = 'Docker.DockerDesktop';    Name = 'Docker Desktop'; Probe = { [bool](Get-Command 'docker' -ErrorAction SilentlyContinue) } },
    @{ Id = 'Git.Git';                 Name = 'Git'; Probe = { [bool](Get-Command 'git' -ErrorAction SilentlyContinue) } },
    @{ Id = 'Microsoft.NodeJS.LTS';    Name = 'Node.js LTS'; Probe = { [bool](Get-Command 'node' -ErrorAction SilentlyContinue) } }
)

if (Assert-CommandExists 'winget' 'Install App Installer from the Microsoft Store.') {
    foreach ($pkg in $wingetPackages) {
        if (& $pkg.Probe) {
            Write-Host "  $($pkg.Name) already present — skipping." -ForegroundColor Green
            continue
        }
        Write-Host "  Installing $($pkg.Name)..." -ForegroundColor Gray
        winget install --id $pkg.Id --silent --accept-source-agreements --accept-package-agreements 2>&1 |
            Where-Object { $_ -notmatch '^$' } |
            ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    }
} else {
    Write-Warning "Winget not available — install prerequisites manually."
}

# ── 2. Docker / Azurite ──────────────────────────────────────────────
# This Azurite container is for the LOCAL DEV INNER LOOP only (F5 / `dotnet run`). The test suites
# spin up their own ephemeral Azurite via Testcontainers, so they never touch this container — a
# stale dev container can't leak state into a test run. See ADR-019.
Write-Step "Starting Azurite via Docker Compose (local dev inner loop only)"

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
# Idempotent: Playwright caches browsers under %USERPROFILE%\AppData\Local\ms-playwright. If a
# chromium build is already cached, skip the (slow) Release build + reinstall entirely.
Write-Step "Installing Playwright browsers (Chromium) for the E2E UI suite"

$chromiumCached = $false
$pwCache = Join-Path $env:LOCALAPPDATA 'ms-playwright'
if (Test-Path $pwCache) {
    $chromiumCached = [bool](Get-ChildItem -Path $pwCache -Directory -Filter 'chromium-*' -ErrorAction SilentlyContinue)
}

if ($chromiumCached) {
    Write-Host "  Chromium already cached under $pwCache — skipping build + install." -ForegroundColor Green
} elseif (Get-Command 'dotnet' -ErrorAction SilentlyContinue) {
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
# Reports ONE of three explicit states and, when access is missing, prints the exact self-heal
# command. Local runs depend on real Key Vault secrets (kv-poshared) loading via `az login`
# credentials — a silent "warn and continue" here is the root cause of "every feature breaks
# locally", so we make the failing state loud and actionable.
Write-Step "Checking Azure CLI authentication + Key Vault access (kv-poshared)"

$KeyVaultName = 'kv-poshared'
if (Assert-CommandExists 'az' 'Install Azure CLI from https://aka.ms/installazurecli') {
    $account = az account show --query user.name -o tsv 2>$null

    if ([string]::IsNullOrWhiteSpace($account)) {
        # State 1: NOT LOGGED IN.
        Write-Warning "  [NOT LOGGED IN] Run 'az login' so the app can read secrets from Key Vault ($KeyVaultName)."
        Write-Host   "    Or run fully offline with mocks: set Mocks:UseMockAi=true in appsettings.Development.json." -ForegroundColor DarkGray
    } else {
        Write-Host "  Signed in as $account" -ForegroundColor Green
        Write-Host "  Verifying Key Vault access ($KeyVaultName)..." -ForegroundColor Gray

        $secretCount = az keyvault secret list --vault-name $KeyVaultName --query "length(@)" -o tsv 2>$null
        if (-not [string]::IsNullOrWhiteSpace($secretCount) -and $secretCount -match '^\d+$') {
            # State 2: FULLY READY.
            Write-Host "  [READY] Key Vault reachable — $secretCount secrets visible. Secrets will load locally." -ForegroundColor Green
        } else {
            # State 3: LOGGED IN BUT NO KEY VAULT RBAC. Emit the exact self-heal command.
            $objectId = az ad signed-in-user show --query id -o tsv 2>$null
            $subId    = az account show --query id -o tsv 2>$null
            $scope    = "/subscriptions/$subId/resourceGroups/PoShared/providers/Microsoft.KeyVault/vaults/$KeyVaultName"

            Write-Warning "  [NO KEY VAULT ACCESS] Signed in, but cannot list secrets in $KeyVaultName."
            Write-Host    "    Without access, real secrets won't load and AI/storage/auth features fail locally." -ForegroundColor Yellow
            Write-Host    "    Self-heal (grant your account the Key Vault Secrets User role):" -ForegroundColor Yellow
            if (-not [string]::IsNullOrWhiteSpace($objectId) -and -not [string]::IsNullOrWhiteSpace($subId)) {
                Write-Host  "      az role assignment create ``" -ForegroundColor Cyan
                Write-Host  "        --role 'Key Vault Secrets User' ``" -ForegroundColor Cyan
                Write-Host  "        --assignee-object-id $objectId ``" -ForegroundColor Cyan
                Write-Host  "        --assignee-principal-type User ``" -ForegroundColor Cyan
                Write-Host  "        --scope $scope" -ForegroundColor Cyan
            } else {
                Write-Host  "      (Could not resolve your object id / subscription — ensure 'az login' completed, then retry.)" -ForegroundColor DarkGray
            }
            Write-Host    "    Or run fully offline with mocks: set Mocks:UseMockAi=true." -ForegroundColor DarkGray
        }
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
