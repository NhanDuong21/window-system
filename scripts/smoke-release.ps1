param([string]$AppPath)
. "$PSScriptRoot/common.ps1"
if(-not $AppPath) { $release=Get-Content -LiteralPath "$PSScriptRoot/release.json" -Raw | ConvertFrom-Json; $AppPath=Join-Path $script:RepoRoot ('artifacts/'+$release.folder+'/NyanControlCenter.exe') }
$AppPath=[IO.Path]::GetFullPath($AppPath)
if(-not(Test-Path -LiteralPath $AppPath -PathType Leaf)) { throw 'Thiếu bản EXE cần smoke-test.' }
$evidence=Join-Path $script:RepoRoot ('.evidence/release-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
foreach($mode in @('ui-checks','capture-native')) {
    $owned=Join-Path $evidence $mode
    New-Item -ItemType Directory -Path $owned | Out-Null
    Set-Content -LiteralPath (Join-Path $owned '.nyan-owned') -Value 'Owned verification output; native mode is read-only'
    if($mode -eq 'capture-native') {
        $artifact=Get-Content -LiteralPath (Join-Path (Split-Path -Parent $AppPath) 'build-manifest.json') -Raw | ConvertFrom-Json
        @{product='Nyan acceptance';id=[guid]::NewGuid().ToString('N');root=$owned;sourceCommit=[string]$artifact.sourceCommit;exeSha256=[string]$artifact.sha256} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $owned 'ownership.json') -Encoding utf8
    }
    $launch=Start-Process -FilePath $AppPath -ArgumentList @('--'+$mode,('"'+$owned+'"')) -WorkingDirectory (Split-Path -Parent $AppPath) -WindowStyle Hidden -PassThru
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $resultFile=Join-Path $owned 'result.txt'
    while(-not(Test-Path -LiteralPath $resultFile)) {
        if($watch.Elapsed.TotalSeconds -gt 120) { throw "Smoke-test timeout ($mode); evidence: $owned. Không kill PID khi chưa xác minh lại danh tính." }
        Start-Sleep -Milliseconds 250
    }
    $result=Get-Content -LiteralPath $resultFile
    if($result[0] -ne 'PASS') { throw ($result -join [Environment]::NewLine) }
    Write-Host "PASS $mode; evidence local ignored: $owned"
    if($mode -eq 'capture-native') {
        $performance=Get-Content -LiteralPath (Join-Path $owned 'release-performance.json') -Raw | ConvertFrom-Json
        if($performance.firstPaintMs -gt $performance.budgetFirstPaintMs -or $performance.idleCpuPercent -gt $performance.budgetIdleCpuPercent -or $performance.workingSetMiB -gt $performance.budgetWorkingSetMiB) { throw 'Vượt ngân sách hiệu năng release; xem release-performance.json.' }
        $launches=Get-Content -LiteralPath (Join-Path $owned 'launch.txt')
        if(-not($launches | Where-Object { $_ -match 'elevated=False' })) { throw 'Chưa chứng minh app chính chạy user thường.' }
        $directWindowsContext=[bool]($result | Where-Object { $_ -match 'packaged=False; redirectedAppData=False' })
        $persistence=Get-Content -LiteralPath (Join-Path $owned 'persistence.json') -Raw | ConvertFrom-Json
        if($persistence.result -ne 'PASS') { throw 'Isolated app persistence/backup/restore failed.' }
        [ordered]@{version=$artifact.version;sourceCommit=$artifact.sourceCommit;exeSha256=$artifact.sha256;readOnlyNative='PASS';ordinaryUser='PASS';fixtureUi='PASS';privatePersistence='PASS';performance='PASS';explorer='WAITING_FOR_USER';directWindowsMutationContext=if($directWindowsContext){'AVAILABLE_NOT_EXECUTED'}else{'UNVERIFIED_HOST_REDIRECTION'};realMutations='NOT_RUN'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'smoke-summary.json') -Encoding utf8
        if(-not $directWindowsContext) { Write-Host 'CHƯA NGHIỆM THU: mutation context trực tiếp. Host chuyển hướng AppData; production guard giữ nguyên. Mở EXE từ File Explorer để nghiệm thu. Native read-only/fixture checks phía trên đã PASS.' }
        Write-Host "Performance: first frame $($performance.firstPaintMs) ms, idle CPU $([Math]::Round($performance.idleCpuPercent,3))%, working set $([Math]::Round($performance.workingSetMiB,1)) MiB."
    }
    $launch.Dispose()
}
Write-Host 'CHƯA CHẠY: service/UAC/System mutation thật; không cleanup Temp User hoặc đổi startup/PATH thật.'
