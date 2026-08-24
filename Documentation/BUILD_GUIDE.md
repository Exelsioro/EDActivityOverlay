# Build Guide

## Overview

This guide describes how to build the project from the unified repository.

## Prerequisites

- Windows 10 or Windows 11
- .NET 8 SDK
- Visual Studio 2022 (optional, for IDE workflow)

## Build From Command Line

From repository root:

```powershell
dotnet build EDActivityOverlay/EDActivityOverlay.sln
```

## Build Individual Projects

```powershell
dotnet build EDActivityOverlay/EDActivityOverlay.csproj
dotnet build InaraTools/InaraTools.csproj
dotnet build Logger/Logger.csproj
dotnet build Testing/MockTargetApp/MockTargetApp.csproj
```

## Run Application

```powershell
dotnet run --project EDActivityOverlay/EDActivityOverlay.csproj
```

## Build Installer

```powershell
# Build app + installer
./build_installer.ps1

# Build app only
./build_installer.ps1 -SkipInstaller

# Build installer only
./build_installer.ps1 -SkipBuild
```

Installer artifacts are written to `dist/`.

## Validate Build

Run quick regression:

```powershell
powershell -ExecutionPolicy Bypass -File Testing/QuickRegressionTest.ps1
```

Optional full manual regression:

```powershell
powershell -ExecutionPolicy Bypass -File Testing/RegressionTest.ps1
```

Or run orchestrator:

```powershell
powershell -ExecutionPolicy Bypass -File Testing/RunTests.ps1
```

## Troubleshooting

- Missing app executable: run full solution build.
- Missing mock target executable: build `Testing/MockTargetApp/MockTargetApp.csproj`.
- Overlay not visible during tests: verify target process name (`MockTargetApp` or `EliteDangerous64`).
- Build file lock errors: close running `EDActivityOverlay.exe` and rebuild.
