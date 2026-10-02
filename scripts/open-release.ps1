$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$app=Join-Path $repoRoot 'artifacts/NyanControlCenter-1.0.0-win-x64/NyanControlCenter.exe'
if(-not(Test-Path -LiteralPath $app)) { throw 'Chưa có artifact; đọc README.md để build.' }
Start-Process -FilePath $app -WorkingDirectory (Split-Path -Parent $app) -WindowStyle Normal
