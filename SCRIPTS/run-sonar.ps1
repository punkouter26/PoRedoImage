<#
.SYNOPSIS
    Runs a SonarQube/SonarCloud analysis of PoRedoImage with test coverage.

.DESCRIPTION
    Wraps the three-phase MSBuild scanner flow (begin -> build -> end) and collects line coverage
    via coverlet in OpenCover format, which is what Sonar's C# plugin understands.

    Why there is no sonar-project.properties in this repo: that file is read by the *CLI* scanner
    (sonar-scanner). The .NET scanner used here (dotnet-sonarscanner) ignores it entirely and takes
    its configuration from /k: and /d: arguments. Committing one would look like configuration while
    doing nothing, so the settings live here instead.

    Analysis is skipped (not failed) when SONAR_TOKEN is absent, so the script is safe to call from
    a scorecard run on a machine with no credentials.

.PARAMETER ProjectKey
    Sonar project key. Defaults to $env:SONAR_PROJECT_KEY, then 'punkouter26_PoRedoImage'.

.PARAMETER Organization
    SonarCloud organization. Defaults to $env:SONAR_ORGANIZATION, then 'punkouter26'.

.PARAMETER HostUrl
    Sonar host. Defaults to $env:SONAR_HOST_URL, then https://sonarcloud.io.

.PARAMETER SkipTests
    Run analysis without executing tests. Coverage will be absent, so the coverage component of the
    scorecard will read 0 - useful only for a quick smells/debt pass.

.EXAMPLE
    $env:SONAR_TOKEN = '<token>'; pwsh ./SCRIPTS/run-sonar.ps1

.NOTES
    The MAUI head (src/PoRedoImage.Mobile) is excluded: it targets net10.0-android and cannot build
    on an agent without the maui-android workload - the same reason CI builds the Web project graph
    rather than the solution. See .github/workflows/deploy.yml.
#>
[CmdletBinding()]
param(
    [string]$ProjectKey  = $(if ($env:SONAR_PROJECT_KEY)  { $env:SONAR_PROJECT_KEY }  else { 'punkouter26_PoRedoImage' }),
    [string]$Organization = $(if ($env:SONAR_ORGANIZATION) { $env:SONAR_ORGANIZATION } else { 'punkouter26' }),
    [string]$HostUrl     = $(if ($env:SONAR_HOST_URL)     { $env:SONAR_HOST_URL }     else { 'https://sonarcloud.io' }),
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    if (-not $env:SONAR_TOKEN) {
        Write-Warning 'SONAR_TOKEN is not set - skipping Sonar analysis.'
        Write-Host    'Set it first:  $env:SONAR_TOKEN = "<your token>"'
        exit 0
    }

    # The scanner is a local dotnet tool; install on first use so the script is self-bootstrapping.
    if (-not (Get-Command dotnet-sonarscanner -ErrorAction SilentlyContinue)) {
        Write-Host 'Installing dotnet-sonarscanner (global tool)...' -ForegroundColor Cyan
        dotnet tool install --global dotnet-sonarscanner
        $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
    }

    $coverageDir = Join-Path $repoRoot 'artifacts/coverage'
    New-Item -ItemType Directory -Force -Path $coverageDir | Out-Null

    # Sonar reads OpenCover XML for C#. Absolute path: the scanner resolves this relative to its own
    # working directory, and a relative path silently yields "no coverage" rather than an error.
    $coverageReport = Join-Path $coverageDir 'coverage.opencover.xml'

    Write-Host "Sonar begin  (project=$ProjectKey org=$Organization host=$HostUrl)" -ForegroundColor Cyan
    dotnet sonarscanner begin `
        /k:"$ProjectKey" `
        /o:"$Organization" `
        /d:sonar.host.url="$HostUrl" `
        /d:sonar.token="$env:SONAR_TOKEN" `
        /d:sonar.cs.opencover.reportsPaths="$coverageReport" `
        /d:sonar.scanner.scanAll=false `
        /d:sonar.exclusions="**/bin/**,**/obj/**,**/wwwroot/lib/**,src/PoRedoImage.Mobile/**" `
        /d:sonar.coverage.exclusions="tests/**,**/Program.cs,src/PoRedoImage.Mobile/**"
    if ($LASTEXITCODE -ne 0) { throw "sonarscanner begin failed ($LASTEXITCODE)" }

    # Build the same graph CI builds - deliberately not the solution (excludes the MAUI head).
    Write-Host 'Building...' -ForegroundColor Cyan
    dotnet build src/PoRedoImage.Web/PoRedoImage.Web.csproj -c Debug
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }

    if (-not $SkipTests) {
        Write-Host 'Running tests with coverage...' -ForegroundColor Cyan
        # Unit + Architecture only: Integration needs Docker and E2E needs a live server, so
        # including them would make coverage depend on machine state rather than on the code.
        foreach ($proj in @('tests/PoRedoImage.Tests.Unit', 'tests/PoRedoImage.Tests.Architecture')) {
            dotnet test $proj `
                --no-build `
                /p:CollectCoverage=true `
                /p:CoverletOutputFormat=opencover `
                /p:CoverletOutput="$coverageReport" `
                /p:MergeWith="$coverageReport"
            if ($LASTEXITCODE -ne 0) { Write-Warning "$proj reported failures - continuing so Sonar still receives results." }
        }
    }

    Write-Host 'Sonar end (uploading)...' -ForegroundColor Cyan
    dotnet sonarscanner end /d:sonar.token="$env:SONAR_TOKEN"
    if ($LASTEXITCODE -ne 0) { throw "sonarscanner end failed ($LASTEXITCODE)" }

    Write-Host "Analysis uploaded. Dashboard: $HostUrl/dashboard?id=$ProjectKey" -ForegroundColor Green
}
finally {
    Pop-Location
}
