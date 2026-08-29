using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeSearchServiceTests
{
    [Fact]
    public async Task SearchUsesCorrectSpreadAndTwoRadiusEnvelope()
    {
        var provider = new FakeProvider();
        var service = new TradeSearchService(provider);

        TradeSearchResult result = await service.SearchAsync(new TradeSearchConstraints
        {
            OriginSystemName = "Origin",
            CargoCapacity = 100,
            SourceSearchRadiusLy = 30,
            TargetSearchRadiusLy = 60,
            MaxDataAge = TimeSpan.FromDays(3),
            MinLandingPadSize = 1,
            MaxCommodityCandidates = 1,
            MaxConcurrentCommoditySearches = 1
        });

        TradeRouteCandidate route = Assert.Single(result.Candidates);
        Assert.Equal("high", route.Source.CommodityName);
        Assert.Equal(30, provider.LastExportRadiusLy);
        Assert.Equal(90, provider.LastImportRadiusLy);
    }

    [Fact]
    public async Task SearchIncludesOriginSystemOrders()
    {
        var provider = new FakeProvider { UseLocalSource = true };
        var service = new TradeSearchService(provider);

        TradeSearchResult result = await service.SearchAsync(new TradeSearchConstraints
        {
            OriginSystemName = "Origin",
            CargoCapacity = 10,
            SourceSearchRadiusLy = 0,
            TargetSearchRadiusLy = 40,
            MaxDataAge = TimeSpan.FromDays(3),
            MinLandingPadSize = 1,
            MaxCommodityCandidates = 1,
            MaxConcurrentCommoditySearches = 1
        });

        TradeRouteCandidate route = Assert.Single(result.Candidates);
        Assert.Equal("Origin", route.Source.SystemName);
        Assert.Equal(0, route.OriginToSourceDistanceLy, 8);
    }

    private sealed class FakeProvider : ITradeDataProvider
    {
        private static readonly DateTimeOffset Updated = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        public bool UseLocalSource { get; init; }
        public int LastExportRadiusLy { get; private set; }
        public int LastImportRadiusLy { get; private set; }
        public string Name => "Fake";

        public Task<TradeSystemLocation> ResolveSystemAsync(
            TradeSystemReference system,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TradeSystemLocation(1, "Origin", 0, 0, 0));

        public Task<IReadOnlyList<TradeCommoditySummary>> GetCommoditySummariesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeCommoditySummary>>(
            [
                new TradeCommoditySummary("low", 9_000, 9_100, 1_000, 1_000),
                new TradeCommoditySummary("high", 1_000, 5_000, 1_000, 1_000)
            ]);

        public Task<IReadOnlyList<TradeMarketOrder>> GetSystemCommodityOrdersAsync(
            TradeSystemLocation system,
            string commodityName,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                UseLocalSource
                    ? [Order(commodityName, 11, "Origin", 0, 1_000, 0, 100, 0)]
                    : Array.Empty<TradeMarketOrder>());

        public Task<IReadOnlyList<TradeMarketOrder>> GetNearbyExportsAsync(
            TradeSystemLocation system,
            string commodityName,
            int maxDistanceLy,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default)
        {
            LastExportRadiusLy = maxDistanceLy;
            return Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                UseLocalSource
                    ? Array.Empty<TradeMarketOrder>()
                    : [Order(commodityName, 12, "Supplier", 20, 1_000, 0, 100, 0)]);
        }

        public Task<IReadOnlyList<TradeMarketOrder>> GetNearbyImportsAsync(
            TradeSystemLocation system,
            string commodityName,
            int maxDistanceLy,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default)
        {
            LastImportRadiusLy = maxDistanceLy;
            return Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                [Order(commodityName, 20, "Buyer", 40, 0, 4_000, 0, 1_000)]);
        }

        private static TradeMarketOrder Order(
            string commodity,
            long market,
            string system,
            double x,
            int buy,
            int sell,
            long stock,
            long demand) => new()
        {
            CommodityName = commodity,
            MarketId = market,
            StationName = $"Station {market}",
            StationType = "Coriolis",
            DistanceToArrivalLs = 100,
            MaxLandingPadSize = 3,
            SystemAddress = market,
            SystemName = system,
            SystemX = x,
            SystemY = 0,
            SystemZ = 0,
            BuyFromStationPrice = buy,
            SellToStationPrice = sell,
            Stock = stock,
            Demand = demand,
            UpdatedAt = Updated
        };
    }
}
