using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeDistanceSemanticsTests
{
    [Fact]
    public void PresentationDistanceIsSupplierToBuyerLeg()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        var candidate =
            new TradeRouteCandidate
            {
                Source =
                    Order(
                        1,
                        "Source",
                        1_000,
                        0,
                        now),
                Target =
                    Order(
                        2,
                        "Target",
                        0,
                        3_000,
                        now),
                ProfitPerTon =
                    2_000,
                TradableAmount =
                    100,
                ProfitPerTrip =
                    200_000,
                OriginToSourceDistanceLy =
                    90,
                SourceToTargetDistanceLy =
                    25,
                SourceAge =
                    TimeSpan.FromHours(1),
                TargetAge =
                    TimeSpan.FromHours(1)
            };

        var route =
            TradeRoutePresentationAdapter.ToPresentation(
                candidate);

        Assert.Equal(
            25,
            route.RouteDistance,
            8);
    }

    private static TradeMarketOrder Order(
        long market,
        string system,
        int buy,
        int sell,
        DateTimeOffset updated) =>
        new()
        {
            CommodityName = "gold",
            MarketId = market,
            StationName = $"Station {market}",
            StationType = "Coriolis",
            DistanceToArrivalLs = 100,
            MaxLandingPadSize = 3,
            SystemAddress = market,
            SystemName = system,
            BuyFromStationPrice = buy,
            SellToStationPrice = sell,
            Stock = 1_000,
            Demand = 1_000,
            UpdatedAt = updated
        };
}
