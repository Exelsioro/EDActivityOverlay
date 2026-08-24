# Future Changes

## Critical Priority
1. Decompose `InaraTools/InaraParserUtils.LegParsing.cs`.
2. Decompose `EDActivityOverlay/Windows/TradeRouteWindow.xaml.cs`.
3. Decompose `EDActivityOverlay/UserControls/TradeRouteCard.xaml.cs`.
4. Decompose `InaraTools/InaraCommunicator.cs`.

## High Priority
1. Decompose `EDActivityOverlay/Services/ThemeManager.cs`.
2. Decompose `EDActivityOverlay/Utils/UIHelpers.cs`.
3. Decompose `EDActivityOverlay/Utils/WindowsAPI.cs`.

## Medium Priority
1. Continue decomposition of `EDActivityOverlay/Windows/MainWindow.xaml.cs`.
2. Continue decomposition of `EDActivityOverlay/Windows/MainWindow.OverlayOrchestration.cs`.

## Low Priority
1. Split `EDActivityOverlay/Resources/UIStyles.xaml` into thematic resource dictionaries.

## Notes
1. Keep current behavior unchanged during decomposition.
2. After each change block, run `dotnet build` for `InaraTools` and `EDActivityOverlay`.
