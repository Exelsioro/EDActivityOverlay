using EDActivityOverlay.Models.Trading;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeRoutePresentationAdapterTests
{
    [Fact]
    public void CandidateMapsToExistingTradeRouteUiContract()
    {
        DateTimeOffset updated =
            new(
                2026,
                8,
                29,
                1,
                0,
                0,
                TimeSpan.Zero);

        var candidate =
            new TradeRouteCandidate
            {
                Source =
                    new TradeMarketOrder
                    {
                        CommodityName =
                            "gold",
                        MarketId =
                            1,
                        StationName =
                            "Source Station",
                        StationType =
                            "Coriolis",
                        DistanceToArrivalLs =
                            120,
                        MaxLandingPadSize =
                            3,
                        SystemAddress =
                            11,
                        SystemName =
                            "Source",
                        BuyFromStationPrice =
                            1_000,
                        Stock =
                            500,
                        UpdatedAt =
                            updated
                    },
                Target =
                    new TradeMarketOrder
                    {
                        CommodityName =
                            "gold",
                        MarketId =
                            2,
                        StationName =
                            "Target Station",
                        StationType =
                            "Orbis",
                        DistanceToArrivalLs =
                            300,
                        MaxLandingPadSize =
                            3,
                        SystemAddress =
                            22,
                        SystemName =
                            "Target",
                        SellToStationPrice =
                            3_000,
                        Demand =
                            1_000,
                        UpdatedAt =
                            updated
                    },
                ProfitPerTon =
                    2_000,
                TradableAmount =
                    100,
                ProfitPerTrip =
                    200_000,
                OriginToSourceDistanceLy =
                    10,
                SourceToTargetDistanceLy =
                    25,
                SourceAge =
                    TimeSpan.FromHours(
                        1),
                TargetAge =
                    TimeSpan.FromHours(
                        1)
            };

        TradeRoute route =
            TradeRoutePresentationAdapter.ToPresentation(
                candidate);

        Assert.Equal(
            "Source",
            route.CardHeader.FromStation.System);

        Assert.Equal(
            "Target",
            route.CardHeader.ToStation.System);

        Assert.Equal(
            1_000,
            route.FirstRoute.BuyCommodity.Price);

        Assert.Equal(
            3_000,
            route.FirstRoute.SellCommodity.Price);

        Assert.Equal(
            100,
            route.CargoCapacity);

        Assert.Equal(
            200_000,
            route.TotalProfitPerTrip);

        // Trade distance is the supplier -> buyer leg only.
        // The search anchor is only used to include/exclude suppliers.
        Assert.Equal(
            25,
            route.RouteDistance,
            8);
    }
}
