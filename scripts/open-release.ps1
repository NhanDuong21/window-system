$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$release=Get-Content -LiteralPath "$PSScriptRoot/release.json" -Raw | ConvertFrom-Json
$app=Join-Path $repoRoot ('artifacts/'+$release.folder+'/NyanControlCenter.exe')
if(-not(Test-Path -LiteralPath $app)) { throw 'Chưa có artifact; đọc README.md để build.' }
Start-Process -FilePath $app -WorkingDirectory (Split-Path -Parent $app) -WindowStyle Normal
