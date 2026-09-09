# Build Guide

## Overview

This guide describes how to build ED Activity Overlay from the unified repository.

## Prerequisites

- Windows 10 version 2004 (build 19041+) or Windows 11
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

The default development release version is `1.3.0-beta.1`. Override it for a
release build without editing project files:

```powershell
.\build_installer.ps1 -Version 1.3.0-beta.2
```

The script resolves `ISCC.exe` from `PATH` and the standard Inno Setup 6
installation directories. Use `-InnoCompiler` for a custom location.

## Continuous integration and releases

Every branch push and every pull request into `master` runs restore, a
warning-free Release build, the complete automated test suite and a
self-contained `win-x64` publish smoke test.

Tags matching `v*` run the release workflow. The tag text after `v` becomes
the product and installer version. A version containing a suffix such as
`-beta.1` is published as a GitHub prerelease. The workflow produces:

- `EDActivityOverlay_<version>_portable_win-x64.zip`;
- `EDActivityOverlay_Setup_<version>.exe`;
- `SHA256SUMS.txt`.

Example:

```powershell
git tag v1.3.0-beta.1
git push origin v1.3.0-beta.1
```

## Troubleshooting

- Missing app executable: run a full build or publish.
- Missing mock target executable: build `Testing\MockTargetApp\MockTargetApp.csproj`.
- Overlay not visible during harness testing: verify the configured target process.
- Build file lock errors: close the running overlay, Visual Studio design-time builds, and other `dotnet`/`MSBuild` processes.
