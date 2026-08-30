using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeV2WiringTests
{
    [Fact]
    public void TradeWindowUsesProgressiveArdentSearchAndTwoRadii()
    {
        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "TradeRouteWindow.ArdentSearch.cs");

        Assert.Contains(
            "ArdentSearchButton_Click",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "SearchProgressAsync",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "TargetRouteDistanceComboBox",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "SourceSearchRadiusLy",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "TargetSearchRadiusLy",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "IncludeFleetCarriers",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ShowProgressiveTradeResults",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsOverlaySupportsProgressiveSortingWithoutReplacingCards()
    {
        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "ResultsOverlayWindow.ProgressiveTrade.cs");

        Assert.Contains(
            "DisplayProgressiveTradeRoutes",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_TRADE_SORT_PROFIT",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_TRADE_SORT_PER_TON",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_TRADE_SORT_DISTANCE",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "DisplayTradeRoutes(",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ArdentOriginMarketUsesSingleSystemCommoditiesEndpoint()
    {
        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "Services",
                "Ardent",
                "ArdentApiClient.SystemCommodities.cs");

        Assert.Contains(
            "/commodities",
            code,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "/commodity/name/",
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
             directory = directory.Parent)
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
