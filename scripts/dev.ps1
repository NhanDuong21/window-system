. "$PSScriptRoot/common.ps1"
Invoke-Dotnet restore NyanControlCenter.sln --locked-mode
Invoke-Dotnet build src/Nyan.App/Nyan.App.csproj --no-restore
$executable=Join-Path $script:RepoRoot 'src/Nyan.App/bin/Debug/net10.0-windows/NyanControlCenter.exe'
Start-Process -FilePath $executable -WorkingDirectory $script:RepoRoot -WindowStyle Normal
