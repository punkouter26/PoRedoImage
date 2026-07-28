# Render every .mmd in this folder to .svg AND a standalone .html viewer.
# Usage: pwsh -File docs/diagrams/_render.ps1 [-Force]
# Logs to _render.log / _render.err.log.
param([switch]$Force)

$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot

$files = Get-ChildItem *.mmd | Sort-Object Name
$results = @()

foreach ($f in $files) {
    $svg = [System.IO.Path]::ChangeExtension($f.FullName, "svg")
    if ((Test-Path $svg) -and -not $Force) {
        Write-Host "[skip] $($f.Name) already rendered"
        $results += [pscustomobject]@{ File = $f.Name; Svg = "skip"; Html = "-" }
        continue
    }

    Write-Host "[render] $($f.Name)"
    $p = Start-Process -FilePath "npx.cmd" `
        -ArgumentList "-y", "@mermaid-js/mermaid-cli", "-i", $f.FullName, "-o", $svg, "-b", "transparent" `
        -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput "_render.log" -RedirectStandardError "_render.err.log"

    $ok = ($p.ExitCode -eq 0) -and (Test-Path $svg)
    $results += [pscustomobject]@{ File = $f.Name; Svg = $(if ($ok) { "ok" } else { "fail" }); Html = "-" }
    Write-Host $(if ($ok) { "  OK" } else { "  FAIL (see _render.err.log)" })
}

# ── Standalone HTML viewer per diagram ────────────────────────────────────────
# The SVG is inlined so each page is a single self-contained file that opens from
# disk with no server, no CDN, and no network. The .mmd source ships alongside it
# so the page is also the reviewable artifact.
foreach ($f in $files) {
    $svg = [System.IO.Path]::ChangeExtension($f.FullName, "svg")
    if (-not (Test-Path $svg)) { continue }

    $html = [System.IO.Path]::ChangeExtension($f.FullName, "html")
    $name = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
    $svgBody = (Get-Content $svg -Raw) -replace '<\?xml.*?\?>', '' -replace '<!DOCTYPE.*?>', ''
    $source = [System.Net.WebUtility]::HtmlEncode((Get-Content $f.FullName -Raw))

    $doc = @"
<!doctype html>
<html lang="en"><head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>$name · PoRedoImage</title>
<style>
  :root { color-scheme: light dark; --bg:#fff; --fg:#111; --mut:#666; --line:#e3e3e3; --card:#fafafa; }
  @media (prefers-color-scheme: dark) {
    :root { --bg:#14161a; --fg:#e8e8e8; --mut:#9aa0a6; --line:#2a2e35; --card:#1b1e24; }
  }
  * { box-sizing: border-box; }
  body { margin:0; background:var(--bg); color:var(--fg);
         font:15px/1.55 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif; }
  header { padding:18px 22px; border-bottom:1px solid var(--line); }
  h1 { margin:0; font-size:18px; font-weight:650; }
  header p { margin:4px 0 0; color:var(--mut); font-size:13px; }
  main { padding:22px; }
  .frame { border:1px solid var(--line); border-radius:10px; background:var(--card);
           padding:16px; overflow-x:auto; }
  .frame svg { max-width:100%; height:auto; display:block; }
  details { margin-top:20px; border:1px solid var(--line); border-radius:10px; background:var(--card); }
  summary { cursor:pointer; padding:12px 16px; font-weight:600; font-size:14px; }
  pre { margin:0; padding:0 16px 16px; overflow-x:auto; font-size:12.5px; line-height:1.5; }
  a { color:inherit; }
</style>
</head><body>
<header>
  <h1>$name</h1>
  <p>PoRedoImage architecture diagram &middot; rendered from <code>$($f.Name)</code></p>
</header>
<main>
  <div class="frame">$svgBody</div>
  <details><summary>Mermaid source</summary><pre>$source</pre></details>
</main>
</body></html>
"@

    Set-Content -Path $html -Value $doc -Encoding UTF8
    ($results | Where-Object File -eq $f.Name)[0].Html = "ok"
    Write-Host "[html]  $([System.IO.Path]::GetFileName($html))"
}

Write-Host "`nSummary:"
$results | Format-Table -AutoSize
Write-Host "SVG: $((Get-ChildItem *.svg -ErrorAction SilentlyContinue).Count) / $($files.Count)   HTML: $((Get-ChildItem *.html -ErrorAction SilentlyContinue).Count) / $($files.Count)"
