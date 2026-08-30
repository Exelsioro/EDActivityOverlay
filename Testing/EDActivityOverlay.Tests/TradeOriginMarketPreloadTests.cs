using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeOriginMarketPreloadTests
{
    [Fact]
    public async Task BulkOriginMarketIsLoadedOnceForAllCommodities()
    {
        var provider =
            new BulkProvider();

        var service =
            new TradeSearchService(
                provider);

        TradeSearchResult result =
            await service.SearchAsync(
                new TradeSearchConstraints
                {
                    OriginSystemName =
                        "Origin",
                    CargoCapacity =
                        100,
                    SourceSearchRadiusLy =
                        30,
                    TargetSearchRadiusLy =
                        60,
                    MaxDataAge =
                        TimeSpan.FromDays(
                            3),
                    MinLandingPadSize =
                        1,
                    MaxCommodityCandidates =
                        2,
                    MaxResults =
                        20,
                    MaxConcurrentCommoditySearches =
                        2
                });

        Assert.Equal(
            1,
            provider.BulkOriginCalls);

        Assert.Equal(
            0,
            provider.PerCommodityOriginCalls);

        Assert.NotEmpty(
            result.Candidates);
    }

    private sealed class BulkProvider :
        ITradeDataProvider,
        ITradeOriginMarketProvider
    {
        public int BulkOriginCalls { get; private set; }
        public int PerCommodityOriginCalls { get; private set; }

        public string Name =>
            "Bulk";

        public Task<TradeSystemLocation> ResolveSystemAsync(
            TradeSystemReference system,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new TradeSystemLocation(
                    1,
                    "Origin",
                    0,
                    0,
                    0));

        public Task<IReadOnlyList<TradeCommoditySummary>> GetCommoditySummariesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeCommoditySummary>>(
                [
                    new TradeCommoditySummary(
                        "gold",
                        1_000,
                        5_000,
                        1_000,
                        1_000),
                    new TradeCommoditySummary(
                        "silver",
                        1_000,
                        4_000,
                        1_000,
                        1_000)
                ]);

        public Task<IReadOnlyList<TradeMarketOrder>> GetSystemOrdersAsync(
            TradeSystemLocation system,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default)
        {
            BulkOriginCalls++;

            return
                Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                    Array.Empty<TradeMarketOrder>());
        }

        public Task<IReadOnlyList<TradeMarketOrder>> GetSystemCommodityOrdersAsync(
            TradeSystemLocation system,
            string commodityName,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default)
        {
            PerCommodityOriginCalls++;

            return
                Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                    Array.Empty<TradeMarketOrder>());
        }

        public Task<IReadOnlyList<TradeMarketOrder>> GetNearbyExportsAsync(
            TradeSystemLocation system,
            string commodityName,
            int maxDistanceLy,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                [
                    Order(
                        commodityName,
                        commodityName == "gold"
                            ? 10
                            : 11,
                        x:
                            10,
                        buy:
                            1_000,
                        sell:
                            0,
                        stock:
                            1_000,
                        demand:
                            0)
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
                        commodityName,
                        commodityName == "gold"
                            ? 20
                            : 21,
                        x:
                            20,
                        buy:
                            0,
                        sell:
                            commodityName == "gold"
                                ? 4_000
                                : 3_000,
                        stock:
                            0,
                        demand:
                            1_000)
                ]);

        private static TradeMarketOrder Order(
            string commodity,
            long market,
            double x,
            int buy,
            int sell,
            long stock,
            long demand) =>
            new()
            {
                CommodityName =
                    commodity,
                MarketId =
                    market,
                StationName =
                    $"Station {market}",
                StationType =
                    "Coriolis",
                DistanceToArrivalLs =
                    100,
                MaxLandingPadSize =
                    3,
                SystemAddress =
                    market,
                SystemName =
                    $"System {market}",
                SystemX =
                    x,
                SystemY =
                    0,
                SystemZ =
                    0,
                BuyFromStationPrice =
                    buy,
                SellToStationPrice =
                    sell,
                Stock =
                    stock,
                Demand =
                    demand,
                UpdatedAt =
                    DateTimeOffset.UtcNow
            };
    }
}
