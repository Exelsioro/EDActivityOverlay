Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptRoot
$buildScript = Join-Path $repoRoot 'build.ps1'
$unitTests = Join-Path $scriptRoot 'EDActivityOverlay.Tests\EDActivityOverlay.Tests.csproj'
$quick = Join-Path $scriptRoot 'QuickRegressionTest.ps1'
$manual = Join-Path $scriptRoot 'RegressionTest.ps1'

Write-Host 'ED Activity Overlay - Test Runner' -ForegroundColor Green
Write-Host '==============================' -ForegroundColor Green

Write-Host 'Building current checkout (Release)...' -ForegroundColor Yellow
& $buildScript -Configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Release build failed. Stopping.' -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host 'Running automated test suite...' -ForegroundColor Yellow
dotnet test $unitTests --configuration Release --no-build
$unitExit = $LASTEXITCODE
if ($unitExit -ne 0) {
    Write-Host 'Automated test suite failed. Stopping.' -ForegroundColor Red
    exit $unitExit
}

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

Write-Host 'Automated tests and quick regression passed. Manual regression skipped.' -ForegroundColor Green
exit 0
