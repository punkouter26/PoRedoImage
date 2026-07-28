#requires -Version 7
$ErrorActionPreference = "Stop"
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Login via fake endpoint, capture cookies
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginResp = Invoke-WebRequest -Uri "http://localhost:4000/auth/login/fake?email=tester@local" -UseBasicParsing -MaximumRedirection 5 -SkipHttpErrorCheck -WebSession $session
Write-Host "Login status: $($loginResp.StatusCode); cookies captured: $($session.Cookies.Count)"
foreach ($c in $session.Cookies.GetCookies("http://localhost:4000")) {
    Write-Host "  cookie: $($c.Name)=$([string]::new('*', $c.Value.Length))"
}

$b64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes("$PSScriptRoot/BODY_Grandpa_small.jpg"))

# 1. Save original with TAGS + idempotency key
Write-Host "" ; Write-Host "=== 1. Save original with Tags + Idempotency-Key ===" -ForegroundColor Cyan
$key = [Guid]::NewGuid().ToString()
$body = @{ ImageData = $b64; ContentType = "image/jpeg"; FileName = "BODY_Grandpa_small.jpg"; Tags = @("portrait","elderly","family","photograph") } | ConvertTo-Json -Depth 5 -Compress
$headers = @{ "Idempotency-Key" = $key; "Content-Type" = "application/json" }

$resp = Invoke-WebRequest -Uri "http://localhost:4000/api/user-images/original" -Method POST -Body $body -Headers $headers -UseBasicParsing -TimeoutSec 30 -SkipHttpErrorCheck -WebSession $session
Write-Host "Status: $($resp.StatusCode) ; Idempotent-Replay: $($resp.Headers['Idempotent-Replay'])"
Write-Host "Body: $($resp.Content)"

# 2. Replay same idempotency key — server should return cached response
Write-Host "" ; Write-Host "=== 2. Replay same Idempotency-Key (should be 200 with Idempotent-Replay: true) ===" -ForegroundColor Cyan
$resp2 = Invoke-WebRequest -Uri "http://localhost:4000/api/user-images/original" -Method POST -Body $body -Headers $headers -UseBasicParsing -TimeoutSec 30 -SkipHttpErrorCheck -WebSession $session
Write-Host "Status: $($resp2.StatusCode) ; Idempotent-Replay: $($resp2.Headers['Idempotent-Replay'])"
Write-Host "Body: $($resp2.Content)"

# 3. List gallery — should show tags
Write-Host "" ; Write-Host "=== 3. List gallery ===" -ForegroundColor Cyan
$listResp = Invoke-WebRequest -Uri "http://localhost:4000/api/user-images" -UseBasicParsing -WebSession $session -TimeoutSec 10
Write-Host "Status: $($listResp.StatusCode)"
$listResp.Content

# 4. Save a result with tags (Regeneration kind) + different idempotency key
Write-Host "" ; Write-Host "=== 4. Save regeneration with Tags ===" -ForegroundColor Cyan
$pngBytes = New-Object byte[] 256
(New-Object Random).NextBytes($pngBytes)
$pngB64 = [Convert]::ToBase64String($pngBytes)
$key2 = [Guid]::NewGuid().ToString()
$body2 = @{ ImageData = $pngB64; ContentType = "image/png"; Kind = "Regeneration"; Tags = @("vangogh","portrait","oil-painting") } | ConvertTo-Json -Depth 5 -Compress
$headers2 = @{ "Idempotency-Key" = $key2; "Content-Type" = "application/json" }
$resp3 = Invoke-WebRequest -Uri "http://localhost:4000/api/user-images/result" -Method POST -Body $body2 -Headers $headers2 -UseBasicParsing -TimeoutSec 30 -SkipHttpErrorCheck -WebSession $session
Write-Host "Status: $($resp3.StatusCode)"
Write-Host "Body: $($resp3.Content)"

# 5. List gallery
Write-Host "" ; Write-Host "=== 5. List gallery (after result) ===" -ForegroundColor Cyan
$listResp2 = Invoke-WebRequest -Uri "http://localhost:4000/api/user-images" -UseBasicParsing -WebSession $session -TimeoutSec 10
Write-Host "Status: $($listResp2.StatusCode)"
$listResp2.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10

