Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptRoot
$appExe = Join-Path $repoRoot 'EDActivityOverlay\bin\Debug\net8.0-windows\EDActivityOverlay.exe'
$solution = Join-Path $repoRoot 'EDActivityOverlay\EDActivityOverlay.sln'

function Stop-TestProcesses {
    foreach ($name in @('EDActivityOverlay','MockTargetApp','notepad')) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

function Ensure-AppBuilt {
    if (Test-Path $appExe) { return }
    Write-Host 'Application binary not found. Building solution...' -ForegroundColor Yellow
    dotnet build $solution --configuration Debug | Out-Host
    if (-not (Test-Path $appExe)) {
        throw "Build finished but executable not found: $appExe"
    }
}

Write-Host 'ED Inara Overlay - Quick Regression Test' -ForegroundColor Green
Write-Host '=========================================' -ForegroundColor Green

$passed = 0
$total = 0

Stop-TestProcesses
Ensure-AppBuilt

$total++
if (Test-Path $appExe) {
    Write-Host '[PASS] Executable exists' -ForegroundColor Green
    $passed++
} else {
    Write-Host '[FAIL] Executable missing' -ForegroundColor Red
}

$total++
$overlay = Start-Process -FilePath $appExe -ArgumentList 'notepad' -PassThru
Start-Sleep -Seconds 3
if ($overlay.HasExited) {
    Write-Host '[FAIL] Application exited immediately' -ForegroundColor Red
} else {
    Write-Host "[PASS] Application started (PID: $($overlay.Id))" -ForegroundColor Green
    $passed++
}

$total++
$proc = Get-Process -Name 'EDActivityOverlay' -ErrorAction SilentlyContinue
if ($proc) {
    $memMb = [math]::Round($proc.WorkingSet64 / 1MB, 2)
    Write-Host "Memory: $memMb MB" -ForegroundColor White
    Write-Host '[PASS] Process is running' -ForegroundColor Green
    $passed++
} else {
    Write-Host '[FAIL] Process not found after startup' -ForegroundColor Red
}

$total++
$stable = $true
for ($i = 0; $i -lt 10; $i++) {
    if (-not (Get-Process -Name 'EDActivityOverlay' -ErrorAction SilentlyContinue)) {
        $stable = $false
        break
    }
    Start-Sleep -Seconds 1
}
if ($stable) {
    Write-Host '[PASS] Process stayed alive for 10 seconds' -ForegroundColor Green
    $passed++
} else {
    Write-Host '[FAIL] Process became unstable within 10 seconds' -ForegroundColor Red
}

$total++
$notepad = Start-Process -FilePath 'notepad.exe' -PassThru
Start-Sleep -Seconds 2
if (Get-Process -Name 'EDActivityOverlay' -ErrorAction SilentlyContinue) {
    Write-Host '[PASS] Overlay process survived target creation' -ForegroundColor Green
    $passed++
} else {
    Write-Host '[FAIL] Overlay process crashed after target creation' -ForegroundColor Red
}

if ($notepad -and -not $notepad.HasExited) {
    $notepad.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 1
}

$overlay = Get-Process -Name 'EDActivityOverlay' -ErrorAction SilentlyContinue
if ($overlay) {
    $overlay.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 3
}
Stop-TestProcesses

Write-Host ''
Write-Host "Result: $passed / $total tests passed" -ForegroundColor White
if ($passed -eq $total) {
    Write-Host '[PASS] Quick regression passed' -ForegroundColor Green
    exit 0
}

Write-Host '[FAIL] Quick regression failed' -ForegroundColor Red
exit 1
