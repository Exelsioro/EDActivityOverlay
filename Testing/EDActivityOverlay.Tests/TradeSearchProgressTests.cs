using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeSearchProgressTests
{
    [Fact]
    public async Task ProgressiveSearchPublishesResultsBeforeCompletion()
    {
        var service =
            new TradeSearchService(
                new DelayedProvider());

        var events =
            new List<TradeSearchProgress>();

        await foreach (TradeSearchProgress progress
                       in service.SearchProgressAsync(
                           Constraints()))
        {
            events.Add(
                progress);
        }

        TradeSearchProgress[] searching =
            events
                .Where(
                    item =>
                        item.Stage
                        == TradeSearchStage.Searching
                        && item.CompletedCommodities > 0)
                .ToArray();

        Assert.NotEmpty(
            searching);

        Assert.Contains(
            searching,
            item =>
                item.BestCandidates.Count > 0
                && item.CompletedCommodities
                   < item.TotalCommodities);

        TradeSearchProgress completed =
            Assert.Single(
                events.Where(
                    item =>
                        item.Stage
                        == TradeSearchStage.Completed));

        Assert.Equal(
            2,
            completed.TotalCommodities);

        Assert.Equal(
            2,
            completed.CompletedCommodities);

        Assert.Equal(
            0,
            completed.FailedCommodities);

        Assert.NotEmpty(
            completed.BestCandidates);
    }

    [Fact]
    public async Task OneCommodityFailureDoesNotDiscardOtherResults()
    {
        var service =
            new TradeSearchService(
                new DelayedProvider(
                    failSecondCommodity: true));

        TradeSearchProgress? completed =
            null;

        await foreach (TradeSearchProgress progress
                       in service.SearchProgressAsync(
                           Constraints()))
        {
            if (progress.Stage
                == TradeSearchStage.Completed)
            {
                completed =
                    progress;
            }
        }

        Assert.NotNull(
            completed);

        Assert.Equal(
            1,
            completed!.FailedCommodities);

        Assert.NotEmpty(
            completed.BestCandidates);
    }

    private static TradeSearchConstraints Constraints() =>
        new()
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
        };

    private sealed class DelayedProvider : ITradeDataProvider
    {
        private readonly bool failSecondCommodity;

        public DelayedProvider(
            bool failSecondCommodity = false)
        {
            this.failSecondCommodity =
                failSecondCommodity;
        }

        public string Name =>
            "Delayed";

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
                        "fast",
                        100,
                        5_000,
                        1_000,
                        1_000),

                    new TradeCommoditySummary(
                        "slow",
                        100,
                        4_000,
                        1_000,
                        1_000)
                ]);

        public async Task<IReadOnlyList<TradeMarketOrder>> GetSystemCommodityOrdersAsync(
            TradeSystemLocation system,
            string commodityName,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                5,
                cancellationToken);

            return
                Array.Empty<TradeMarketOrder>();
        }

        public async Task<IReadOnlyList<TradeMarketOrder>> GetNearbyExportsAsync(
            TradeSystemLocation system,
            string commodityName,
            int maxDistanceLy,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                commodityName == "fast"
                    ? 10
                    : 80,
                cancellationToken);

            if (commodityName == "slow"
                && failSecondCommodity)
            {
                throw new InvalidOperationException(
                    "simulated commodity failure");
            }

            return
                [
                    Order(
                        commodityName,
                        commodityName == "fast"
                            ? 10
                            : 11,
                        10,
                        buy: 1_000,
                        sell: 0,
                        stock: 100,
                        demand: 0)
                ];
        }

        public async Task<IReadOnlyList<TradeMarketOrder>> GetNearbyImportsAsync(
            TradeSystemLocation system,
            string commodityName,
            int maxDistanceLy,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                commodityName == "fast"
                    ? 15
                    : 85,
                cancellationToken);

            return
                [
                    Order(
                        commodityName,
                        commodityName == "fast"
                            ? 20
                            : 21,
                        20,
                        buy: 0,
                        sell: commodityName == "fast"
                            ? 3_000
                            : 2_500,
                        stock: 0,
                        demand: 1_000)
                ];
        }

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
