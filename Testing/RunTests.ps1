Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$quick = Join-Path $scriptRoot 'QuickRegressionTest.ps1'
$manual = Join-Path $scriptRoot 'RegressionTest.ps1'

Write-Host 'ED Activity Overlay - Test Runner' -ForegroundColor Green
Write-Host '==============================' -ForegroundColor Green

& $quick
$quickExit = $LASTEXITCODE
if ($quickExit -ne 0) {
    Write-Host 'Quick regression failed. Stopping.' -ForegroundColor Red
    exit $quickExit
}

$runManual = Read-Host 'Run manual regression now? (y/n)'
if ($runManual -match '^(?i:y)$') {
    & $manual
    exit $LASTEXITCODE
}

Write-Host 'Quick regression passed. Manual regression skipped.' -ForegroundColor Green
exit 0
