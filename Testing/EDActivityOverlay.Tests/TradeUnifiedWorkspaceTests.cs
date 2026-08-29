using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeUnifiedWorkspaceTests
{
    [Fact]
    public void TradeUsesActivityWorkspaceInsteadOfLegacySearchAndResultsWindows()
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

        string workspace =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.Trade.cs");

        Assert.Contains(
            "TradeWorkspaceControl",
            workspace,
            StringComparison.Ordinal);

        Assert.Contains(
            "TradeRoutePresentationAdapter.ToPresentation",
            workspace,
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

    private static string ReadProjectFile(
        params string[] relative)
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate =
                Path.Combine(
                    [
                        directory.FullName,
                        .. relative
                    ]);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
