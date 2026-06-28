<#
.SYNOPSIS
    Run the E2E tier (E2EAPI + E2EUI) against a freshly-launched local server.

.DESCRIPTION
    Starts the Web app on http://localhost:5000 (Development → no HTTPS redirect, so the default
    E2E_BASE_URL works), waits for /alive, runs the E2E test project, then stops the server.
    Playwright Chromium must be installed (SCRIPTS/setup.ps1 does this).

.USAGE
    pwsh ./SCRIPTS/run-e2e.ps1
#>

#Requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path $PSScriptRoot -Parent
$BaseUrl = 'http://localhost:5000'

Write-Host "==> Building solution" -ForegroundColor Cyan
dotnet build "$Root/PoRedoImage.slnx" -c Release --nologo | Out-Null

Write-Host "==> Launching Web app (Development) on $BaseUrl" -ForegroundColor Cyan
$server = Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', "$Root/src/PoRedoImage.Web/PoRedoImage.Web.csproj",
                    '--launch-profile', 'https', '--no-build', '-c', 'Release') `
    -PassThru -WindowStyle Hidden

try {
    Write-Host "==> Waiting for /alive ..." -ForegroundColor Cyan
    $up = $false
    foreach ($i in 1..40) {
        try {
            $resp = Invoke-WebRequest -Uri "$BaseUrl/alive" -TimeoutSec 3 -SkipHttpErrorCheck
            if ($resp.StatusCode -eq 200) { $up = $true; break }
        } catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $up) { throw "Server did not become healthy at $BaseUrl/alive" }
    Write-Host "  Server is up." -ForegroundColor Green

    Write-Host "==> Running E2E tier" -ForegroundColor Cyan
    $env:E2E_BASE_URL = $BaseUrl
    dotnet test "$Root/tests/PoRedoImage.Tests.E2E/PoRedoImage.Tests.E2E.csproj" -c Release --no-build --nologo
    exit $LASTEXITCODE
}
finally {
    Write-Host "==> Stopping Web app" -ForegroundColor Cyan
    if ($server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    }
    # Kill any child dotnet host the launcher spawned.
    Get-CimInstance Win32_Process -Filter "name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*PoRedoImage.Web*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}
