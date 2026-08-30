using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeV9cExecutionTests
{
    [Fact]
    public void LedgerTracksPartialWeightedBuyAndSell()
    {
        var ledger =
            new TradeExecutionLedger(
                100);

        ledger.ApplyBuy(
            40,
            1_000);

        ledger.ApplyBuy(
            60,
            1_100);

        Assert.Equal(
            100,
            ledger.PurchasedQuantity);

        Assert.Equal(
            106_000,
            ledger.PurchaseCost);

        Assert.Equal(
            1_060,
            ledger.AverageBuyPrice);

        ledger.ApplySell(
            25,
            1_500,
            averagePricePaid:
                1_060);

        Assert.Equal(
            25,
            ledger.SoldQuantity);

        Assert.Equal(
            75,
            ledger.RemainingPurchasedQuantity);

        Assert.Equal(
            37_500,
            ledger.SaleRevenue);

        Assert.Equal(
            11_000,
            ledger.RealizedProfit);

        Assert.Equal(
            44_000,
            ledger.ProjectedProfit(
                plannedBuyPrice:
                    1_000,
                plannedSellPrice:
                    1_500));
    }

    [Fact]
    public void LedgerKeepsActualCostWhenPlanIsOnlyPartiallyFilled()
    {
        var ledger =
            new TradeExecutionLedger(
                100);

        ledger.ApplyBuy(
            40,
            1_200);

        Assert.Equal(
            48_000,
            ledger.PurchaseCost);

        Assert.Equal(
            42_000,
            ledger.ProjectedProfit(
                plannedBuyPrice:
                    1_000,
                plannedSellPrice:
                    1_500));
    }

    [Fact]
    public void RoutePresentationCarriesPerLegPlannedQuantity()
    {
        string oneWay =
            ReadProjectFile(
                "EDActivityOverlay",
                "Services",
                "Trading",
                "TradeRoutePresentationAdapter.cs");

        string roundTrip =
            ReadProjectFile(
                "EDActivityOverlay",
                "Services",
                "Trading",
                "TradeRoutePresentationAdapter.RoundTrip.cs");

        string compactOneWay =
            oneWay
                .Replace(" ", string.Empty)
                .Replace("\t", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);

        string compactRoundTrip =
            roundTrip
                .Replace(" ", string.Empty)
                .Replace("\t", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty);

        Assert.Contains(
            "PlannedQuantity=candidate.TradableAmount",
            compactOneWay,
            StringComparison.Ordinal);

        Assert.Contains(
            "PlannedQuantity=outbound.TradableAmount",
            compactRoundTrip,
            StringComparison.Ordinal);

        Assert.Contains(
            "PlannedQuantity=candidate.ReturnTradableAmount",
            compactRoundTrip,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TrackerUsesStableCommodityAndMarketIdentity()
    {
        string tracker =
            ReadProjectFile(
                "EDActivityOverlay",
                "Services",
                "Journal",
                "TradeRouteProgressTracker.cs");

        Assert.Contains(
            "CargoByCommodityId",
            tracker,
            StringComparison.Ordinal);

        Assert.Contains(
            "MarketByCommodityId",
            tracker,
            StringComparison.Ordinal);

        Assert.Contains(
            "MarketSnapshotId",
            tracker,
            StringComparison.Ordinal);

        Assert.Contains(
            "CommodityIdentity.Normalize",
            tracker,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"MarketID\"",
            tracker,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedCardAndActiveHudShareOneProgressTracker()
    {
        string pinned =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "PinnedRouteOverlay.xaml.cs");

        string host =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.Trade.cs");

        string active =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.ActiveTrade.cs");

        Assert.Contains(
            "public TradeRouteProgressTracker PinTradeRoute(",
            pinned,
            StringComparison.Ordinal);

        Assert.Contains(
            "TradeRouteProgressTracker? tracker =",
            host,
            StringComparison.Ordinal);

        Assert.Contains(
            "ActivatePinnedRoute(",
            host,
            StringComparison.Ordinal);

        Assert.Contains(
            "AttachExecutionTracker(",
            active,
            StringComparison.Ordinal);

        Assert.Contains(
            "executionTracker.ProgressChanged +=",
            active,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReroutePreservesExecutionTracker()
    {
        string host =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.Trade.cs");

        string pinned =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "PinnedRouteOverlay.xaml.cs");

        Assert.Contains(
            "preserveExecution:",
            host,
            StringComparison.Ordinal);

        Assert.Contains(
            "progressTracker.UpdateRoute(",
            pinned,
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
                directory.FullName;

            foreach (string part in relative)
            {
                candidate =
                    Path.Combine(
                        candidate,
                        part);
            }

            if (File.Exists(
                    candidate))
            {
                return File.ReadAllText(
                    candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
