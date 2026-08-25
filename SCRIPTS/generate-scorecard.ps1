<#
.SYNOPSIS
    Computes the Unified Code Quality Score (0-100) from NetArchTest, SonarQube/SonarCloud and CodeScene.

.DESCRIPTION
    Reads the three tools' outputs and produces a single weighted score, printed as a table and
    written to CODE-HEALTH-SCORECARD.md.

        Final = (CodeScene Health x 3.5) + (Sonar Score x 0.35) + (NetArch Pass Rate x 0.30)

    which is the same thing as weighting each tool's 0-100 normalised score by 35 / 35 / 30.

    SOURCES
      NetArchTest  artifacts/architecture-results.json, written by
                   PoRedoImage.Tests.Architecture. Produced locally by `dotnet test`, so this
                   component always has real data - the script runs the suite if the file is stale
                   or missing, unless -NoRun is passed.
      Sonar        SonarCloud/SonarQube web API, using $env:SONAR_TOKEN.
      CodeScene    CodeScene web API, using $env:CODESCENE_API_TOKEN + $env:CODESCENE_PROJECT_ID.

    MISSING TOOLS
    Sonar and CodeScene are hosted services; without credentials they return no data. By default a
    missing tool is EXCLUDED and the remaining weights are renormalised to 100, with the partial
    coverage stated plainly in the output - scoring an unmeasured tool as zero would report a
    codebase problem where there is only a missing token. Pass -MissingAsZero for the strict
    reading (useful in CI, where absent data usually means a broken pipeline).

.PARAMETER NoRun
    Do not invoke `dotnet test` for the architecture suite; use the existing JSON as-is.

.PARAMETER MissingAsZero
    Score unavailable tools as 0 instead of renormalising over the available ones.

.PARAMETER OutputPath
    Markdown destination. Defaults to CODE-HEALTH-SCORECARD.md at the repository root.

.EXAMPLE
    pwsh ./SCRIPTS/generate-scorecard.ps1

.EXAMPLE
    $env:SONAR_TOKEN='...'; $env:CODESCENE_API_TOKEN='...'; $env:CODESCENE_PROJECT_ID='42'
    pwsh ./SCRIPTS/generate-scorecard.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoRun,
    [switch]$MissingAsZero,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $repoRoot 'CODE-HEALTH-SCORECARD.md' }

# Weights must total 100. Kept in one place so the table, the maths and the docs cannot drift apart.
$Weights = [ordered]@{ CodeScene = 35; SonarQube = 35; NetArchTest = 30 }

function Write-Section($text) { Write-Host ''; Write-Host $text -ForegroundColor Cyan }

# --------------------------------------------------------------------------------------------
# NetArchTest
# --------------------------------------------------------------------------------------------
function Get-NetArchTestMetrics {
    $jsonPath = Join-Path $repoRoot 'artifacts/architecture-results.json'

    $before = if (Test-Path $jsonPath) { (Get-Item $jsonPath).LastWriteTimeUtc } else { [datetime]::MinValue }

    if (-not $NoRun) {
        Write-Section 'Running architecture suite...'
        # Output is captured rather than streamed: a rule FAILING is a valid scorecard input
        # (it lowers the pass rate), not a reason to abort the report.
        $output = & dotnet test (Join-Path $repoRoot 'tests/PoRedoImage.Tests.Architecture') --nologo 2>&1

        # A COMPILE failure is different from a rule failure and must not pass silently: the run
        # would leave the previous JSON in place and the scorecard would quietly report a stale
        # pass rate as if it were current.
        $after = if (Test-Path $jsonPath) { (Get-Item $jsonPath).LastWriteTimeUtc } else { [datetime]::MinValue }
        if ($after -le $before) {
            Write-Warning 'The architecture suite did not refresh artifacts/architecture-results.json.'
            if ($output -match 'being used by another process|MSB3021|MSB3027') {
                Write-Warning 'Cause: the dev server is running and holds the build outputs. Stop `dotnet run --project src/PoRedoImage.Web` and re-run, or pass -NoRun to score the existing results.'
            }
            if ($after -eq [datetime]::MinValue) {
                return @{ Available = $false; Reason = 'architecture suite failed to run and no previous results exist' }
            }
            Write-Warning "Falling back to the previous results, written $after UTC - treat the NetArchTest row as STALE."
        }
    }

    if (-not (Test-Path $jsonPath)) {
        return @{ Available = $false; Reason = 'artifacts/architecture-results.json not found - run: dotnet test tests/PoRedoImage.Tests.Architecture' }
    }

    $j = Get-Content $jsonPath -Raw | ConvertFrom-Json
    return @{
        Available = $true
        Score     = [double]$j.passRate
        Metrics   = [ordered]@{
            'Total rules'      = $j.totalRules
            'Passed'           = $j.passedRules
            'Failed'           = $j.failedRules
            'Pass rate'        = "$($j.passRate)%"
        }
        Failures  = @($j.rules | Where-Object { -not $_.passed })
    }
}

# --------------------------------------------------------------------------------------------
# SonarQube / SonarCloud
# --------------------------------------------------------------------------------------------
function Get-SonarMetrics {
    if (-not $env:SONAR_TOKEN) {
        return @{ Available = $false; Reason = 'SONAR_TOKEN not set' }
    }

    $projectKey = if ($env:SONAR_PROJECT_KEY) { $env:SONAR_PROJECT_KEY } else { 'punkouter26_PoRedoImage' }
    $hostUrl    = if ($env:SONAR_HOST_URL)    { $env:SONAR_HOST_URL }    else { 'https://sonarcloud.io' }

    $keys = 'sqale_debt_ratio,sqale_rating,coverage,code_smells,duplicated_lines_density,vulnerabilities,cognitive_complexity,complexity,ncloc'
    $uri  = "$hostUrl/api/measures/component?component=$projectKey&metricKeys=$keys"

    try {
        # Sonar takes the token as HTTP Basic username with an empty password.
        $auth    = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("$($env:SONAR_TOKEN):"))
        $headers = @{ Authorization = "Basic $auth" }
        $resp    = Invoke-RestMethod -Uri $uri -Headers $headers -TimeoutSec 45
    }
    catch {
        return @{ Available = $false; Reason = "Sonar API call failed: $($_.Exception.Message)" }
    }

    $m = @{}
    foreach ($measure in $resp.component.measures) {
        $m[$measure.metric] = $measure.value
    }

    function Num($key, $default = 0) {
        if ($m.ContainsKey($key) -and $m[$key] -ne '') { return [double]$m[$key] } else { return [double]$default }
    }

    $debtRatio   = Num 'sqale_debt_ratio'
    $coverage    = Num 'coverage'
    $duplication = Num 'duplicated_lines_density'
    $smells      = Num 'code_smells'
    $vulns       = Num 'vulnerabilities'
    $ncloc       = Num 'ncloc' 1
    $ratingNum   = Num 'sqale_rating' 1
    $rating      = @('A','B','C','D','E')[[Math]::Min([int]$ratingNum - 1, 4)]

    # Composite 0-100. Each sub-score is clamped to [0,100] so one pathological metric cannot
    # drag the total negative and make the final score meaningless.
    function Clamp($v) { [Math]::Max(0, [Math]::Min(100, $v)) }

    $debtScore     = Clamp (100 - ($debtRatio * 5))          # 20% debt ratio -> 0
    $coverageScore = Clamp $coverage                          # already a percentage
    $dupScore      = Clamp (100 - ($duplication * 5))         # 20% duplication -> 0
    $smellScore    = Clamp (100 - (($smells / [Math]::Max($ncloc,1) * 1000) * 10))  # smells per KLOC
    $vulnScore     = Clamp (100 - ($vulns * 20))              # 5 vulnerabilities -> 0

    # Coverage and debt dominate; duplication/smells/vulnerabilities temper the result.
    $sonarScore = ($coverageScore * 0.35) + ($debtScore * 0.25) + ($smellScore * 0.20) +
                  ($dupScore * 0.10) + ($vulnScore * 0.10)

    return @{
        Available = $true
        Score     = [Math]::Round($sonarScore, 2)
        Metrics   = [ordered]@{
            'Maintainability rating' = $rating
            'Technical debt ratio'   = "$debtRatio%"
            'Line coverage'          = "$coverage%"
            'Code smells'            = $smells
            'Duplications'           = "$duplication%"
            'Vulnerabilities'        = $vulns
            'Cognitive complexity'   = Num 'cognitive_complexity'
            'Cyclomatic complexity'  = Num 'complexity'
        }
    }
}

# --------------------------------------------------------------------------------------------
# CodeScene
# --------------------------------------------------------------------------------------------
function Get-CodeSceneMetrics {
    if (-not $env:CODESCENE_API_TOKEN) { return @{ Available = $false; Reason = 'CODESCENE_API_TOKEN not set' } }
    if (-not $env:CODESCENE_PROJECT_ID) { return @{ Available = $false; Reason = 'CODESCENE_PROJECT_ID not set' } }

    $hostUrl = if ($env:CODESCENE_HOST_URL) { $env:CODESCENE_HOST_URL } else { 'https://codescene.io' }
    $uri     = "$hostUrl/api/v2/projects/$($env:CODESCENE_PROJECT_ID)/analyses/latest"

    try {
        $headers = @{ Authorization = "Bearer $($env:CODESCENE_API_TOKEN)"; Accept = 'application/json' }
        $resp    = Invoke-RestMethod -Uri $uri -Headers $headers -TimeoutSec 45
    }
    catch {
        return @{ Available = $false; Reason = "CodeScene API call failed: $($_.Exception.Message)" }
    }

    # Field names vary across CodeScene deployments/versions; probe the documented shapes and fall
    # back rather than hard-failing the whole scorecard on one renamed key.
    $health = $null
    foreach ($path in 'code_health','codeHealth','system_code_health','average_code_health') {
        if ($null -ne $resp.$path) { $health = [double]$resp.$path; break }
    }
    if ($null -eq $health) {
        return @{ Available = $false; Reason = 'CodeScene response contained no recognisable code-health field' }
    }

    $hotspots = 0
    foreach ($path in 'high_risk_hotspots','hotspots_at_risk','riskyHotspotCount') {
        if ($null -ne $resp.$path) { $hotspots = [int]$resp.$path; break }
    }

    $coupling = $null
    foreach ($path in 'inter_module_coupling','coupling','architectural_coupling') {
        if ($null -ne $resp.$path) { $coupling = $resp.$path; break }
    }

    $decay = $null
    foreach ($path in 'architectural_decay_index','decay_index') {
        if ($null -ne $resp.$path) { $decay = $resp.$path; break }
    }

    return @{
        Available = $true
        Score     = [Math]::Round($health * 10, 2)   # 1.0-10.0 -> 0-100
        Metrics   = [ordered]@{
            'Code health (1-10)'        = $health
            'High-risk hotspots'        = $hotspots
            'Inter-module coupling'     = $(if ($null -ne $coupling) { $coupling } else { 'n/a' })
            'Architectural decay index' = $(if ($null -ne $decay) { $decay } else { 'n/a' })
        }
    }
}

# --------------------------------------------------------------------------------------------
# Assemble
# --------------------------------------------------------------------------------------------
Write-Section 'PoRedoImage - Unified Code Quality Scorecard'

$tools = [ordered]@{
    CodeScene   = Get-CodeSceneMetrics
    SonarQube   = Get-SonarMetrics
    NetArchTest = Get-NetArchTestMetrics
}

$available   = @($tools.Keys | Where-Object { $tools[$_].Available })
$unavailable = @($tools.Keys | Where-Object { -not $tools[$_].Available })

if ($available.Count -eq 0) {
    Write-Error 'No tool produced data - cannot compute a score. Run the architecture suite at minimum.'
    exit 1
}

# Renormalise across available tools unless -MissingAsZero.
$effectiveWeight = @{}
if ($MissingAsZero) {
    foreach ($t in $tools.Keys) { $effectiveWeight[$t] = $Weights[$t] }
} else {
    $availableWeight = ($available | ForEach-Object { $Weights[$_] } | Measure-Object -Sum).Sum
    foreach ($t in $tools.Keys) {
        $effectiveWeight[$t] = if ($tools[$t].Available) {
            [Math]::Round($Weights[$t] * 100 / $availableWeight, 2)
        } else { 0 }
    }
}

$final = 0.0
$rows  = @()
foreach ($name in $tools.Keys) {
    $t = $tools[$name]
    $score    = if ($t.Available) { [double]$t.Score } else { 0.0 }
    $weight   = [double]$effectiveWeight[$name]
    $weighted = [Math]::Round($score * $weight / 100, 2)
    $final   += $weighted

    $metricText = if ($t.Available) {
        (($t.Metrics.GetEnumerator() | ForEach-Object { "$($_.Key): $($_.Value)" }) -join '; ')
    } else { "UNAVAILABLE - $($t.Reason)" }

    $rows += [pscustomobject]@{
        Tool     = $name
        Metrics  = $metricText
        Score    = if ($t.Available) { [Math]::Round($score,2) } else { 'n/a' }
        Weight   = "$weight%"
        Weighted = "$weighted / $weight"
    }
}

$final = [Math]::Round($final, 2)
$grade = switch ($final) {
    { $_ -ge 90 } { 'A'; break }
    { $_ -ge 80 } { 'B'; break }
    { $_ -ge 70 } { 'C'; break }
    { $_ -ge 60 } { 'D'; break }
    default       { 'F' }
}

# ---- terminal output ----
$rows | Format-Table -AutoSize -Wrap | Out-String | Write-Host
$colour = if ($grade -in 'A','B') { 'Green' } elseif ($grade -eq 'C') { 'Yellow' } else { 'Red' }
Write-Host ("FINAL SCORE: {0} / 100   Grade {1}" -f $final, $grade) -ForegroundColor $colour

if ($unavailable.Count -gt 0) {
    $mode = if ($MissingAsZero) { 'scored as zero' } else { 'excluded; remaining weights renormalised to 100' }
    Write-Warning "PARTIAL RESULT - no data from: $($unavailable -join ', ') ($mode)."
}

$archFailures = @()
if ($tools.NetArchTest.Available) { $archFailures = @($tools.NetArchTest.Failures) }
if ($archFailures.Count -gt 0) {
    Write-Section 'Architecture violations'
    foreach ($f in $archFailures) {
        Write-Host "  [$($f.category)] $($f.description)" -ForegroundColor Red
        foreach ($v in $f.violations) { Write-Host "      $v" -ForegroundColor DarkGray }
    }
}

# ---- markdown output ----
$stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
$md = New-Object Text.StringBuilder
[void]$md.AppendLine('# Code Health Scorecard')
[void]$md.AppendLine()
[void]$md.AppendLine("Generated $stamp by ``SCRIPTS/generate-scorecard.ps1``.")
[void]$md.AppendLine()
if ($unavailable.Count -gt 0) {
    $mode = if ($MissingAsZero) { 'scored as **zero**' } else { '**excluded**, and the remaining weights renormalised to 100' }
    [void]$md.AppendLine("> **Partial result.** No data from: $($unavailable -join ', ') - $mode.")
    [void]$md.AppendLine('>')
    [void]$md.AppendLine('> A tool reports no data when its credentials are absent, so this reflects tooling setup, not necessarily code quality.')
    [void]$md.AppendLine()
}
[void]$md.AppendLine('| Tool | Key Metrics Extracted | Tool Score (0-100) | Weight | Weighted Score |')
[void]$md.AppendLine('|---|---|---|---|---|')
foreach ($r in $rows) {
    [void]$md.AppendLine("| **$($r.Tool)** | $($r.Metrics) | $($r.Score) | $($r.Weight) | $($r.Weighted) |")
}
[void]$md.AppendLine("| **FINAL SCORE** | **Overall Codebase Quality Rating** | **$final** | **100%** | **$final / 100 (Grade $grade)** |")
[void]$md.AppendLine()

if ($archFailures.Count -gt 0) {
    [void]$md.AppendLine('## Architecture violations')
    [void]$md.AppendLine()
    foreach ($f in $archFailures) {
        [void]$md.AppendLine("- **[$($f.category)]** $($f.description)")
        foreach ($v in $f.violations) { [void]$md.AppendLine("  - ``$v``") }
    }
    [void]$md.AppendLine()
}

[void]$md.AppendLine('## Weighting')
[void]$md.AppendLine()
[void]$md.AppendLine('`Final = (CodeScene Health x 3.5) + (Sonar Score x 0.35) + (NetArchTest Pass Rate x 0.30)`')
[void]$md.AppendLine()
[void]$md.AppendLine('Equivalently: each tool normalised to 0-100, then weighted 35 / 35 / 30.')
[void]$md.AppendLine()
[void]$md.AppendLine('| Grade | Score |')
[void]$md.AppendLine('|---|---|')
[void]$md.AppendLine('| A | 90-100 |')
[void]$md.AppendLine('| B | 80-89 |')
[void]$md.AppendLine('| C | 70-79 |')
[void]$md.AppendLine('| D | 60-69 |')
[void]$md.AppendLine('| F | below 60 |')

Set-Content -Path $OutputPath -Value $md.ToString() -Encoding UTF8
Write-Host ''
Write-Host "Markdown written to $OutputPath" -ForegroundColor Green
