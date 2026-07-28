$b64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes("$PSScriptRoot/BODY_Grandpa_small.jpg"))
$json = @{ ImageData = $b64; ContentType = "image/jpeg"; FileName = "BODY_Grandpa_small.jpg"; Tags = @("portrait","elderly") } | ConvertTo-Json -Depth 5 -Compress
Set-Content -Path "$PSScriptRoot/test_payload.json" -Value $json -Encoding utf8
Write-Host "Wrote $((Get-Item "$PSScriptRoot/test_payload.json").Length) bytes"
