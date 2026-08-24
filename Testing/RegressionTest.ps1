Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptRoot
$appExe = Join-Path $repoRoot 'EDActivityOverlay\bin\Debug\net8.0-windows\EDActivityOverlay.exe'
$mockExe = Join-Path $repoRoot 'Testing\MockTargetApp\bin\Debug\net8.0-windows\MockTargetApp.exe'
$solution = Join-Path $repoRoot 'EDActivityOverlay\EDActivityOverlay.sln'
$mockProj = Join-Path $repoRoot 'Testing\MockTargetApp\MockTargetApp.csproj'

function Stop-TestProcesses {
    foreach ($name in @('EDActivityOverlay','MockTargetApp','notepad')) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

function Ensure-Binaries {
    if (-not (Test-Path $appExe)) {
        Write-Host 'Building solution...' -ForegroundColor Yellow
        dotnet build $solution --configuration Debug | Out-Host
    }
    if (-not (Test-Path $mockExe)) {
        Write-Host 'Building MockTargetApp...' -ForegroundColor Yellow
        dotnet build $mockProj --configuration Debug | Out-Host
    }
    if (-not (Test-Path $appExe)) { throw "Missing app executable: $appExe" }
    if (-not (Test-Path $mockExe)) { throw "Missing mock executable: $mockExe" }
}

function Ask-PassFail([string]$question) {
    while ($true) {
        $r = Read-Host "$question (y/n)"
        if ($r -match '^(?i:y|n)$') { return ($r -match '^(?i:y)$') }
    }
}

$results = @()
function Add-Result([string]$name, [bool]$ok, [string]$details='') {
    $results += [pscustomobject]@{ Test = $name; Status = if ($ok) { 'PASS' } else { 'FAIL' }; Details = $details }
}

Write-Host 'ED Inara Overlay - Manual Regression Test' -ForegroundColor Green
Write-Host '==========================================' -ForegroundColor Green

Stop-TestProcesses
Ensure-Binaries

$overlay = Start-Process -FilePath $appExe -ArgumentList 'MockTargetApp' -PassThru
Start-Sleep -Seconds 2
Add-Result 'Startup' (-not $overlay.HasExited) 'Overlay starts and stays running'

$mock = Start-Process -FilePath $mockExe -PassThru
Start-Sleep -Seconds 3

Write-Host ''
Write-Host 'Manual checks:' -ForegroundColor Yellow
Write-Host '1) Waiting window disappears after mock target starts.' -ForegroundColor Yellow
Write-Host '2) Overlay appears near mock target window.' -ForegroundColor Yellow
Write-Host '3) Focus notepad -> overlay hides.' -ForegroundColor Yellow
Write-Host '4) Focus mock target -> overlay shows again.' -ForegroundColor Yellow
Start-Process notepad.exe | Out-Null

$focusOk = Ask-PassFail 'Did focus-dependent visibility work as expected?'
Add-Result 'Focus behavior' $focusOk 'Overlay hide/show follows target focus'

Write-Host ''
Write-Host '5) Move mock target window. Overlay should follow position.' -ForegroundColor Yellow
$positionOk = Ask-PassFail 'Did overlay tracking/positioning work as expected?'
Add-Result 'Window tracking' $positionOk 'Overlay follows target move/restore'

Write-Host ''
Write-Host '6) Press Ctrl+5 with target focused. Trade route window should toggle.' -ForegroundColor Yellow
$hotkeyOk = Ask-PassFail 'Did global hotkey toggle work?'
Add-Result 'Global hotkey' $hotkeyOk 'Ctrl+5 toggles route window'

$proc = Get-Process -Name 'EDActivityOverlay' -ErrorAction SilentlyContinue
$memOk = $false
if ($proc) {
    $mem = [math]::Round($proc.WorkingSet64 / 1MB, 2)
    $memOk = ($mem -lt 150)
    Add-Result 'Memory check' $memOk "WorkingSet=$mem MB"
} else {
    Add-Result 'Memory check' $false 'Process not found'
}

Stop-TestProcesses

$pass = ($results | Where-Object Status -eq 'PASS').Count
$fail = ($results | Where-Object Status -eq 'FAIL').Count

Write-Host ''
Write-Host 'Results:' -ForegroundColor White
$results | ForEach-Object {
    $color = if ($_.Status -eq 'PASS') { 'Green' } else { 'Red' }
    Write-Host "[$($_.Status)] $($_.Test): $($_.Details)" -ForegroundColor $color
}
Write-Host "Summary: PASS=$pass FAIL=$fail" -ForegroundColor White

if ($fail -gt 0) { exit 1 }
exit 0
