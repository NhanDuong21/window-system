# Human opens Nghiem-Thu-Nyan.cmd from Explorer. No SDK, elevation or policy bypass.
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$release=Get-Content -LiteralPath "$PSScriptRoot/release.json" -Raw | ConvertFrom-Json
$folder=Join-Path $repoRoot ('artifacts/'+$release.folder)
$app=Join-Path $folder 'NyanControlCenter.exe'
$manifest=Get-Content -LiteralPath (Join-Path $folder 'build-manifest.json') -Raw | ConvertFrom-Json
if($manifest.version -ne $release.version -or $manifest.rid -ne 'win-x64' -or $manifest.files.Count -lt 1) { throw 'Release manifest mismatch.' }
foreach($file in $manifest.files) {
    $path=[IO.Path]::GetFullPath((Join-Path $folder $file.path))
    if(-not $path.StartsWith($folder+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path outside release.' }
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw ('Checksum mismatch: '+$file.path) }
}
$id=[guid]::NewGuid().ToString('N')
$root=Join-Path $repoRoot ('.evidence/acceptance-'+$id)
New-Item -ItemType Directory -Path $root | Out-Null
@{product='Nyan acceptance';id=$id;root=$root;at=(Get-Date).ToUniversalTime().ToString('o');version=[string]$manifest.version;sourceCommit=[string]$manifest.sourceCommit;exeSha256=[string]$manifest.sha256} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'ownership.json') -Encoding utf8
$declaration=Read-Host 'Did YOU open Nghiem-Thu-Nyan.cmd from File Explorer? Type EXPLORER or leave blank'
@{at=(Get-Date).ToUniversalTime().ToString('o');explorer=if($declaration -ceq 'EXPLORER'){'USER_DECLARED'}else{'UNVERIFIED'};basis='Human declaration, not inferred from child process'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'entry-declaration.json') -Encoding utf8
Write-Host "Release $($manifest.version), source $($manifest.sourceCommit)"
Write-Host "EXE SHA256 $($manifest.sha256)"
Write-Host "Private evidence: $root"
function Invoke-Acceptance([string]$mode,[string]$resultLeaf) {
    $result=Join-Path $root $resultLeaf
    if(Test-Path -LiteralPath $result) { throw 'This case already has evidence. Start a new acceptance run; no evidence overwrite.' }
    $launch=Start-Process -FilePath $app -ArgumentList @($mode,('"'+$root+'"')) -WorkingDirectory $folder -WindowStyle Normal -PassThru
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path -LiteralPath $result)) {
        Start-Sleep -Milliseconds 300
        if($mode -eq '--capture-native' -and $watch.Elapsed.TotalSeconds -gt 150) { throw 'Read smoke timed out. Keep evidence; no automatic retry.' }
        if($watch.Elapsed.TotalSeconds -gt 1800) { throw 'Session still has no completion result. Keep evidence and inspect the window; no automatic retry or process termination.' }
    }
    # Existing user launcher may create a verified limited child. Wait for its session result, not just launcher exit.
    Start-Sleep -Milliseconds 500
    $launch.Dispose()
}
Invoke-Acceptance '--capture-native' 'result.txt'
$readResult=Get-Content -LiteralPath (Join-Path $root 'result.txt')
if($readResult[0] -ne 'PASS') { throw ("Read acceptance failed:`n"+($readResult -join [Environment]::NewLine)+"`nEvidence: "+$root) }
$context=Get-Content -LiteralPath (Join-Path $root 'read-context.json') -Raw | ConvertFrom-Json
$persistence=Get-Content -LiteralPath (Join-Path $root 'persistence.json') -Raw | ConvertFrom-Json
if($context.elevated -or $persistence.result -ne 'PASS') { throw 'Ordinary user / private persistence failed.' }
Write-Host "Native read PASS; elevated=$($context.elevated); packaged=$($context.packaged); redirectedAppData=$($context.redirectedAppData)"
Invoke-Acceptance '--acceptance-session' 'session-closed.json'
Write-Host 'Reopen the same private store to check theme persistence; close this second window.'
# Keep the first close evidence, then reuse only this same isolated store.
Move-Item -LiteralPath (Join-Path $root 'session-closed.json') -Destination (Join-Path $root 'session-first-close.json')
Invoke-Acceptance '--acceptance-session' 'session-closed.json'
$first=Get-Content -LiteralPath (Join-Path $root 'session-first-close.json') -Raw | ConvertFrom-Json
$second=Get-Content -LiteralPath (Join-Path $root 'session-closed.json') -Raw | ConvertFrom-Json
$opens=@(Get-ChildItem -LiteralPath $root -Filter 'session-open-*.json' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } | Sort-Object at)
$manual=Read-Host 'After both real windows: navigation, Ctrl+K, F5 selection, resize, Vietnamese text and theme reopen OK? Type OK or describe the issue'
@{at=(Get-Date).ToUniversalTime().ToString('o');humanObservation=$manual;themeOnFirstClose=$first.dark;themeOnSecondOpen=$opens[-1].dark;themeReopenNativeMatches=($first.dark -eq $opens[-1].dark);explorer=if($declaration -ceq 'EXPLORER'){'USER_DECLARED'}else{'UNVERIFIED'};nativeRead='PASS';privatePersistence=$persistence.result;productionMutations='NOT_RUN';uac='WAITING_FOR_USER';automaticResult='PARTIAL'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'acceptance-summary.json') -Encoding utf8
if($context.packaged -or $context.redirectedAppData) { Write-Host 'Host redirection detected. Guard unchanged; open this launcher from Explorer. No data migration. Mutation cases unavailable.'; return }
Write-Host 'Default read-only acceptance complete. Optional Windows resource cases require their own explicit dialog; blank exits.'
Write-Host '1 User environment | 2 User startup | 3 owned process/port | 4 owned temp recycle | 5 System environment | 6 registered service'
Write-Host 'Service registration/removal: separate elevated PowerShell, service-fixture.ps1 -Root <this evidence root> -Action Register/Remove; each asks exact object confirmation.'
while($true) {
    $choice=Read-Host 'Optional case (1-6), or Enter to finish'
    if(-not $choice) { break }
    $kind=@{'1'='environment';'2'='startup';'3'='process-port';'4'='cleanup';'5'='system-environment';'6'='service'}[$choice]
    if(-not $kind) { Write-Host 'Choose 1-6 or Enter.'; continue }
    Invoke-Acceptance ('--acceptance-'+$kind) ($kind+'-result.json')
    Get-Content -LiteralPath (Join-Path $root ($kind+'-result.json'))
}
Write-Host "Evidence retained: $root"
