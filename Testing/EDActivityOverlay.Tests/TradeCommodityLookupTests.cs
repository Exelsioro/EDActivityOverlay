using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeCommodityLookupTests
{
    [Fact]
    public async Task FullCoverageIsPreferredThenCheapestPrice()
    {
        var provider = new FakeProvider([
            Order(1, "Partial Cheap", 800, 20, 4),
            Order(2, "Full Expensive", 1_100, 500, 8),
            Order(3, "Full Cheapest", 900, 200, 12)
        ]);
        var service = new TradeCommodityLookupService(provider);

        IReadOnlyList<TradeCommoditySourceCandidate> rows =
            await service.SearchAsync(Constraints(), "Gold", 100);

        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].FullCoverage);
        Assert.Equal("Full Cheapest", rows[0].Market.StationName);
        Assert.Equal(900, rows[0].Market.BuyFromStationPrice);
        Assert.Equal(90_000, rows[0].TotalCost);
        Assert.False(rows[^1].FullCoverage);
    }

    [Fact]
    public async Task FiltersCarrierPadArrivalAndStaleRows()
    {
        var provider = new FakeProvider([
            Order(1, "Good", 1_000, 200, 5),
            Order(2, "Carrier", 500, 200, 5) with { StationType = "Fleet Carrier" },
            Order(3, "Small", 600, 200, 5) with { MaxLandingPadSize = 1 },
            Order(4, "Far SC", 700, 200, 5) with { DistanceToArrivalLs = 50_000 },
            Order(5, "Stale", 400, 200, 5) with { UpdatedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(9) }
        ]);
        var service = new TradeCommodityLookupService(provider);
        TradeSearchConstraints constraints = Constraints() with
        {
            MaxStationDistanceLs = 10_000,
            MaxDataAge = TimeSpan.FromDays(3),
            MinLandingPadSize = 3,
            IncludeFleetCarriers = false
        };

        IReadOnlyList<TradeCommoditySourceCandidate> rows =
            await service.SearchAsync(constraints, "Gold", 100);

        TradeCommoditySourceCandidate only = Assert.Single(rows);
        Assert.Equal("Good", only.Market.StationName);
    }

    private static TradeSearchConstraints Constraints() => new()
    {
        OriginSystemName = "Origin",
        OriginSystemAddress = 42,
        CargoCapacity = 100,
        SourceSearchRadiusLy = 50,
        TargetSearchRadiusLy = 0,
        MaxDataAge = TimeSpan.FromDays(3),
        MinLandingPadSize = 1,
        MinSupply = 1,
        MinDemand = 1,
        MaxResults = 100
    };

    private static TradeMarketOrder Order(
        long marketId,
        string station,
        int price,
        long stock,
        double distance) => new()
    {
        CommodityName = "Gold",
        MarketId = marketId,
        StationName = station,
        StationType = "Coriolis",
        DistanceToArrivalLs = 500,
        MaxLandingPadSize = 3,
        SystemAddress = marketId + 100,
        SystemName = station + " System",
        SystemX = distance,
        SystemY = 0,
        SystemZ = 0,
        BuyFromStationPrice = price,
        Stock = stock,
        Demand = 0,
        UpdatedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(15),
        ReferenceDistanceLy = distance
    };

    private sealed class FakeProvider(IReadOnlyList<TradeMarketOrder> exports) : ITradeDataProvider
    {
        public string Name => "fake";
        public Task<TradeSystemLocation> ResolveSystemAsync(TradeSystemReference system, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TradeSystemLocation(42, system.Name, 0, 0, 0));
        public Task<IReadOnlyList<TradeCommoditySummary>> GetCommoditySummariesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeCommoditySummary>>([new("Gold", 500, 2_000, 1_000, 1_000)]);
        public Task<IReadOnlyList<TradeMarketOrder>> GetSystemCommodityOrdersAsync(TradeSystemLocation system, string commodityName, TradeSearchConstraints constraints, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(Array.Empty<TradeMarketOrder>());
        public Task<IReadOnlyList<TradeMarketOrder>> GetNearbyExportsAsync(TradeSystemLocation system, string commodityName, int maxDistanceLy, TradeSearchConstraints constraints, CancellationToken cancellationToken = default) =>
            Task.FromResult(exports);
        public Task<IReadOnlyList<TradeMarketOrder>> GetNearbyImportsAsync(TradeSystemLocation system, string commodityName, int maxDistanceLy, TradeSearchConstraints constraints, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(Array.Empty<TradeMarketOrder>());
    }
}
