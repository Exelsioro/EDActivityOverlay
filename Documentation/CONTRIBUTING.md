# Contributing to ED Activity Overlay

Thank you for contributing.

## Repository Structure

| Project | Purpose | Tech |
|---|---|---|
| `EDActivityOverlay/` | Main overlay application and activity services | C# / .NET 8 / WPF |
| `Logger/` | Shared logging | C# / .NET 8 |
| `Testing/` | Automated tests, harnesses and regression scripts | C# / PowerShell |
| `Testing/MockTargetApp/` | Mock target process for overlay tests | C# / .NET 8 |
| `Documentation/` | Architecture and feature documentation | Markdown |

## Getting Started

```powershell
git clone <your-fork-url>
cd <repository-folder>
dotnet build .\EDActivityOverlay\EDActivityOverlay.sln
```

Run the application:

```powershell
dotnet run --project .\EDActivityOverlay\EDActivityOverlay.csproj
```

## Development Workflow

1. Create a focused feature branch.
2. Make scoped changes.
3. Build the solution.
4. Run the automated test project.
5. Use the relevant harness/regression scripts for UI or integration changes.
6. Open a pull request with a summary and test notes.

## Code Guidelines

- Keep changes small and scoped.
- Prefer provider-neutral domain/service boundaries for external data sources.
- Keep Frontier Journal/JSON ingestion separate from activity-specific presentation logic.
- Reuse shared application services and `Logger` instead of duplicating infrastructure.
- Keep UI behavior in the WPF application layer.
- Add or update documentation when architecture or behavior changes.

## Testing

```powershell
dotnet test .\Testing\EDActivityOverlay.Tests\EDActivityOverlay.Tests.csproj
```

Mock target app:

```powershell
dotnet run --project .\Testing\MockTargetApp\MockTargetApp.csproj
```

## Reporting Issues

Include:

- steps to reproduce;
- expected behavior;
- actual behavior;
- Windows/.NET version;
- relevant runtime logs.

## License

By contributing, you agree your changes are licensed under MIT.