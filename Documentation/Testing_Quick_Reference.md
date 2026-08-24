# Testing Quick Reference

## Build

```powershell
dotnet build EDActivityOverlay/EDActivityOverlay.sln
```

## Run Tests

```powershell
.\Testing\QuickRegressionTest.ps1
.\Testing\RegressionTest.ps1
.\Testing\RunTests.ps1
```

## Process Checks

```powershell
Get-Process EDActivityOverlay -ErrorAction SilentlyContinue |
  Select-Object ProcessName, CPU, @{n='MemoryMB';e={[math]::Round($_.WorkingSet64 / 1MB, 2)}}
```

## Force Cleanup

```powershell
Get-Process EDActivityOverlay -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process MockTargetApp -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force
```

## Logs

- `EDActivityOverlay/bin/<Configuration>/net8.0-windows/logs/`
