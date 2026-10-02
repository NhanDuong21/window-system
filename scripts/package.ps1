. "$PSScriptRoot/common.ps1"
if(git status --porcelain --untracked-files=normal) { throw 'Commit source đã kiểm tra trước khi package.' }
$sourceCommit=git rev-parse HEAD
$output=Join-Path $script:RepoRoot 'artifacts/NyanControlCenter-1.0.0-win-x64'
Invoke-Dotnet publish src/Nyan.App/Nyan.App.csproj -c Release -r win-x64 --self-contained true -p:SourceRevisionId=$sourceCommit -o $output
$binary=Join-Path $output 'NyanControlCenter.exe'
$sha=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
$manifest=[ordered]@{product='Nyan Control Center';version='1.0.0';sourceCommit=$sourceCommit;rid='win-x64';selfContained=$true;signed=$false;sha256=$sha;builtAt=(Get-Date).ToUniversalTime().ToString('o');entry='NyanControlCenter.exe'}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'build-manifest.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'docs/OPERATIONS.md') -Destination (Join-Path $output 'HUONG-DAN.md')
$archive=Join-Path $script:RepoRoot 'artifacts/NyanControlCenter-1.0.0-win-x64.zip'
Compress-Archive -Path "$output/*" -DestinationPath $archive -Force
Write-Host "Artifact: $output"
Write-Host "Source commit: $sourceCommit"
Write-Host "EXE SHA-256: $sha"
Write-Host "ZIP SHA-256: $((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash)"
