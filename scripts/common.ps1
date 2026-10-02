$ErrorActionPreference = 'Stop'
$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:Dotnet = Join-Path $script:RepoRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $script:Dotnet)) { throw 'Thiếu SDK local. Chạy ./scripts/bootstrap.ps1 trước.' }
$env:DOTNET_ROOT = Split-Path -Parent $script:Dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Set-Location -LiteralPath $script:RepoRoot
function Invoke-Dotnet { & $script:Dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet thất bại ($LASTEXITCODE)." } }
