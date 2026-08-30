using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeRouteEngineV2Tests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 29, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RemoteSupplierInsideFirstRadiusCanProduceRoute()
    {
        TradeSystemLocation origin = new(1, "Origin", 0, 0, 0);
        TradeSearchConstraints constraints = Constraints();
        TradeMarketOrder source = Order(10, "Supplier", 20, buy: 1_000, sell: 0, stock: 100, demand: 0);
        TradeMarketOrder target = Order(20, "Buyer", 70, buy: 0, sell: 3_000, stock: 0, demand: 100);

        TradeRouteCandidate candidate = Assert.Single(
            TradeRouteEngine.BuildOneWayCandidates(origin, [source], [target], constraints, Now));

        Assert.Equal(2_000, candidate.ProfitPerTon);
        Assert.Equal(100, candidate.TradableAmount);
        Assert.Equal(200_000, candidate.ProfitPerTrip);
        Assert.Equal(20, candidate.OriginToSourceDistanceLy, 8);
        Assert.Equal(50, candidate.SourceToTargetDistanceLy, 8);
    }

    [Fact]
    public void ArdentZeroDemandIsInfinite()
    {
        TradeSystemLocation origin = new(1, "Origin", 0, 0, 0);
        TradeSearchConstraints constraints = Constraints() with { CargoCapacity = 120 };
        TradeMarketOrder source = Order(10, "Supplier", 10, buy: 1_000, sell: 0, stock: 500, demand: 0);
        TradeMarketOrder target = Order(20, "Buyer", 20, buy: 0, sell: 2_000, stock: 0, demand: 0);

        TradeRouteCandidate candidate = Assert.Single(
            TradeRouteEngine.BuildOneWayCandidates(origin, [source], [target], constraints, Now));

        Assert.True(target.HasInfiniteDemand);
        Assert.Equal(120, candidate.TradableAmount);
    }

    [Fact]
    public void TargetOutsideSecondRadiusIsRejected()
    {
        TradeSystemLocation origin = new(1, "Origin", 0, 0, 0);
        TradeSearchConstraints constraints = Constraints() with
        {
            SourceSearchRadiusLy = 30,
            TargetSearchRadiusLy = 40
        };
        TradeMarketOrder source = Order(10, "Supplier", 20, buy: 1_000, sell: 0, stock: 100, demand: 0);
        TradeMarketOrder target = Order(20, "Buyer", 70, buy: 0, sell: 3_000, stock: 0, demand: 100);

        Assert.Empty(
            TradeRouteEngine.BuildOneWayCandidates(origin, [source], [target], constraints, Now));
    }

    [Fact]
    public void StaleCarrierAndUnknownPadSourcesAreRejected()
    {
        TradeSystemLocation origin = new(1, "Origin", 0, 0, 0);
        TradeSearchConstraints constraints = Constraints() with
        {
            IncludeFleetCarriers = false,
            MaxDataAge = TimeSpan.FromHours(24)
        };

        TradeMarketOrder carrier = Order(10, "Supplier", 10, 1_000, 0, 100, 0) with
        {
            StationType = "FleetCarrier"
        };
        TradeMarketOrder stale = Order(11, "Supplier2", 12, 1_000, 0, 100, 0) with
        {
            UpdatedAt = Now - TimeSpan.FromDays(2)
        };
        TradeMarketOrder unknownPad = Order(12, "Supplier3", 14, 1_000, 0, 100, 0) with
        {
            MaxLandingPadSize = 0
        };
        TradeMarketOrder target = Order(20, "Buyer", 20, 0, 3_000, 0, 500);

        Assert.Empty(
            TradeRouteEngine.BuildOneWayCandidates(
                origin,
                [carrier, stale, unknownPad],
                [target],
                constraints,
                Now));
    }

    private static TradeSearchConstraints Constraints() => new()
    {
        OriginSystemName = "Origin",
        CargoCapacity = 100,
        SourceSearchRadiusLy = 30,
        TargetSearchRadiusLy = 60,
        MaxDataAge = TimeSpan.FromDays(3),
        MinLandingPadSize = 1,
        MinSupply = 1,
        MinDemand = 1
    };

    private static TradeMarketOrder Order(
        long marketId,
        string system,
        double x,
        int buy,
        int sell,
        long stock,
        long demand) => new()
    {
        CommodityName = "gold",
        MarketId = marketId,
        StationName = $"Station {marketId}",
        StationType = "Coriolis",
        DistanceToArrivalLs = 500,
        MaxLandingPadSize = 3,
        SystemAddress = marketId,
        SystemName = system,
        SystemX = x,
        SystemY = 0,
        SystemZ = 0,
        BuyFromStationPrice = buy,
        SellToStationPrice = sell,
        Stock = stock,
        Demand = demand,
        UpdatedAt = Now - TimeSpan.FromHours(1)
    };
}
