. "$PSScriptRoot/common.ps1"
if(git status --porcelain --untracked-files=normal) { throw 'Commit source đã kiểm tra trước khi package.' }
$sourceCommit=git rev-parse HEAD
$release=Get-Content -LiteralPath "$PSScriptRoot/release.json" -Raw | ConvertFrom-Json
$output=Join-Path $script:RepoRoot ('artifacts/'+$release.folder)
$archive=$output+'.zip'
if((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $archive)) { throw 'Artifact version này đã tồn tại; không ghi đè. Tăng version để giữ bản cũ.' }
Invoke-Dotnet restore src/Nyan.App/Nyan.App.csproj -r win-x64 --locked-mode
Invoke-Dotnet publish src/Nyan.App/Nyan.App.csproj -c Release -r win-x64 --self-contained true --no-restore "-p:SourceRevisionId=$sourceCommit" -o $output
$binary=Join-Path $output 'NyanControlCenter.exe'
$sha=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'docs/OPERATIONS.md') -Destination (Join-Path $output 'HUONG-DAN.md')
$files=@(Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object { [ordered]@{path=$_.FullName.Substring($output.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
$manifest=[ordered]@{product='Nyan Control Center';version=$release.version;sourceCommit=$sourceCommit;rid=$release.rid;selfContained=$true;signed=$false;sha256=$sha;builtAt=(Get-Date).ToUniversalTime().ToString('o');entry='NyanControlCenter.exe';files=$files}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'build-manifest.json') -Encoding utf8
Compress-Archive -Path "$output/*" -DestinationPath $archive
Write-Host "Artifact: $output"
Write-Host "Source commit: $sourceCommit"
Write-Host "EXE SHA-256: $sha"
Write-Host "ZIP SHA-256: $((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash)"
