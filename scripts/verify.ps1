param([switch]$SkipUI)
. "$PSScriptRoot/common.ps1"
$evidence=Join-Path $script:RepoRoot '.evidence/verify'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
Invoke-Dotnet restore NyanControlCenter.sln --locked-mode
Invoke-Dotnet build NyanControlCenter.sln -c Release --no-restore
& $script:Dotnet 'tests/Nyan.Tests/bin/Release/net10.0-windows/Nyan.Tests.dll' $evidence
$testExit=$LASTEXITCODE
if(-not $SkipUI) {
    $uiRoot=Join-Path $evidence 'ui'
    New-Item -ItemType Directory -Force -Path $uiRoot | Out-Null
    Set-Content -LiteralPath (Join-Path $uiRoot '.nyan-owned') -Value 'Owned UI verification fixtures'
    $app=Join-Path $script:RepoRoot 'src/Nyan.App/bin/Release/net10.0-windows/NyanControlCenter.exe'
    $checkProcess=Start-Process -FilePath $app -ArgumentList @('--ui-checks',('"'+$uiRoot+'"')) -WorkingDirectory $script:RepoRoot -WindowStyle Hidden -PassThru
    if(-not $checkProcess.WaitForExit(60000)) { Stop-Process -Id $checkProcess.Id; throw 'UI test timeout; chỉ đóng process test do script tạo.' }
    $resultFile=Join-Path $uiRoot 'result.txt'
    if(-not(Test-Path -LiteralPath $resultFile)) { throw 'UI tests không trả kết quả.' }
    $uiResult=Get-Content -LiteralPath $resultFile
    if($uiResult[0] -ne 'PASS') { throw ($uiResult -join [Environment]::NewLine) }
    Write-Host "UI: $($uiResult.Count-1) kiểm tra/capture; evidence ignored .evidence/verify/ui"
} else { Write-Host 'SKIP UI: do tham số -SkipUI, không tính PASS.' }
Write-Host 'CHƯA CHẠY: UAC thật, điều khiển service thật, PATH/startup thật và cleanup Temp User. Test mutation dùng fixture có marker.'
if($testExit -ne 0) { throw "Test suite thất bại ($testExit)." }
