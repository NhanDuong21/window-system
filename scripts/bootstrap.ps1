$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$toolsRoot=Join-Path $repoRoot '.tools'
New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
$sdkArchive=Join-Path $toolsRoot 'sdk.zip'
$sdkRoot=Join-Path $toolsRoot 'dotnet'
if(Test-Path -LiteralPath (Join-Path $sdkRoot 'dotnet.exe')) { Write-Host 'SDK local đã có; không thay đổi toolchain global.'; exit 0 }
Invoke-WebRequest 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip' -OutFile $sdkArchive
$expected='24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430'
if((Get-FileHash -LiteralPath $sdkArchive -Algorithm SHA512).Hash.ToLowerInvariant() -ne $expected) { throw 'SDK SHA-512 không khớp; không giải nén/chạy.' }
Expand-Archive -LiteralPath $sdkArchive -DestinationPath $sdkRoot
Write-Host 'SDK official đã xác minh, đặt tại .tools/dotnet. Không cài global.'
