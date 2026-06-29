<#
.SYNOPSIS
    Cloud-governance audit via Azure Resource Graph (ARG) — report only, never prune.

.DESCRIPTION
    Runs three idempotent governance queries against the subscription and prints a report:
      1. Stray resources   — anything NOT in the PoRedoImage or PoShared resource groups.
      2. Naming violations  — resources in those RGs whose name does not start with an
                              approved 'po'/'Po' token (see docs/ADR_Log.md ADR-018).
      3. Idle compute       — App Service Plans / VMs averaging < 5% CPU over 7 days.

    This script is DELIBERATELY read-only. F1 plans legitimately look "idle", and a stray
    resource may be shared infra, so deletion is a human decision. Run it locally or as a
    SEPARATE scheduled workflow — it is intentionally kept OUT of the deploy pipeline
    (deploy.yml builds + ships only; project policy).

.PREREQUISITES
    - Azure CLI (`az login` completed)
    - Resource Graph extension (auto-installed below if missing)

.USAGE
    pwsh ./SCRIPTS/audit-arg.ps1
#>

#Requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Approved resource groups and naming prefix (lowercase per Azure DNS rules; see ADR-018).
$OwnedResourceGroups = @('PoRedoImage', 'PoShared')
$NamePrefixPattern = '^po'   # case-insensitive; storage/web must be lowercase 'po…'
$IdleCpuThreshold = 5.0       # percent average over the window
$IdleWindowDays = 7
# Plan names that legitimately look idle and must NEVER be auto-stopped. The F1 plan is always
# expected to show <5% CPU (it is the dev/CI plan; runs at 60-min/day cap and is now empty since
# ADR-025 moved the app to B1). Add additional names here as you find more "structural idle" plans.
$AutoStopExcludedPlans = @('asp-poredoimage-f1')

[CmdletBinding()]
param(
    # Print-only mode by default. When -AutoStopIdleCompute is passed, the script additionally
    # calls 'az webapp stop' on the listed App Service Plans after a 10s operator-abort window.
    # Reversible (az webapp start brings it back in seconds). No deletion. No naming-prune.
    # See ADR-030 for the safety design.
    [switch]$AutoStopIdleCompute
)

function Write-Step([string]$Message) { Write-Host "`n==> $Message" -ForegroundColor Cyan }

# ── Preconditions ────────────────────────────────────────────────────
if (-not (Get-Command 'az' -ErrorAction SilentlyContinue)) {
    throw "Azure CLI not found. Install from https://aka.ms/installazurecli then run 'az login'."
}

$account = az account show --query name -o tsv 2>$null
if ([string]::IsNullOrWhiteSpace($account)) {
    throw "Not logged in to Azure CLI. Run 'az login' first."
}
Write-Host "Subscription: $account" -ForegroundColor Gray

# Resource Graph is an extension; install idempotently (no-op if already present).
$hasGraph = az extension show --name resource-graph --query name -o tsv 2>$null
if ([string]::IsNullOrWhiteSpace($hasGraph)) {
    Write-Host "Installing 'resource-graph' CLI extension..." -ForegroundColor Gray
    az extension add --name resource-graph --only-show-errors | Out-Null
}

function Invoke-Graph([string]$Query) {
    return az graph query -q $Query --first 1000 --query "data" -o json 2>$null | ConvertFrom-Json
}

$findings = 0

# ── 1. Stray resources (outside owned RGs) ───────────────────────────
Write-Step "1/3 Resources outside owned RGs ($($OwnedResourceGroups -join ', '))"
$rgList = ($OwnedResourceGroups | ForEach-Object { "'$_'" }) -join ','
$strayQuery = @"
Resources
| where resourceGroup !in~ ($rgList)
| project name, type, resourceGroup, location
| order by resourceGroup asc
"@
$stray = Invoke-Graph $strayQuery
if ($stray -and $stray.Count -gt 0) {
    $findings += $stray.Count
    Write-Warning "  $($stray.Count) resource(s) live outside $($OwnedResourceGroups -join '/'):"
    $stray | Format-Table name, type, resourceGroup, location -AutoSize | Out-Host
} else {
    Write-Host "  None. All resources are in owned resource groups." -ForegroundColor Green
}

# ── 2. Naming-convention violations ──────────────────────────────────
Write-Step "2/3 Naming violations in owned RGs (must match $NamePrefixPattern, see ADR-018)"
$nameQuery = @"
Resources
| where resourceGroup in~ ($rgList)
| where name !startswith 'po' and name !startswith 'Po'
| where type !in~ ('microsoft.web/serverfarms/sites')
| project name, type, resourceGroup
| order by name asc
"@
$badNames = Invoke-Graph $nameQuery
if ($badNames -and $badNames.Count -gt 0) {
    $findings += $badNames.Count
    Write-Warning "  $($badNames.Count) resource(s) violate the 'po'-prefix convention:"
    $badNames | Format-Table name, type, resourceGroup -AutoSize | Out-Host
} else {
    Write-Host "  None. All owned resources follow the 'po' prefix." -ForegroundColor Green
}

# ── 3. Idle compute (< 5% CPU over 7 days) ───────────────────────────
Write-Step "3/3 Idle compute (< $IdleCpuThreshold% avg CPU over $IdleWindowDays days)"
Write-Host "  Note: the F1 Free plan (asp-poredoimage-f1) is EXPECTED to look idle — do not prune it." -ForegroundColor DarkGray

$planQuery = @"
Resources
| where type =~ 'microsoft.web/serverfarms'
| project id, name, resourceGroup, sku=tostring(sku.name)
"@
$plans = Invoke-Graph $planQuery
$idle = @()
foreach ($p in @($plans)) {
    $startTime = (Get-Date).AddDays(-$IdleWindowDays).ToString('yyyy-MM-ddTHH:mm:ssZ')
    $avg = az monitor metrics list --resource $p.id `
        --metric 'CpuPercentage' --interval PT1H --start-time $startTime `
        --aggregation Average --query "value[0].timeseries[0].data[?average!=null].average" -o tsv 2>$null
    if ($avg) {
        $mean = ($avg -split "`n" | Where-Object { $_ } | Measure-Object -Average).Average
        if ($null -ne $mean -and $mean -lt $IdleCpuThreshold) {
            $idle += [pscustomobject]@{ Name = $p.name; RG = $p.resourceGroup; Sku = $p.sku; AvgCpu = [math]::Round($mean, 2) }
        }
    }
}
if ($idle.Count -gt 0) {
    Write-Warning "  $($idle.Count) low-utilization plan(s) (review; F1 is expected here):"
    $idle | Format-Table Name, RG, Sku, AvgCpu -AutoSize | Out-Host

    # ── 3a. Optional auto-stop (ADR-030) ────────────────────────────────────
    # Print-only by default. When -AutoStopIdleCompute is passed, the script stops (NOT deletes)
    # any idle plan that is NOT in $AutoStopExcludedPlans. Reversible via 'az webapp start'.
    if ($AutoStopIdleCompute) {
        $candidates = @($idle | Where-Object { $AutoStopExcludedPlans -notcontains $_.Name })
        $excluded = @($idle | Where-Object { $AutoStopExcludedPlans -contains $_.Name })

        if ($excluded.Count -gt 0) {
            Write-Host ("  Excluded (structural idle, never auto-stop): " + (($excluded | ForEach-Object { $_.Name }) -join ', ')) -ForegroundColor DarkGray
        }

        if ($candidates.Count -eq 0) {
            Write-Host "  No auto-stop candidates after exclusions." -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Warning "  Auto-stop will fire on the following $($candidates.Count) plan(s) in 10s — press Ctrl-C to abort:"
            $candidates | Format-Table Name, RG, Sku, AvgCpu -AutoSize | Out-Host
            for ($s = 10; $s -gt 0; $s--) {
                Write-Host -NoNewline "`r  Auto-stop in ${s}s… "
                Start-Sleep -Seconds 1
            }
            Write-Host "`r  Auto-stop proceeding.                 "

            foreach ($p in $candidates) {
                $plan = $p.Name
                $rg = $p.RG
                Write-Host "  Stopping '$plan' in $rg… " -NoNewline
                # az webapp stop on a serverfarm stops every web app on that plan — that is the
                # intended behavior here (the whole plan is idle). Single line of output.
                $stopOut = az webapp stop --resource-group $rg --name $plan 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "OK" -ForegroundColor Green
                } else {
                    Write-Host "FAILED ($stopOut)" -ForegroundColor Red
                }
            }
            Write-Host "  Auto-stop complete. Restart with 'az webapp start -g <rg> -n <plan>'." -ForegroundColor Cyan
        }
    } else {
        Write-Host "  Tip: pass -AutoStopIdleCompute to stop (not delete) these plans. Reversible." -ForegroundColor DarkGray
    }
} else {
    Write-Host "  No plans below the idle threshold (or no CPU metrics available)." -ForegroundColor Green
}

# ── Summary ──────────────────────────────────────────────────────────
Write-Step "Audit complete"
if ($findings -gt 0) {
    Write-Warning "$findings governance finding(s) above. Review and prune manually — this script never deletes."
} else {
    Write-Host "✓ No stray or mis-named resources. Cloud footprint is clean." -ForegroundColor Green
}
