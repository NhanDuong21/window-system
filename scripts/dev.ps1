. "$PSScriptRoot/common.ps1"
Invoke-Dotnet restore NyanControlCenter.sln --locked-mode
Invoke-Dotnet publish src/Nyan.App/Nyan.App.csproj -c Debug -r win-x64 --self-contained true -o .runtime/dev
$executable=Join-Path $script:RepoRoot '.runtime/dev/NyanControlCenter.exe'
Start-Process -FilePath $executable -WorkingDirectory $script:RepoRoot -WindowStyle Normal
