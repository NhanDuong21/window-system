param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/acceptance-report.ps1"
if(-not(Test-Path -LiteralPath (Join-Path $Root '.nyan-fixture'))) { throw 'Owned test marker required.' }
$checks=New-Object Collections.Generic.List[string]
function Check([string]$Name,[bool]$Condition) { if(-not $Condition){throw ('FAIL '+$Name)};$checks.Add('PASS '+$Name) }
function Json([string]$Leaf,$Value) { $Value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Root $Leaf) -Encoding utf8 }
try {
    $s=Write-NyanAcceptanceSummary $Root 'EXPLORER'
    Check 'summary-before-work-retains-not-run' ($s.nativeRead -eq 'NOT_RUN' -and $s.performance.result -eq 'NOT_RUN' -and $s.uiReopen -eq 'WAITING_FOR_USER')
    Set-Content -LiteralPath (Join-Path $Root 'result.txt') -Value 'PASS'
    Json 'persistence.json' @{result='PASS'};Json 'read-context.json' @{elevated=$false}
    $metrics=@{firstPaintMs=757;idleCpuPercent=2.2256973;workingSetMiB=187.84;budgetFirstPaintMs=2500;budgetIdleCpuPercent=1;budgetWorkingSetMiB=350}
    Json 'release-performance.json' $metrics
    $s=Write-NyanAcceptanceSummary $Root 'EXPLORER'
    Check 'native-pass-cpu-fail-recorded-before-human-ui' ($s.nativeRead -eq 'PASS' -and $s.performance.cpu -eq 'FAIL' -and $s.performance.firstFrame -eq 'PASS' -and $s.automaticResult -eq 'FAIL')
    Check 'early-summary-written-on-disk' ((Read-NyanEvidenceJson (Join-Path $Root 'acceptance-summary.json')).performance.cpu -eq 'FAIL')
    Json 'session-open-a.json' @{at='2026-01-01T00:00:00Z';dark=$false};Json 'session-closed.json' @{dark=$true}
    $s=Write-NyanAcceptanceSummary $Root 'EXPLORER'
    Check 'one-close-is-not-reopen-pass' ($s.uiReopen -eq 'WAITING_FOR_USER')
    Json 'session-first-close.json' @{dark=$true};Json 'session-open-b.json' @{at='2026-01-01T00:01:00Z';dark=$false}
    $s=Write-NyanAcceptanceSummary $Root 'EXPLORER' 'OK'
    Check 'theme-mismatch-fails-even-human-ok' ($s.uiReopen -eq 'FAIL' -and $s.humanUi -eq 'PASS' -and $s.automaticResult -eq 'FAIL')
    Json 'session-open-b.json' @{at='2026-01-01T00:01:00Z';dark=$true}
    $s=Write-NyanAcceptanceSummary $Root 'EXPLORER' 'OK'
    Check 'human-and-reopen-pass-do-not-hide-performance-fail' ($s.uiReopen -eq 'PASS' -and $s.performance.result -eq 'FAIL' -and $s.automaticResult -eq 'FAIL')
    $metrics.idleCpuPercent=0.013;$metrics.workingSetMiB=350;$metrics.firstPaintMs=2500;Json 'release-performance.json' $metrics
    $s=Write-NyanAcceptanceSummary $Root 'EXPLORER' 'OK'
    Check 'exact-budget-boundary-pass-remains-partial-native-mutations' ($s.performance.result -eq 'PASS' -and $s.automaticResult -eq 'PARTIAL' -and $s.productionMutations -eq 'NOT_RUN')
    $metrics.workingSetMiB=350.001;Json 'release-performance.json' $metrics
    Check 'memory-budget-failure' ((Get-NyanPerformanceResult (Join-Path $Root 'release-performance.json')).memory -eq 'FAIL')
    $metrics.workingSetMiB=100;$metrics.firstPaintMs=2501;Json 'release-performance.json' $metrics
    Check 'first-frame-budget-failure' ((Get-NyanPerformanceResult (Join-Path $Root 'release-performance.json')).firstFrame -eq 'FAIL')
    $metrics.firstPaintMs=1;$metrics.budgetIdleCpuPercent=3;Json 'release-performance.json' $metrics
    Check 'budget-increase-rejected' ((Get-NyanPerformanceResult (Join-Path $Root 'release-performance.json')).result -eq 'FAIL')
    $metrics.budgetIdleCpuPercent=1;$metrics.idleCpuPercent=-1;Json 'release-performance.json' $metrics
    Check 'negative-metric-rejected' ((Get-NyanPerformanceResult (Join-Path $Root 'release-performance.json')).result -eq 'FAIL')
    Json 'release-performance.json' @{idleCpuPercent=0}
    Check 'missing-metrics-rejected' ((Get-NyanPerformanceResult (Join-Path $Root 'release-performance.json')).result -eq 'FAIL')
    Set-Content -LiteralPath (Join-Path $Root 'release-performance.json') -Value '{broken'
    Check 'corrupt-metrics-rejected' ((Get-NyanPerformanceResult (Join-Path $Root 'release-performance.json')).result -eq 'FAIL')
    $checks | Set-Content -LiteralPath (Join-Path $Root 'report-checks.txt') -Encoding utf8
    Write-Host ('Acceptance report: '+$checks.Count+' PASS (Windows PowerShell '+$PSVersionTable.PSVersion+')')
} catch { $checks.Add($_.Exception.Message);$checks | Set-Content -LiteralPath (Join-Path $Root 'report-checks.txt') -Encoding utf8;throw }
