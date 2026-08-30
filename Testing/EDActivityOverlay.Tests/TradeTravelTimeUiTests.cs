using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeTravelTimeUiTests
{
    [Fact]
    public void TradeResultsExposeTimeAndProfitPerHourRanking()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        Assert.Contains(
            "Loc_TRADE_SORT_PER_HOUR",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Tag=\"perhour\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_TRADE_SORT_FASTEST",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Tag=\"time\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Text=\"{Binding TravelTime}\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Text=\"{Binding CreditsPerHour}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DetailPanelShowsExplicitTravelEstimate()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        Assert.Contains(
            "SelectedTravelEstimateText",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_TRADE_TRAVEL_ESTIMATE",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RankingDoesNotPretendUnavailableShipProfileIsFast()
    {
        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.TravelTime.cs");

        Assert.Contains(
            "double.MaxValue",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "TradeTravelEstimateConfidence.Unavailable",
            code,
            StringComparison.Ordinal);
    }

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
