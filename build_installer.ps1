# Build & Publish script for Elite Dangerous Inara Overlay
# Requires: dotnet SDK + Inno Setup (ISCC in PATH)

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipBuild = $false,
    [switch]$SkipInstaller = $false
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $ScriptDir

Write-Host "Elite Dangerous Inara Overlay - Build & Installer Script" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green

# -------- Validate Inno Setup --------
if (-not $SkipInstaller) {
    if (-not (Get-Command ISCC -ErrorAction SilentlyContinue)) {
        Write-Host "ERROR: ISCC (Inno Setup) not found in PATH." -ForegroundColor Red
        Write-Host "Add Inno Setup folder to PATH and restart terminal." -ForegroundColor Yellow
        exit 1
    }
}

# -------- Publish Application --------
if (-not $SkipBuild) {

    Write-Host "`nPublishing application..." -ForegroundColor Cyan

    # Clean previous publish
    if (Test-Path ".\Release") {
        Remove-Item ".\Release" -Recurse -Force
    }

    dotnet publish `
        ".\EDActivityOverlay\EDActivityOverlay.csproj" `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o ".\Release"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed!" -ForegroundColor Red
        exit 1
    }

    if (-not (Test-Path ".\Release\EDActivityOverlay.exe")) {
        Write-Host "Executable not found in Release folder." -ForegroundColor Red
        exit 1
    }

    Write-Host "Publish completed successfully!" -ForegroundColor Green
}
else {
    Write-Host "Skipping publish step..." -ForegroundColor Yellow
}

# -------- Create Installer --------
if (-not $SkipInstaller) {

    Write-Host "`nCreating installer..." -ForegroundColor Cyan

    if (-not (Test-Path ".\installer.iss")) {
        Write-Host "installer.iss not found in project root." -ForegroundColor Red
        exit 1
    }

    ISCC ".\installer.iss"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Installer creation failed!" -ForegroundColor Red
        exit 1
    }

    Write-Host "Installer created successfully!" -ForegroundColor Green

    if (Test-Path ".\Installer") {
        Write-Host "`nCreated installer files:" -ForegroundColor Cyan
        Get-ChildItem ".\Installer\*.exe" | ForEach-Object {
            $size = [math]::Round($_.Length / 1MB, 2)
            Write-Host "  $($_.Name) ($size MB)"
        }
    }
}
else {
    Write-Host "Skipping installer step..." -ForegroundColor Yellow
}

Write-Host "`nBuild process completed successfully." -ForegroundColor Green