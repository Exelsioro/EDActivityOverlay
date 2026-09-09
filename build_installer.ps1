#requires -Version 5.1
# Build & Publish script for ED Activity Overlay
# Requires: dotnet SDK + Inno Setup (ISCC in PATH)

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.3.0-beta.2",
    [string]$InnoCompiler = "",
    [switch]$SkipBuild = $false,
    [switch]$SkipInstaller = $false
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $RepoRoot "EDActivityOverlay\EDActivityOverlay.csproj"
$ReleaseDir = Join-Path $RepoRoot "Release"
$InstallerScript = Join-Path $RepoRoot "installer.iss"
$VersionCore = ($Version -split '[-+]')[0]
$ParsedVersion = $null

if (-not [System.Version]::TryParse($VersionCore, [ref]$ParsedVersion) -or
    $ParsedVersion.Build -lt 0) {
    throw "Version must start with major.minor.patch: $Version"
}

$FileVersion = "{0}.{1}.{2}.0" -f `
    $ParsedVersion.Major, `
    $ParsedVersion.Minor, `
    $ParsedVersion.Build

Write-Host "ED Activity Overlay - Build & Installer" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green

if (-not (Test-Path -LiteralPath $Project)) {
    throw "Project not found: $Project"
}

if (-not $SkipInstaller) {
    if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
        $command = Get-Command ISCC -ErrorAction SilentlyContinue
        if ($command) {
            $InnoCompiler = $command.Source
        }
        else {
            $candidates = @(
                "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
                "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
            )

            $InnoCompiler = $candidates |
                Where-Object { Test-Path -LiteralPath $_ } |
                Select-Object -First 1
        }
    }

    if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or
        -not (Test-Path -LiteralPath $InnoCompiler)) {
        Write-Host "ERROR: Inno Setup compiler was not found. Install Inno Setup 6 or pass -InnoCompiler." -ForegroundColor Red
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
        -p:Version=$Version `
        -p:AssemblyVersion=$FileVersion `
        -p:FileVersion=$FileVersion `
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

    & $InnoCompiler `
        "/DMyAppVersion=$Version" `
        "/DMyAppFileVersion=$FileVersion" `
        $InstallerScript

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
