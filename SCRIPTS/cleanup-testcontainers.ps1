<#
.SYNOPSIS
    One-shot cleanup of orphaned Testcontainers containers left behind by crashed or
    interrupted `dotnet test` runs.

.DESCRIPTION
    Pattern (from /memories/repo/testcontainers-cleanup.md): Testcontainers 4.x names
    ephemeral containers `{prefix}-test-{image}-{16-32 hex chars}`. This script
    enumerates all `docker ps -a` containers matching that shape, EXCLUDING the
    dev Azurite container managed by docker-compose.yml (`poredoimage-azurite-dev`),
    and force-removes the rest.

    Idempotent + safe to call before/after `dotnet test`. With `-DryRun` it lists
    what would be removed without touching Docker.

.PARAMETER DryRun
    Print the would-be-removed containers and exit. No `docker rm` is issued.

.PARAMETER Prefix
    Container-name prefix to match. Defaults to `poredoimage` (the solution
    short-name). Override for the test-utility project (`pomemevideo`,
    `poface`, etc.) when running cleanup across worktrees.

.EXAMPLE
    .\SCRIPTS\cleanup-testcontainers.ps1
    # Remove every orphaned poredoimage test container (except the dev Azurite).

.EXAMPLE
    .\SCRIPTS\cleanup-testcontainers.ps1 -DryRun
    # Show what would be removed; take no action.

.EXAMPLE
    .\SCRIPTS\cleanup-testcontainers.ps1 -Prefix poface
    # Clean up a sibling worktree's test containers.
#>

#Requires -Version 7

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

param(
    [switch] $DryRun,
    [string] $Prefix = 'poredoimage'
)

# ── Sanity: Docker available ──────────────────────────────────────────────
if (-not (Get-Command 'docker' -ErrorAction SilentlyContinue)) {
    Write-Warning "Docker CLI not found in PATH — nothing to clean up."
    exit 0
}

# ── Enumerate candidates ──────────────────────────────────────────────────
# The Testcontainers 4.x default name pattern is `{prefix}-test-{image}-{16-32 hex}`.
# Older projects may have used a different shape; we additionally strip the dev Azurite
# explicitly (named `poredoimage-azurite-dev` in docker-compose.yml) so the regex
# never accidentally matches it.
$allContainers = docker ps -a --format '{{.Names}}' 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Warning "docker ps failed (exit $LASTEXITCODE) — is the Docker daemon running?"
    exit 0
}

# Regex breakdown (the same one in /memories/repo/testcontainers-cleanup.md):
#   ^               start of name
#   (?:.+)          prefix token (greedy)
#   -test-          Testcontainers suffix marker
#   [^-]+           image name (no hyphens — images with hyphens like `azure-storage/azurite` use a
#                   single token here because the actual TC name uses `_` not `-` for the tag delimiter)
#   -[0-9a-f]{16,32}$  random hex suffix (16-32 chars)
$tcRegex = "^(?:$Prefix)-test-[^-]+-[0-9a-f]{16,32}$"
$devAzurite = "$Prefix-azurite-dev"   # docker-compose.yml service name — NEVER remove

$candidates = $allContainers |
    Where-Object { $_ -match $tcRegex -and $_ -ne $devAzurite }

if (-not $candidates -or $candidates.Count -eq 0) {
    Write-Host "No orphaned test containers matching prefix '$Prefix'." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($candidates.Count) orphaned test container(s):" -ForegroundColor Yellow
$candidates | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }

if ($DryRun) {
    Write-Host "`nDryRun: no containers removed. Re-run without -DryRun to clean up." -ForegroundColor Cyan
    exit 0
}

# ── Remove ────────────────────────────────────────────────────────────────
$removed = 0
foreach ($c in $candidates) {
    Write-Host "Removing $c..." -ForegroundColor Gray
    docker rm -f $c 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
    if ($LASTEXITCODE -eq 0) { $removed++ }
}

Write-Host "`nRemoved $removed of $($candidates.Count) container(s)." -ForegroundColor Green
exit 0
