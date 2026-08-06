<#
.SYNOPSIS
    Run the E2E tier (E2EAPI + E2EUI) against a freshly-launched local server.

.DESCRIPTION
    Starts the Web app on http://localhost:4000 (Development → no HTTPS redirect, so the default
    E2E_BASE_URL works), waits for /alive, runs the E2E test project, then stops the server.
    Playwright Chromium must be installed (SCRIPTS/setup.ps1 does this).

.USAGE
    pwsh ./SCRIPTS/run-e2e.ps1
#>

#Requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path $PSScriptRoot -Parent
$BaseUrl = 'http://localhost:4000'

Write-Host "==> Building solution" -ForegroundColor Cyan
dotnet build "$Root/PoRedoImage.slnx" -c Release --nologo | Out-Null

Write-Host "==> Launching Web app (Development) on $BaseUrl" -ForegroundColor Cyan
# Budget guardrail: force the app into mock-AI mode so the E2E suite can NEVER spend a live token,
# and flag the test run to HARD-FAIL if the target isn't actually mocked (asserted by the
# Ai_services_are_mocked_when_mock_mode_is_required E2E test via /api/diag/mock-status).
# Start-Process inherits these from the current process, so the launched app picks them up.
$env:Mocks__UseMockAi = 'true'
$env:E2E_REQUIRE_MOCK = 'true'
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

    # Two projects since the E2EAPI/E2EUI split (spec §2.2): ApiSmoke is pure HTTP, UI drives
    # Playwright. This script pointed at the pre-split PoRedoImage.Tests.E2E.csproj and had been
    # failing with MSB1009 "Project file does not exist" ever since — so the documented E2E entry
    # point ran nothing. Both projects run even if the first fails, because a red API smoke and a
    # red UI suite are independently useful signals; the script exits non-zero if either failed.
    $projects = @(
        "$Root/tests/PoRedoImage.Tests.E2E.ApiSmoke/PoRedoImage.Tests.E2E.ApiSmoke.csproj",
        "$Root/tests/PoRedoImage.Tests.E2E.UI/PoRedoImage.Tests.E2E.UI.csproj"
    )

    $failed = 0
    foreach ($proj in $projects) {
        Write-Host "--> $(Split-Path $proj -LeafBase)" -ForegroundColor Cyan
        dotnet test $proj -c Release --no-build --nologo
        if ($LASTEXITCODE -ne 0) { $failed = 1 }
    }

    exit $failed
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
