# Build Guide

## Overview

This guide describes how to build ED Activity Overlay from the unified repository.

## Prerequisites

- Windows 10 or Windows 11
- .NET 8 SDK
- Visual Studio 2022 (optional)
- Inno Setup when building the installer

## Build From Command Line

From the repository root:

```powershell
dotnet build .\EDActivityOverlay\EDActivityOverlay.sln
```

or:

```powershell
.\build.ps1
```

## Build Individual Projects

```powershell
dotnet build .\EDActivityOverlay\EDActivityOverlay.csproj
dotnet build .\Logger\Logger.csproj
dotnet build .\Testing\MockTargetApp\MockTargetApp.csproj
```

## Run Application

```powershell
dotnet run --project .\EDActivityOverlay\EDActivityOverlay.csproj
```

## Run Tests

```powershell
dotnet test .\Testing\EDActivityOverlay.Tests\EDActivityOverlay.Tests.csproj
```

Additional regression scripts and test harnesses are available under `Testing/`.

## Build Installer

```powershell
# Build app + installer
.\build_installer.ps1

# Build app only
.\build_installer.ps1 -SkipInstaller

# Build installer only when Release already exists
.\build_installer.ps1 -SkipBuild
```

Installer artifacts are written to `Installer/`.

## Troubleshooting

- Missing app executable: run a full build or publish.
- Missing mock target executable: build `Testing\MockTargetApp\MockTargetApp.csproj`.
- Overlay not visible during harness testing: verify the configured target process.
- Build file lock errors: close the running overlay, Visual Studio design-time builds, and other `dotnet`/`MSBuild` processes.