# Testing Guide

## Scope

This guide covers the active automated and manual tests for the current repository layout.

## Prerequisites

- Windows 10/11
- .NET 8 SDK
- PowerShell 5.1+

## Build

```bash
dotnet build EDActivityOverlay/EDActivityOverlay.sln
```

Expected output binaries:

- `EDActivityOverlay/bin/Debug/net8.0-windows/EDActivityOverlay.exe`
- `Testing/MockTargetApp/bin/Debug/net8.0-windows/MockTargetApp.exe`

## Scripted Checks

From repository root:

```powershell
powershell -ExecutionPolicy Bypass -File Testing\QuickRegressionTest.ps1
powershell -ExecutionPolicy Bypass -File Testing\RegressionTest.ps1
powershell -ExecutionPolicy Bypass -File Testing\RunTests.ps1
```

`RunTests.ps1` executes quick checks first and then asks whether to run manual checks.

## Unit Tests (Layout Math)

The project includes focused unit tests for adaptive scaling and positioning math:

```bash
dotnet test Testing/EDActivityOverlay.LayoutTests/EDActivityOverlay.LayoutTests.csproj -p:UseAppHost=false
```

Covered areas:
- Adaptive scale computation and clamping
- Relative anchor positioning
- Work-area clamping on X/Y axes

## Manual Verification Checklist

1. Waiting window appears when target is absent.
2. Waiting window disappears after target start.
3. Overlay hide/show follows target focus.
4. Overlay tracks target position.
5. `Ctrl+5` toggles trade route window.

## Troubleshooting

- App binary missing: run `dotnet build`.
- Mock target missing: run `dotnet build Testing/MockTargetApp/MockTargetApp.csproj`.
- Overlay not visible: verify target process name (`EliteDangerous64` or `MockTargetApp`).
