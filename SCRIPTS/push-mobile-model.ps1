<#
.SYNOPSIS
    Downloads the on-device Qwen meme-caption model and pushes it to an Android device.

.DESCRIPTION
    The MAUI head runs meme-caption generation locally through ONNX Runtime GenAI
    (see src/PoRedoImage.Mobile/Services/QwenCaptionService.cs). The weights are far too
    large to ship inside the APK, so they live in the app's external files directory and
    are side-loaded with adb.

    Files are cached under $env:LOCALAPPDATA\PoRedoImage\models so a re-run only transfers
    what is missing. Each file is verified against the size recorded in $ModelFiles below;
    a truncated download is deleted and re-fetched rather than pushed.

.PARAMETER Serial
    adb device serial. Required when more than one device or emulator is attached.

.PARAMETER CacheDir
    Local download cache. Defaults to $env:LOCALAPPDATA\PoRedoImage\models\<model>.

.PARAMETER SkipDownload
    Push whatever is already cached; do not contact Hugging Face.

.PARAMETER SkipPush
    Download and verify only. Useful when no device is attached yet.

.EXAMPLE
    pwsh ./SCRIPTS/push-mobile-model.ps1
    pwsh ./SCRIPTS/push-mobile-model.ps1 -Serial emulator-5554
    pwsh ./SCRIPTS/push-mobile-model.ps1 -SkipPush
#>
[CmdletBinding()]
param(
    [string]$Serial,
    [string]$CacheDir,
    [switch]$SkipDownload,
    [switch]$SkipPush
)

$ErrorActionPreference = 'Stop'
# Invoke-WebRequest spends most of a large download repainting the progress bar.
$ProgressPreference = 'SilentlyContinue'

# Must match OnDeviceModelCatalog.Qwen25MemeCaption in the MAUI project — the app looks for
# exactly this directory name and refuses to load if genai_config.json is not inside it.
$ModelId    = 'qwen2.5-0.5b-instruct'
$PackageId  = 'com.poredoimage.mobile'
$HfRepo     = 'amd/Qwen2.5-0.5B-Instruct-quantized_int4-float16-cpu-onnx'
$HfRevision = 'main'

# Expected sizes are the authoritative integrity check. Hugging Face serves LFS files through
# a redirect that returns HTML on failure, so a "successful" download can still be a 400-byte
# error page; comparing length catches that without needing a hash of an 800 MB blob.
$ModelFiles = @(
    @{ Name = 'genai_config.json';       Size = 1567L },
    @{ Name = 'model.onnx';              Size = 191883L },
    @{ Name = 'model.onnx.data';         Size = 817777152L },
    @{ Name = 'tokenizer.json';          Size = 11421896L },
    @{ Name = 'tokenizer_config.json';   Size = 7544L },
    @{ Name = 'special_tokens_map.json'; Size = 644L },
    @{ Name = 'vocab.json';              Size = 2776833L },
    @{ Name = 'merges.txt';              Size = 1671853L },
    @{ Name = 'added_tokens.json';       Size = 629L }
)

if (-not $CacheDir) {
    $CacheDir = Join-Path $env:LOCALAPPDATA "PoRedoImage\models\$ModelId"
}

function Resolve-Adb {
    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'),
        (Join-Path $env:ProgramFiles 'Android\android-sdk\platform-tools\adb.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Android\android-sdk\platform-tools\adb.exe')
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    throw "adb not found. Install Android platform-tools or add adb to PATH."
}

function Format-Size([long]$bytes) {
    if ($bytes -ge 1GB) { return '{0:N2} GB' -f ($bytes / 1GB) }
    if ($bytes -ge 1MB) { return '{0:N1} MB' -f ($bytes / 1MB) }
    if ($bytes -ge 1KB) { return '{0:N1} KB' -f ($bytes / 1KB) }
    return "$bytes B"
}

# ── Download ──────────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $CacheDir | Out-Null
$totalBytes = ($ModelFiles | Measure-Object -Property Size -Sum).Sum

Write-Host ""
Write-Host "Model : $ModelId  ($(Format-Size $totalBytes))" -ForegroundColor Cyan
Write-Host "Source: https://huggingface.co/$HfRepo" -ForegroundColor Cyan
Write-Host "Cache : $CacheDir" -ForegroundColor Cyan
Write-Host ""

if ($SkipDownload) {
    Write-Host "Skipping download (-SkipDownload)." -ForegroundColor Yellow
}
else {
    foreach ($file in $ModelFiles) {
        $dest = Join-Path $CacheDir $file.Name

        if (Test-Path $dest) {
            $actual = (Get-Item $dest).Length
            if ($actual -eq $file.Size) {
                Write-Host ("  cached  {0,-24} {1}" -f $file.Name, (Format-Size $actual)) -ForegroundColor DarkGray
                continue
            }
            Write-Host ("  resize  {0,-24} expected {1}, found {2} - refetching" -f `
                $file.Name, (Format-Size $file.Size), (Format-Size $actual)) -ForegroundColor Yellow
            Remove-Item $dest -Force
        }

        $url = "https://huggingface.co/$HfRepo/resolve/$HfRevision/$($file.Name)"
        Write-Host ("  get     {0,-24} {1} ..." -f $file.Name, (Format-Size $file.Size)) -NoNewline

        try {
            Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing -TimeoutSec 1800
        }
        catch {
            if (Test-Path $dest) { Remove-Item $dest -Force }
            throw "Download failed for $($file.Name): $($_.Exception.Message)"
        }

        $actual = (Get-Item $dest).Length
        if ($actual -ne $file.Size) {
            Remove-Item $dest -Force
            throw "Size mismatch for $($file.Name): expected $($file.Size), got $actual."
        }
        Write-Host " ok" -ForegroundColor Green
    }
    Write-Host ""
    Write-Host "Download complete and verified." -ForegroundColor Green
}

# ── Push ──────────────────────────────────────────────────────────────────────
if ($SkipPush) {
    Write-Host ""
    Write-Host "Skipping push (-SkipPush). Re-run without it once a device is attached." -ForegroundColor Yellow
    exit 0
}

$adb = Resolve-Adb
$adbArgs = @()
if ($Serial) { $adbArgs += @('-s', $Serial) }

$devices = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\sdevice$' }
if (-not $devices) {
    Write-Host ""
    Write-Host "No device or emulator attached - nothing to push." -ForegroundColor Yellow
    Write-Host "Attach the phone (USB debugging on) or start an emulator, then re-run:" -ForegroundColor Yellow
    Write-Host "  pwsh ./SCRIPTS/push-mobile-model.ps1 -SkipDownload" -ForegroundColor Yellow
    exit 1
}
if ($devices.Count -gt 1 -and -not $Serial) {
    throw "More than one device attached; pass -Serial. Found:`n$($devices -join "`n")"
}

# The app resolves this from Context.GetExternalFilesDir(null). It is app-scoped storage, so
# adb (the shell user) can write it without root, but it only exists once the package is
# installed - a fresh install that has never launched is fine, Android creates the parent.
$remoteRoot = "/sdcard/Android/data/$PackageId/files/models"
$remoteDir = "$remoteRoot/$ModelId"

Write-Host ""
Write-Host "Target: $remoteDir" -ForegroundColor Cyan

& $adb @adbArgs shell "mkdir -p '$remoteDir'" 2>&1 | ForEach-Object { Write-Host "  $_" }
$probe = & $adb @adbArgs shell "[ -d '$remoteDir' ] && echo present || echo missing"
if ($probe -notmatch 'present') {
    throw "Could not create $remoteDir. Is $PackageId installed on the device? Install the app first, then re-run."
}

foreach ($file in $ModelFiles) {
    $src = Join-Path $CacheDir $file.Name
    if (-not (Test-Path $src)) {
        throw "$($file.Name) is not in the cache. Re-run without -SkipDownload."
    }

    $remoteFile = "$remoteDir/$($file.Name)"
    # Some shells emit a trailing blank line, so take the first line rather than calling .Trim()
    # on what would then be an array.
    $existing = (& $adb @adbArgs shell "stat -c %s '$remoteFile' 2>/dev/null || echo 0" |
        Select-Object -First 1).ToString().Trim()
    if ($existing -eq "$($file.Size)") {
        Write-Host ("  on device {0,-24} {1}" -f $file.Name, (Format-Size $file.Size)) -ForegroundColor DarkGray
        continue
    }

    Write-Host ("  push      {0,-24} {1} ..." -f $file.Name, (Format-Size $file.Size)) -NoNewline
    & $adb @adbArgs push $src $remoteFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "adb push failed for $($file.Name)." }

    $pushed = (& $adb @adbArgs shell "stat -c %s '$remoteFile'" |
        Select-Object -First 1).ToString().Trim()
    if ($pushed -ne "$($file.Size)") {
        throw "Verify failed for $($file.Name): device reports $pushed bytes, expected $($file.Size)."
    }
    Write-Host " ok" -ForegroundColor Green
}

Write-Host ""
Write-Host "Model is on the device and verified." -ForegroundColor Green
Write-Host "Enable it in the app: Settings -> On-Device Meme Captions." -ForegroundColor Green
