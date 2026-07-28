$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$login = Invoke-WebRequest -Uri "http://localhost:4000/auth/login/fake?email=tester@local" -UseBasicParsing -MaximumRedirection 5 -SkipHttpErrorCheck -WebSession $session
$list = Invoke-WebRequest -Uri "http://localhost:4000/api/user-images" -UseBasicParsing -WebSession $session | ConvertFrom-Json
foreach ($img in $list) {
    try { Invoke-WebRequest -Uri "http://localhost:4000/api/user-images/$($img.id)" -Method DELETE -UseBasicParsing -WebSession $session -SkipHttpErrorCheck | Out-Null } catch {}
}
Write-Host "Deleted $($list.Count) images"
$list2 = Invoke-WebRequest -Uri "http://localhost:4000/api/user-images" -UseBasicParsing -WebSession $session | ConvertFrom-Json
Write-Host "Now have $($list2.Count) images"
