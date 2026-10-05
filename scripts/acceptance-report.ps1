# Pure evidence summarization; no Windows resource actions or process inventory.
function Read-NyanEvidenceJson([string]$Path) {
    if(-not(Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}
function Get-NyanPerformanceResult([string]$Path) {
    if(-not(Test-Path -LiteralPath $Path -PathType Leaf)) { return [pscustomobject]@{result='NOT_RUN';cpu='NOT_RUN';firstFrame='NOT_RUN';memory='NOT_RUN';metrics=$null;reason='Chưa có số đo'} }
    try {
        $p=Read-NyanEvidenceJson $Path
        foreach($name in @('firstPaintMs','idleCpuPercent','workingSetMiB','budgetFirstPaintMs','budgetIdleCpuPercent','budgetWorkingSetMiB')) {
            $v=$p.$name
            if($null -eq $v -or $v -is [string] -or $v -is [bool]) { throw 'Invalid metric' }
            $n=[double]$v
            if([double]::IsNaN($n) -or [double]::IsInfinity($n) -or $n -lt 0) { throw 'Invalid metric' }
        }
        if($p.budgetFirstPaintMs -ne 2500 -or $p.budgetIdleCpuPercent -ne 1 -or $p.budgetWorkingSetMiB -ne 350) { throw 'Unexpected budgets' }
        $cpu=if($p.idleCpuPercent -le 1){'PASS'}else{'FAIL'}
        $frame=if($p.firstPaintMs -le 2500){'PASS'}else{'FAIL'}
        $memory=if($p.workingSetMiB -le 350){'PASS'}else{'FAIL'}
        return [pscustomobject]@{result=if(@($cpu,$frame,$memory) -contains 'FAIL'){'FAIL'}else{'PASS'};cpu=$cpu;firstFrame=$frame;memory=$memory;metrics=$p;reason=$null}
    } catch { return [pscustomobject]@{result='FAIL';cpu='FAIL';firstFrame='FAIL';memory='FAIL';metrics=$null;reason='Số đo thiếu, sai định dạng hoặc ngân sách đã thay đổi'} }
}
function Write-NyanAcceptanceSummary([string]$Root,[string]$Declaration,[AllowNull()][string]$Observation=$null) {
    $native='NOT_RUN';$persistence='NOT_RUN';$ordinary='NOT_RUN';$reopen='WAITING_FOR_USER';$human='WAITING_FOR_USER';$context=$null
    $resultPath=Join-Path $Root 'result.txt'
    if(Test-Path -LiteralPath $resultPath) { $head=@(Get-Content -LiteralPath $resultPath)[0];$native=if($head -ceq 'PASS'){'PASS'}else{'FAIL'} }
    try { $data=Read-NyanEvidenceJson (Join-Path $Root 'persistence.json');if($null -ne $data){$persistence=if($data.result -ceq 'PASS'){'PASS'}else{'FAIL'}} } catch { $persistence='FAIL' }
    try { $context=Read-NyanEvidenceJson (Join-Path $Root 'read-context.json');if($null -ne $context){$ordinary=if($context.elevated -is [bool] -and -not $context.elevated){'PASS'}else{'FAIL'}} } catch { $ordinary='FAIL' }
    $performance=Get-NyanPerformanceResult (Join-Path $Root 'release-performance.json')
    try {
        $first=Read-NyanEvidenceJson (Join-Path $Root 'session-first-close.json');$second=Read-NyanEvidenceJson (Join-Path $Root 'session-closed.json')
        $opens=@(Get-ChildItem -LiteralPath $Root -Filter 'session-open-*.json' | ForEach-Object { Read-NyanEvidenceJson $_.FullName } | Sort-Object at)
        if($null -ne $first -and $null -ne $second -and $opens.Count -ge 2) {
            $reopen=if($first.dark -is [bool] -and $second.dark -is [bool] -and $opens[-1].dark -is [bool] -and $first.dark -eq $opens[-1].dark){'PASS'}else{'FAIL'}
        }
    } catch { $reopen='FAIL' }
    if(-not [string]::IsNullOrWhiteSpace($Observation)) { $human=if($Observation.Trim() -ceq 'OK'){'PASS'}else{'FAIL'} }
    $automatic=if(@($native,$persistence,$ordinary,$performance.result,$reopen,$human) -contains 'FAIL'){'FAIL'}else{'PARTIAL'}
    $summary=[ordered]@{at=(Get-Date).ToUniversalTime().ToString('o');explorer=if($Declaration -ceq 'EXPLORER'){'USER_DECLARED'}else{'UNVERIFIED'};nativeRead=$native;privatePersistence=$persistence;ordinaryUser=$ordinary;performance=$performance;uiReopen=$reopen;humanUi=$human;humanObservation=$Observation;productionMutations='NOT_RUN';uac='WAITING_FOR_USER';automaticResult=$automatic}
    # A summary may be updated; the original observations and measurements remain intact.
    $temporary=Join-Path $Root ('summary-'+[guid]::NewGuid().ToString('N')+'.tmp')
    try { $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporary -Encoding utf8;Move-Item -LiteralPath $temporary -Destination (Join-Path $Root 'acceptance-summary.json') -Force } finally { if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary} }
    return [pscustomobject]$summary
}
function Show-NyanAcceptanceSummary($Summary) {
    $labels=@{PASS='ĐẠT';FAIL='KHÔNG ĐẠT';NOT_RUN='CHƯA CHẠY';WAITING_FOR_USER='CHƯA HOÀN TẤT'}
    Write-Host ('Đọc dữ liệu Windows: '+$labels[$Summary.nativeRead])
    Write-Host ('Lưu dữ liệu ứng dụng: '+$labels[$Summary.privatePersistence])
    $p=$Summary.performance
    $detail=if($null -ne $p.metrics){' — '+([double]$p.metrics.idleCpuPercent).ToString('N2',[Globalization.CultureInfo]::GetCultureInfo('vi-VN'))+'%, giới hạn 1%'}else{' — '+$p.reason}
    Write-Host ('Hiệu năng CPU idle: '+$labels[$p.cpu]+$detail)
    Write-Host ('Hiệu năng khung hình đầu / RAM: '+$labels[$p.firstFrame]+' / '+$labels[$p.memory])
    Write-Host ('Kiểm tra mở lại/theme: '+$labels[$Summary.uiReopen]+'; checklist UI: '+$labels[$Summary.humanUi])
    Write-Host 'Thao tác Windows/UAC: CHƯA CHẠY trong phần tổng hợp tự động'
}
