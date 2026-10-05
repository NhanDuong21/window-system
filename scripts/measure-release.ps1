param([string]$AppPath,[ValidateRange(1,10)][int]$Runs=3)
. "$PSScriptRoot/common.ps1"
. "$PSScriptRoot/acceptance-report.ps1"
if(-not $AppPath){$release=Get-Content -LiteralPath "$PSScriptRoot/release.json" -Raw | ConvertFrom-Json;$AppPath=Join-Path $script:RepoRoot ('artifacts/'+$release.folder+'/NyanControlCenter.exe')}
$AppPath=[IO.Path]::GetFullPath($AppPath);$folder=Split-Path -Parent $AppPath
$manifest=Read-NyanEvidenceJson (Join-Path $folder 'build-manifest.json')
foreach($file in $manifest.files) {
    $path=[IO.Path]::GetFullPath((Join-Path $folder $file.path))
    if(-not $path.StartsWith($folder+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw 'Artifact checksum mismatch.'}
}
$root=Join-Path $script:RepoRoot ('.evidence/performance-series-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
# Fixed plan written before launching: no retry-until-PASS behavior.
[ordered]@{plannedRuns=$Runs;artifactVersion=$manifest.version;sourceCommit=$manifest.sourceCommit;exeSha256=$manifest.sha256;condition='Unprofiled; native read-only capture; hidden launcher window, actual foreground/DPI/task state recorded by child; Settings settle5s/sample10s';budgetFirstPaintMs=2500;budgetIdleCpuPercent=1;budgetWorkingSetMiB=350} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'plan.json') -Encoding utf8
$ui=Join-Path $root 'ui';New-Item -ItemType Directory -Path $ui | Out-Null
Set-Content -LiteralPath (Join-Path $ui '.nyan-owned') -Value 'Owned artifact WPF fixtures'
$savedDotnetRoot=$env:DOTNET_ROOT;$savedDotnetRootX64=$env:DOTNET_ROOT_X64;$savedDotnetRootX86=$env:DOTNET_ROOT_X86;$savedRollForward=$env:DOTNET_ROLL_FORWARD
try {
    $env:DOTNET_ROOT='C:\NyanNoSdk';$env:DOTNET_ROOT_X64='C:\NyanNoSdk';$env:DOTNET_ROOT_X86='C:\NyanNoSdk';$env:DOTNET_ROLL_FORWARD='Disable'
    $uiProcess=Start-Process -FilePath $AppPath -ArgumentList @('--ui-checks',('"'+$ui+'"')) -WorkingDirectory $folder -WindowStyle Hidden -PassThru
    if(-not $uiProcess.WaitForExit(60000)){throw 'Owned artifact UI timeout; evidence retained.'}
    $uiProcess.Dispose()
} finally {$env:DOTNET_ROOT=$savedDotnetRoot;$env:DOTNET_ROOT_X64=$savedDotnetRootX64;$env:DOTNET_ROOT_X86=$savedDotnetRootX86;$env:DOTNET_ROLL_FORWARD=$savedRollForward}
if(-not(Test-Path -LiteralPath (Join-Path $ui 'result.txt')) -or @(Get-Content -LiteralPath (Join-Path $ui 'result.txt'))[0] -cne 'PASS'){throw 'Artifact WPF fixture regression failed.'}
Write-Host "Artifact UI / self-contained invalid-SDK-root check PASS: $ui"
$observations=New-Object Collections.Generic.List[object]
for($index=1;$index -le $Runs;$index++) {
    $owned=Join-Path $root ('run-'+$index);New-Item -ItemType Directory -Path $owned | Out-Null
    Set-Content -LiteralPath (Join-Path $owned '.nyan-owned') -Value 'Owned read-only performance case'
    $ownership=@{product='Nyan acceptance';id=[guid]::NewGuid().ToString('N');root=$owned;version=[string]$manifest.version;sourceCommit=[string]$manifest.sourceCommit;exeSha256=[string]$manifest.sha256} | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $owned 'ownership.json'),$ownership,(New-Object Text.UTF8Encoding($true)))
    $timedOut=$false;$launch=Start-Process -FilePath $AppPath -ArgumentList @('--capture-native',('"'+$owned+'"')) -WorkingDirectory $folder -WindowStyle Hidden -PassThru
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path -LiteralPath (Join-Path $owned 'result.txt'))) { if($watch.Elapsed.TotalSeconds -gt 150){$timedOut=$true;break};Start-Sleep -Milliseconds 250 }
    $performance=Get-NyanPerformanceResult (Join-Path $owned 'release-performance.json')
    $native=if($timedOut){'TIMEOUT'}elseif(@(Get-Content -LiteralPath (Join-Path $owned 'result.txt'))[0] -ceq 'PASS'){'PASS'}else{'FAIL'}
    $summary=Write-NyanAcceptanceSummary $owned ''
    $observations.Add([pscustomobject]@{run=$index;nativeRead=$native;performance=$performance;ordinaryUser=$summary.ordinaryUser;privatePersistence=$summary.privatePersistence;timedOut=$timedOut})
    [ordered]@{plannedRuns=$Runs;completedRuns=$observations.Count;observations=@($observations.ToArray());overall=if(@($observations | Where-Object {$_.nativeRead -ne 'PASS' -or $_.performance.result -ne 'PASS' -or $_.ordinaryUser -ne 'PASS' -or $_.privatePersistence -ne 'PASS'}).Count -gt 0){'FAIL'}else{'PASS'};productionMutations='NOT_RUN';explorer='WAITING_FOR_USER'} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $root 'series-summary.json') -Encoding utf8
    $launch.Dispose()
    Write-Host ("Run $index/$Runs native=$native performance="+$performance.result+" evidence=$owned")
    if($timedOut){throw 'Read capture timeout; all evidence retained, no retry or process termination.'}
    # Capture writes result immediately before closing; allow disposal before the next launch.
    Start-Sleep -Seconds 2
}
Write-Host "Fixed series retained: $root"
if(@($observations | Where-Object {$_.nativeRead -ne 'PASS' -or $_.performance.result -ne 'PASS' -or $_.ordinaryUser -ne 'PASS' -or $_.privatePersistence -ne 'PASS'}).Count -gt 0){exit 1}
