using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeUnifiedWorkspaceTests
{
    [Fact]
    public void TradeUsesSharedActivityWorkspaceOnly()
    {
        string navigation =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "MainWindow.ActivityNavigation.cs");

        Assert.Contains(
            "EnsureJournalWorkspaceVisible(ActivityType.Trade)",
            navigation,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "new TradeRouteWindow",
            navigation,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "tradeRouteWindow?.Hide();",
            navigation,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "resultsOverlayWindow?.Hide();",
            navigation,
            StringComparison.Ordinal);

        string orchestration =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "MainWindow.OverlayOrchestration.cs");

        Assert.Contains(
            "ToggleActivityFromHotkey(",
            orchestration,
            StringComparison.Ordinal);

        Assert.Contains(
            "ActivityType.Trade",
            orchestration,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Creating new TradeRouteWindow instance",
            orchestration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TradeHasCompactAndFullModesInSameControl()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        Assert.Contains(
            "x:Name=\"CompactTradePanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"FullTradePanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "CompactActionButton",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "CompactSecondaryButton_Click",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "CollapseButton_Click",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FullTradeUsesExclusiveInteractionButCompactDoesNot()
    {
        string workspace =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.Trade.cs");

        Assert.Contains(
            "BeginExclusiveOverlayInteraction",
            workspace,
            StringComparison.Ordinal);

        Assert.Contains(
            "EndTradeExclusiveInteraction",
            workspace,
            StringComparison.Ordinal);

        Assert.Contains(
            "IsTradeFullWorkspace",
            workspace,
            StringComparison.Ordinal);

        string shared =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs");

        Assert.Contains(
            "|| IsTradeFullWorkspace",
            shared,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "|| activity == ActivityType.Trade",
            shared,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AdvancedFiltersUseThemeAwareCustomPanelNotDefaultExpander()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        Assert.DoesNotContain(
            "<Expander",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"AdvancedFiltersButton\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Foreground=\"{DynamicResource AccentColorBrush}\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"AdvancedFiltersPanel\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnifiedTradeWorkspaceUsesTradeLegDistanceOnly()
    {
        string control =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml.cs");

        Assert.Contains(
            "SourceToTargetDistanceLy",
            control,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "TotalTravelDistanceLy",
            control,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShipStatusIsNotSuppressedByTradeSurface()
    {
        string main =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "MainWindow.xaml.cs");

        Assert.Contains(
            "shipStatusOverlayWindow.SetContextSuppression(null);",
            main,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TradeWorkspacePreservesSelectedCandidateAcrossProgressiveRefresh()
    {
        string control =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml.cs");

        Assert.Contains(
            "heldSelection",
            control,
            StringComparison.Ordinal);

        Assert.Contains(
            "selectedKey",
            control,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowDoesNotAutoRestoreLegacyTradeWindows()
    {
        string main =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "MainWindow.xaml.cs");

        Assert.DoesNotContain(
            "&& isToggleActive\n                    && tradeRouteWindow != null",
            Normalize(main),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "&& isResultsActive\n                    && resultsOverlayWindow != null",
            Normalize(main),
            StringComparison.Ordinal);
    }

    private static string Normalize(
        string value) =>
        value.Replace(
            "\r\n",
            "\n",
            StringComparison.Ordinal);

    private static string ReadProjectFile(
        params string[] relative)
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
        {
            string candidate =
                Path.Combine(
                    [
                        directory.FullName,
                        .. relative
                    ]);

            if (File.Exists(
                    candidate))
            {
                return
                    File.ReadAllText(
                        candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
