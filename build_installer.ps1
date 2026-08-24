#requires -Version 5.1
# Build & Publish script for ED Activity Overlay
# Requires: dotnet SDK + Inno Setup (ISCC in PATH)

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipBuild = $false,
    [switch]$SkipInstaller = $false
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $RepoRoot "EDActivityOverlay\EDActivityOverlay.csproj"
$ReleaseDir = Join-Path $RepoRoot "Release"
$InstallerScript = Join-Path $RepoRoot "installer.iss"

Write-Host "ED Activity Overlay - Build & Installer" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green

if (-not (Test-Path -LiteralPath $Project)) {
    throw "Project not found: $Project"
}

if (-not $SkipInstaller) {
    if (-not (Get-Command ISCC -ErrorAction SilentlyContinue)) {
        Write-Host "ERROR: ISCC (Inno Setup) not found in PATH." -ForegroundColor Red
        exit 1
    }

    if (-not (Test-Path -LiteralPath $InstallerScript)) {
        Write-Host "ERROR: installer.iss not found in repository root." -ForegroundColor Red
        exit 1
    }
}

if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Publishing application..." -ForegroundColor Cyan

    if (Test-Path -LiteralPath $ReleaseDir) {
        Remove-Item -LiteralPath $ReleaseDir -Recurse -Force
    }

    dotnet publish `
        $Project `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $ReleaseDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed." -ForegroundColor Red
        exit $LASTEXITCODE
    }

    $exe = Join-Path $ReleaseDir "EDActivityOverlay.exe"

    if (-not (Test-Path -LiteralPath $exe)) {
        Write-Host "Executable not found: $exe" -ForegroundColor Red
        exit 1
    }

    Write-Host "Publish completed successfully." -ForegroundColor Green
}
else {
    Write-Host "Skipping publish step." -ForegroundColor Yellow
}

if (-not $SkipInstaller) {
    Write-Host ""
    Write-Host "Creating installer..." -ForegroundColor Cyan

    & ISCC $InstallerScript

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Installer creation failed." -ForegroundColor Red
        exit $LASTEXITCODE
    }

    Write-Host "Installer created successfully." -ForegroundColor Green
}
else {
    Write-Host "Skipping installer step." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Build process completed successfully." -ForegroundColor Green