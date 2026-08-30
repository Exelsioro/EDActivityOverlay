using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeRoundTripPresentationTests
{
    [Fact]
    public void AdapterProducesTwoLegRouteForExistingTrackerContract()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        var roundTrip =
            new TradeRoundTripCandidate
            {
                Outbound =
                    new TradeRouteCandidate
                    {
                        Source =
                            Order(
                                "gold",
                                10,
                                "A",
                                "System A",
                                1_000,
                                0,
                                1_000,
                                0,
                                now),
                        Target =
                            Order(
                                "gold",
                                20,
                                "B",
                                "System B",
                                0,
                                3_000,
                                0,
                                1_000,
                                now),
                        ProfitPerTon =
                            2_000,
                        TradableAmount =
                            100,
                        ProfitPerTrip =
                            200_000,
                        OriginToSourceDistanceLy =
                            80,
                        SourceToTargetDistanceLy =
                            25,
                        SourceAge =
                            TimeSpan.FromMinutes(
                                10),
                        TargetAge =
                            TimeSpan.FromMinutes(
                                10)
                    },
                ReturnSource =
                    Order(
                        "silver",
                        20,
                        "B",
                        "System B",
                        500,
                        0,
                        1_000,
                        0,
                        now),
                ReturnTarget =
                    Order(
                        "silver",
                        10,
                        "A",
                        "System A",
                        0,
                        1_500,
                        0,
                        1_000,
                        now),
                ReturnProfitPerTon =
                    1_000,
                ReturnTradableAmount =
                    100,
                ReturnProfitPerTrip =
                    100_000,
                ReturnSourceAge =
                    TimeSpan.FromMinutes(
                        10),
                ReturnTargetAge =
                    TimeSpan.FromMinutes(
                        10)
            };

        var route =
            TradeRoutePresentationAdapter.ToPresentation(
                roundTrip);

        Assert.True(
            route.IsRoundTrip);

        Assert.NotNull(
            route.SecondRoute);

        Assert.Equal(
            "gold",
            route.FirstRoute.BuyCommodity.Name);

        Assert.Equal(
            "silver",
            route.SecondRoute!.BuyCommodity.Name);

        Assert.Equal(
            25,
            route.RouteDistance,
            8);

        Assert.Equal(
            50,
            route.TotalRouteDistance,
            8);

        Assert.Equal(
            300_000,
            route.TotalProfitPerTrip);
    }

    private static TradeMarketOrder Order(
        string commodity,
        long market,
        string station,
        string system,
        int buy,
        int sell,
        long stock,
        long demand,
        DateTimeOffset now) =>
        new()
        {
            CommodityName =
                commodity,
            MarketId =
                market,
            StationName =
                station,
            StationType =
                "Coriolis",
            DistanceToArrivalLs =
                500,
            MaxLandingPadSize =
                3,
            SystemAddress =
                market == 10
                    ? 100
                    : 200,
            SystemName =
                system,
            BuyFromStationPrice =
                buy,
            SellToStationPrice =
                sell,
            Stock =
                stock,
            Demand =
                demand,
            UpdatedAt =
                now
        };
}
