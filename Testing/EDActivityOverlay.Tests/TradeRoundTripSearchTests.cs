using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeRoundTripSearchTests
{
    [Fact]
    public async Task FindsProfitableReturnCommodityFromDirectionalSystemSidesAtExactStations()
    {
        var provider = new FakeRoundTripProvider(profitableReturn: true);
        var service = new TradeRoundTripSearchService(provider);
        TradeRoundTripSearchProgress? final = null;

        await foreach (TradeRoundTripSearchProgress progress
                       in service.SearchProgressAsync(Constraints(), seedLimit: 10))
        {
            if (progress.Stage == TradeRoundTripSearchStage.Completed)
            {
                final = progress;
            }
        }

        Assert.NotNull(final);
        TradeRoundTripCandidate candidate = Assert.Single(final!.BestCandidates);

        Assert.Equal("gold", candidate.Outbound.Source.CommodityName);
        Assert.Equal("silver", candidate.ReturnCommodity);
        Assert.Equal(10, candidate.Outbound.Source.MarketId);
        Assert.Equal(20, candidate.Outbound.Target.MarketId);

        // Target-system exports also contain market 999 with a much cheaper
        // silver price. Round trip must stay on station B, market 20.
        Assert.Equal(20, candidate.ReturnSource.MarketId);
        Assert.Equal(10, candidate.ReturnTarget.MarketId);

        Assert.Equal(200_000, candidate.Outbound.ProfitPerTrip);
        Assert.Equal(100_000, candidate.ReturnProfitPerTrip);
        Assert.Equal(300_000, candidate.ProfitPerCycle);
        Assert.Equal(20, candidate.CycleDistanceLy, 8);

        Assert.True(provider.SystemExportsCalls > 0);
        Assert.True(provider.SystemImportsCalls > 0);
    }

    [Fact]
    public async Task DoesNotInventRoundTripWithoutProfitableReturnLeg()
    {
        var service = new TradeRoundTripSearchService(
            new FakeRoundTripProvider(profitableReturn: false));

        TradeRoundTripSearchProgress? final = null;

        await foreach (TradeRoundTripSearchProgress progress
                       in service.SearchProgressAsync(Constraints(), seedLimit: 10))
        {
            if (progress.Stage == TradeRoundTripSearchStage.Completed)
            {
                final = progress;
            }
        }

        Assert.NotNull(final);
        Assert.Empty(final!.BestCandidates);
    }

    [Fact]
    public async Task PublishesEnrichmentProgressBeforeCompletion()
    {
        var service = new TradeRoundTripSearchService(
            new FakeRoundTripProvider(profitableReturn: true));

        bool sawDiscovery = false;
        bool sawEnrichment = false;

        await foreach (TradeRoundTripSearchProgress progress
                       in service.SearchProgressAsync(Constraints(), seedLimit: 10))
        {
            sawDiscovery |= progress.Stage == TradeRoundTripSearchStage.DiscoveringOutbound;
            sawEnrichment |= progress.Stage == TradeRoundTripSearchStage.EnrichingPairs;
        }

        Assert.True(sawDiscovery);
        Assert.True(sawEnrichment);
    }

    private static TradeSearchConstraints Constraints() =>
        new()
        {
            OriginSystemName = "Origin",
            OriginSystemAddress = 1,
            CargoCapacity = 100,
            SourceSearchRadiusLy = 30,
            TargetSearchRadiusLy = 30,
            MaxDataAge = TimeSpan.FromDays(3),
            MinLandingPadSize = 1,
            MinSupply = 1,
            MinDemand = 1,
            MaxCommodityCandidates = 1,
            MaxResults = 100,
            MaxConcurrentCommoditySearches = 2
        };

    private sealed class FakeRoundTripProvider :
        ITradeDataProvider,
        ITradeSystemTradeSidesProvider
    {
        private readonly bool profitableReturn;
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;

        public FakeRoundTripProvider(bool profitableReturn)
        {
            this.profitableReturn = profitableReturn;
        }

        public int SystemExportsCalls { get; private set; }
        public int SystemImportsCalls { get; private set; }

        public string Name => "round-trip-fake";

        public Task<TradeSystemLocation> ResolveSystemAsync(
            TradeSystemReference system,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TradeSystemLocation(1, "Origin", 0, 0, 0));

        public Task<IReadOnlyList<TradeCommoditySummary>> GetCommoditySummariesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeCommoditySummary>>(
                [new TradeCommoditySummary("gold", 1_000, 3_000, 1_000, 1_000)]);

        public Task<IReadOnlyList<TradeMarketOrder>> GetSystemCommodityOrdersAsync(
            TradeSystemLocation system,
            string commodityName,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(Array.Empty<TradeMarketOrder>());

        public Task<IReadOnlyList<TradeMarketOrder>> GetNearbyExportsAsync(
            TradeSystemLocation system,
            string commodityName,
            int maxDistanceLy,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                [
                    Order(
                        commodity: "gold",
                        market: 10,
                        station: "Station A",
                        systemAddress: 100,
                        system: "Source System",
                        x: 10,
                        buy: 1_000,
                        sell: 0,
                        stock: 1_000,
                        demand: 0)
                ]);

        public Task<IReadOnlyList<TradeMarketOrder>> GetNearbyImportsAsync(
            TradeSystemLocation system,
            string commodityName,
            int maxDistanceLy,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                [
                    Order(
                        commodity: "gold",
                        market: 20,
                        station: "Station B",
                        systemAddress: 200,
                        system: "Target System",
                        x: 20,
                        buy: 0,
                        sell: 3_000,
                        stock: 0,
                        demand: 1_000)
                ]);

        public Task<IReadOnlyList<TradeMarketOrder>> GetSystemExportsAsync(
            TradeSystemLocation system,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default)
        {
            SystemExportsCalls++;

            IReadOnlyList<TradeMarketOrder> result = system.SystemAddress == 200
                ?
                [
                    Order(
                        commodity: "silver",
                        market: 20,
                        station: "Station B",
                        systemAddress: 200,
                        system: "Target System",
                        x: 20,
                        buy: profitableReturn ? 500 : 1_600,
                        sell: 0,
                        stock: 1_000,
                        demand: 0),
                    Order(
                        commodity: "silver",
                        market: 999,
                        station: "Wrong Station",
                        systemAddress: 200,
                        system: "Target System",
                        x: 20,
                        buy: 100,
                        sell: 0,
                        stock: 1_000,
                        demand: 0)
                ]
                : Array.Empty<TradeMarketOrder>();

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<TradeMarketOrder>> GetSystemImportsAsync(
            TradeSystemLocation system,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default)
        {
            SystemImportsCalls++;

            IReadOnlyList<TradeMarketOrder> result = system.SystemAddress == 100
                ?
                [
                    Order(
                        commodity: "silver",
                        market: 10,
                        station: "Station A",
                        systemAddress: 100,
                        system: "Source System",
                        x: 10,
                        buy: 0,
                        sell: 1_500,
                        stock: 0,
                        demand: 1_000)
                ]
                : Array.Empty<TradeMarketOrder>();

            return Task.FromResult(result);
        }

        private TradeMarketOrder Order(
            string commodity,
            long market,
            string station,
            long systemAddress,
            string system,
            double x,
            int buy,
            int sell,
            long stock,
            long demand) =>
            new()
            {
                CommodityName = commodity,
                MarketId = market,
                StationName = station,
                StationType = "Coriolis",
                DistanceToArrivalLs = 500,
                MaxLandingPadSize = 3,
                SystemAddress = systemAddress,
                SystemName = system,
                SystemX = x,
                SystemY = 0,
                SystemZ = 0,
                BuyFromStationPrice = buy,
                SellToStationPrice = sell,
                Stock = stock,
                Demand = demand,
                UpdatedAt = now - TimeSpan.FromMinutes(10)
            };
    }
}
