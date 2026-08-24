# Future Changes

## Critical Priority

1. Keep Frontier Journal and companion JSON ingestion behind unified state services.
2. Keep trade-route discovery behind a provider-neutral market-data boundary.
3. Continue decomposition of large activity windows and orchestration classes.
4. Preserve existing activity behavior while infrastructure is refactored.

## High Priority

1. Integrate the unified `JournalMonitorService` / canonical game-state flow across activities.
2. Complete provider-neutral trade search after a suitable market-data query strategy is available.
3. Improve trade ranking with route geometry, ship context, round trips and additional ranking modes.
4. Continue decomposition of `ThemeManager`, `UIHelpers`, and Windows integration helpers.

## Medium Priority

1. Continue decomposition of `MainWindow` activity/orchestration code.
2. Consolidate Frontier JSON readers into a unified monitored snapshot service.
3. Continue Engineering and Exploration state separation from WPF presentation.

## Low Priority

1. Split large style/resource dictionaries into thematic resource dictionaries.
2. Continue documentation cleanup as old architecture notes become obsolete.

## Validation

After each change block:

```powershell
dotnet build .\EDActivityOverlay\EDActivityOverlay.sln
dotnet test .\Testing\EDActivityOverlay.Tests\EDActivityOverlay.Tests.csproj
```