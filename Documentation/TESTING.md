# Testing

## Automated test project

The primary automated suite is:

```text
Testing/EDActivityOverlay.Tests/EDActivityOverlay.Tests.csproj
```

Run it from the repository root:

```powershell
dotnet test .\Testing\EDActivityOverlay.Tests\EDActivityOverlay.Tests.csproj
```

The suite covers application services and domain behavior including Journal state reduction, Engineering, Exploration, route/progress logic, localization, storage, layout helpers, X52 integration and related regressions.

Do not use `--no-build` as the only validation after project, namespace or assembly-name changes. A normal `dotnet test` rebuild is required to catch compile-time integration errors such as `InternalsVisibleTo` mismatches.

## Solution build

```powershell
dotnet build .\EDActivityOverlay\EDActivityOverlay.sln
```

A documentation/refactor/rebrand change is not considered complete until both the solution build and automated tests pass.

## Overlay harnesses

`Testing/` also contains WPF harness projects for manual overlay/window behavior:

- `OverlayTestHarness.csproj`
- `MinimalTestHarness.csproj`
- `MockTargetApp/`

These are useful for focus, window attachment, visibility, interaction and layout checks without requiring a normal Elite Dangerous session.

## Regression scripts

The repository currently retains:

```powershell
powershell -ExecutionPolicy Bypass -File .\Testing\QuickRegressionTest.ps1
powershell -ExecutionPolicy Bypass -File .\Testing\RegressionTest.ps1
powershell -ExecutionPolicy Bypass -File .\Testing\RunTests.ps1
```

Treat the automated .NET test project as the baseline regression gate. The PowerShell scripts/harnesses supplement it for process/window/UI scenarios.

## Manual checks for overlay changes

For changes affecting WPF overlay orchestration or input, verify at minimum:

1. waiting/startup window behavior;
2. target-process detection;
3. focus-dependent overlay hide/show;
4. interactive/passive mode transitions;
5. activity switching;
6. pinned secondary overlay behavior;
7. relevant hotkeys;
8. affected X52 controls when hardware integration changed.

## Build artifacts and locks

Typical runtime output:

```text
EDActivityOverlay/bin/<Configuration>/net8.0-windows/
```

If a directory rename or build fails with a file/directory lock, close:

- Visual Studio / Rider;
- the running overlay;
- `dotnet`;
- `MSBuild`;

and retry only after the process handles are released.