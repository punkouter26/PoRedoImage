# Render every .mmd in this folder to .svg. Logs to _render.log.
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot
$files = Get-ChildItem *.mmd | Sort-Object Name
$results = @()
foreach ($f in $files) {
    $out = [System.IO.Path]::ChangeExtension($f.FullName, "svg")
    if (Test-Path $out) { Write-Host "[skip] $($f.Name) already rendered"; continue }
    Write-Host "[render] $($f.Name)"
    $p = Start-Process -FilePath "npx.cmd" -ArgumentList "@mermaid-js/mermaid-cli","-i",$f.FullName,"-o",$out -NoNewWindow -Wait -PassThru -RedirectStandardOutput "_render.log" -RedirectStandardError "_render.err.log"
    if ($p.ExitCode -eq 0 -and (Test-Path $out)) {
        $results += [pscustomobject]@{ File = $f.Name; Status = "ok" }
        Write-Host "  ✔ ok"
    } else {
        $results += [pscustomobject]@{ File = $f.Name; Status = "fail" }
        Write-Host "  ✖ fail"
    }
}
Write-Host "Summary:"
$results | Format-Table -AutoSize
$svgCount = (Get-ChildItem *.svg).Count
Write-Host "Total SVG files: $svgCount of $($files.Count)"