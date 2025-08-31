# PowerShell script to convert all Mermaid (.mmd) files to SVG format
# This script uses the Mermaid CLI to generate SVG diagrams from Mermaid files
# Requires: npm install -g @mermaid-js/mermaid-cli

param(
    [string]$InputPath = ".",
    [string]$OutputPath = ".",
    [switch]$Verbose,
    [switch]$WhatIf
)

# Script metadata
$ScriptVersion = "1.0.0"
$ScriptName = "generate-svg.ps1"

Write-Host "🔄 $ScriptName v$ScriptVersion - Mermaid to SVG Converter" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Gray

# Check if Mermaid CLI is installed
function Test-MermaidCli {
    try {
        $null = Get-Command mmdc -ErrorAction Stop
        $version = & mmdc --version 2>$null
        Write-Host "✅ Mermaid CLI found: $version" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Error "❌ Mermaid CLI not found. Please install it using: npm install -g @mermaid-js/mermaid-cli"
        return $false
    }
}

# Get all .mmd files in the specified directory
function Get-MermaidFiles {
    param([string]$Path)
    
    $mmdFiles = Get-ChildItem -Path $Path -Filter "*.mmd" -File
    
    if ($mmdFiles.Count -eq 0) {
        Write-Warning "⚠️ No .mmd files found in '$Path'"
        return @()
    }
    
    Write-Host "📁 Found $($mmdFiles.Count) Mermaid file(s):" -ForegroundColor Yellow
    foreach ($file in $mmdFiles) {
        Write-Host "   • $($file.Name)" -ForegroundColor Gray
    }
    
    return $mmdFiles
}

# Convert a single Mermaid file to SVG
function Convert-MermaidToSvg {
    param(
        [System.IO.FileInfo]$InputFile,
        [string]$OutputDirectory
    )
    
    $inputPath = $InputFile.FullName
    $outputFileName = [System.IO.Path]::ChangeExtension($InputFile.Name, ".svg")
    $outputPath = Join-Path $OutputDirectory $outputFileName
    
    if ($WhatIf) {
        Write-Host "🔄 [WHAT-IF] Would convert: $($InputFile.Name) → $outputFileName" -ForegroundColor Magenta
        return $true
    }
    
    try {
        Write-Host "🔄 Converting: $($InputFile.Name) → $outputFileName" -ForegroundColor Blue
        
        # Execute Mermaid CLI conversion
        $arguments = @("-i", $inputPath, "-o", $outputPath, "-t", "neutral", "-b", "white")
        $processInfo = New-Object System.Diagnostics.ProcessStartInfo
        $processInfo.FileName = "mmdc"
        $processInfo.Arguments = $arguments -join " "
        $processInfo.UseShellExecute = $false
        $processInfo.RedirectStandardOutput = $true
        $processInfo.RedirectStandardError = $true
        $processInfo.CreateNoWindow = $true
        
        $process = [System.Diagnostics.Process]::Start($processInfo)
        $process.WaitForExit()
        
        if ($process.ExitCode -eq 0) {
            if (Test-Path $outputPath) {
                $fileSize = (Get-Item $outputPath).Length
                Write-Host "✅ Success: $outputFileName ($fileSize bytes)" -ForegroundColor Green
                
                if ($Verbose) {
                    Write-Host "   📍 Output: $outputPath" -ForegroundColor Gray
                }
                
                return $true
            }
            else {
                Write-Error "❌ Output file not created: $outputPath"
                return $false
            }
        }
        else {
            Write-Error "❌ Mermaid CLI failed with exit code: $($process.ExitCode)"
            return $false
        }
    }
    catch {
        Write-Error "❌ Error converting $($InputFile.Name): $($_.Exception.Message)"
        return $false
    }
}

# Main execution
function Main {
    # Validate Mermaid CLI installation
    if (-not (Test-MermaidCli)) {
        exit 1
    }
    
    # Resolve paths
    $resolvedInputPath = Resolve-Path $InputPath -ErrorAction SilentlyContinue
    if (-not $resolvedInputPath) {
        Write-Error "❌ Input path not found: $InputPath"
        exit 1
    }
    
    $resolvedOutputPath = $OutputPath
    if (-not (Test-Path $resolvedOutputPath)) {
        if ($WhatIf) {
            Write-Host "🔄 [WHAT-IF] Would create directory: $resolvedOutputPath" -ForegroundColor Magenta
        }
        else {
            Write-Host "📁 Creating output directory: $resolvedOutputPath" -ForegroundColor Yellow
            New-Item -Path $resolvedOutputPath -ItemType Directory -Force | Out-Null
        }
    }
    
    # Get Mermaid files
    $mermaidFiles = Get-MermaidFiles -Path $resolvedInputPath
    if ($mermaidFiles.Count -eq 0) {
        exit 0
    }
    
    Write-Host ""
    Write-Host "🚀 Starting conversion process..." -ForegroundColor Cyan
    Write-Host ""
    
    # Convert each file
    $successCount = 0
    $totalCount = $mermaidFiles.Count
    
    foreach ($file in $mermaidFiles) {
        $success = Convert-MermaidToSvg -InputFile $file -OutputDirectory $resolvedOutputPath
        if ($success) {
            $successCount++
        }
        Write-Host ""
    }
    
    # Summary
    Write-Host "=" * 60 -ForegroundColor Gray
    if ($WhatIf) {
        Write-Host "🔍 [WHAT-IF] Conversion Summary:" -ForegroundColor Magenta
        Write-Host "   • Would process: $totalCount file(s)" -ForegroundColor Gray
        Write-Host "   • Input directory: $resolvedInputPath" -ForegroundColor Gray
        Write-Host "   • Output directory: $resolvedOutputPath" -ForegroundColor Gray
    }
    else {
        Write-Host "📊 Conversion Summary:" -ForegroundColor Cyan
        Write-Host "   • Successful: $successCount/$totalCount" -ForegroundColor Green
        
        if ($successCount -eq $totalCount) {
            Write-Host "🎉 All conversions completed successfully!" -ForegroundColor Green
        }
        elseif ($successCount -gt 0) {
            Write-Host "⚠️ Some conversions failed. Check the errors above." -ForegroundColor Yellow
        }
        else {
            Write-Host "❌ All conversions failed." -ForegroundColor Red
            exit 1
        }
    }
}

# Run the main function
Main

# Usage examples:
# .\generate-svg.ps1                          # Convert all .mmd files in current directory
# .\generate-svg.ps1 -Verbose                 # Show detailed output
# .\generate-svg.ps1 -WhatIf                  # Preview what would be converted
# .\generate-svg.ps1 -InputPath ".\diagrams"  # Specify input directory
# .\generate-svg.ps1 -OutputPath ".\output"   # Specify output directory
