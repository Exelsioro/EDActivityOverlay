# Contributing to Elite Dangerous Inara Overlay

Thank you for contributing.

## Repository Structure

This is a unified repository with these main components:

| Project | Purpose | Tech |
|---|---|---|
| `EDActivityOverlay/` | Main overlay application | C# / .NET 8 / WPF |
| `InaraTools/` | INARA communication and parsing | C# / .NET 8 |
| `Logger/` | Shared logging | C# / .NET 8 |
| `Testing/` | Test scripts and harness projects | PowerShell / C# |
| `Testing/MockTargetApp/` | Mock target process for overlay tests | C# / .NET 8 |

## Getting Started

```bash
git clone <your-fork-url>
cd EDActivityOverlay
dotnet build EDActivityOverlay/EDActivityOverlay.sln
```

Run app:

```bash
dotnet run --project EDActivityOverlay/EDActivityOverlay.csproj
```

## Development Workflow

1. Create branch: `feature/<name>`.
2. Make focused changes.
3. Build solution.
4. Run `Testing/QuickRegressionTest.ps1`.
5. If needed, run `Testing/RegressionTest.ps1` for manual verification.
6. Open PR with summary and test notes.

## Code Guidelines

- Keep changes small and scoped.
- Use clear naming and avoid dead code.
- Prefer shared logic in `InaraTools` and `Logger` over duplication.
- Keep UI behavior changes inside `EDActivityOverlay`.
- Add or update docs when behavior changes.

## Testing

Quick smoke:

```powershell
powershell -ExecutionPolicy Bypass -File Testing/QuickRegressionTest.ps1
```

Manual regression:

```powershell
powershell -ExecutionPolicy Bypass -File Testing/RegressionTest.ps1
```

Mock target app:

```powershell
dotnet run --project Testing/MockTargetApp/MockTargetApp.csproj
```

## Reporting Issues

Include:

- Steps to reproduce
- Expected behavior
- Actual behavior
- Environment (Windows version, .NET version)
- Relevant logs from `EDActivityOverlay/bin/<Configuration>/net8.0-windows/logs/`

## License

By contributing, you agree your changes are licensed under MIT.
