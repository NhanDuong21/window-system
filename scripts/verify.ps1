param([switch]$SkipUI)
. "$PSScriptRoot/common.ps1"
$evidence=Join-Path $script:RepoRoot ('.evidence/verify-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
Invoke-Dotnet restore NyanControlCenter.sln --locked-mode
Invoke-Dotnet build NyanControlCenter.sln -c Release --no-restore
$probe=Start-Process -FilePath $script:Dotnet -ArgumentList @('tests/Nyan.Tests/bin/Release/net10.0-windows/Nyan.Tests.dll','--failure-aggregation-check') -WorkingDirectory $script:RepoRoot -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $evidence 'expected-failure-probe.txt') -RedirectStandardError (Join-Path $evidence 'expected-failure-probe-error.txt')
if($probe.ExitCode -ne 1) { throw 'Test reporter không chặn imported FAIL; không được tiếp tục nghiệm thu.' }
$probe.Dispose()
& $script:Dotnet 'tests/Nyan.Tests/bin/Release/net10.0-windows/Nyan.Tests.dll' --acceptance-safe $evidence
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
    Write-Host "UI controls/capture: $uiRoot (không phải thao tác Explorer/UAC)"
} else { Write-Host 'SKIP UI: do tham số -SkipUI, không tính PASS.' }
Write-Host 'CHƯA CHẠY: Explorer/UAC và production Windows mutation. Chỉ service mô phỏng, dữ liệu app/file riêng và native read-only; resource cases ở Nghiem-Thu-Nyan.cmd cần xác nhận đối tượng riêng.'
if($testExit -ne 0) { throw "Test suite thất bại ($testExit)." }
