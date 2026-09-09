using EDActivityOverlay.Services.Mining;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MiningLocationMarketEnrichmentTests
{
    [Fact]
    public async Task EnrichmentUsesBuyerAroundCandidateAndReplacesProvisionalMarketScore()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var provider = new FakeTradeProvider(
            [
                Buyer("Platinum", 81_000, "Buyer A", "Port A", 24, 50_000, now.AddMinutes(-20))
            ]);

        var service = new MiningLocationMarketEnrichmentService(provider);
        var query = new MiningLocationQuery
        {
            ReferenceSystem = "Origin",
            CommodityIds = ["Platinum"]
        };
        var candidate = new MiningLocationCandidate
        {
            SystemName = "Mine A",
            RingName = "Mine A 1 A Ring",
            HotspotCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Platinum"] = 1
            },
            Score = 70,
            MarketScore = 2
        };

        IReadOnlyList<MiningLocationCandidate> result =
            await service.EnrichAsync(query, [candidate], CancellationToken.None);

        MiningLocationCandidate enriched = Assert.Single(result);
        Assert.True(enriched.HasDestinationMarket);
        Assert.Equal("Platinum", enriched.BestSellCommodityId);
        Assert.Equal(81_000, enriched.BestSellPrice);
        Assert.Equal("Buyer A", enriched.BestSellSystemName);
        Assert.Equal("Port A", enriched.BestSellStationName);
        Assert.Equal(24, enriched.BestSellDistanceLy, 3);
        Assert.Equal(50_000, enriched.BestSellDemand);
        Assert.Equal(4, enriched.MarketScore);
        Assert.Equal(72, enriched.Score);
    }

    [Fact]
    public async Task EnrichmentRejectsCarrierStaleAndSmallPadBuyers()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var provider = new FakeTradeProvider(
            [
                Buyer("Platinum", 100_000, "Carrier", "FC", 5, 100_000, now.AddMinutes(-5), stationType: "Fleet Carrier"),
                Buyer("Platinum", 95_000, "Stale", "Old Port", 6, 100_000, now.AddDays(-4)),
                Buyer("Platinum", 90_000, "Outpost", "Medium Port", 7, 100_000, now.AddMinutes(-5), maxPad: 2),
                Buyer("Platinum", 80_000, "Valid", "Large Port", 18, 20_000, now.AddMinutes(-10), maxPad: 3)
            ]);

        var service = new MiningLocationMarketEnrichmentService(provider);
        var query = new MiningLocationQuery
        {
            ReferenceSystem = "Origin",
            CommodityIds = ["Platinum"]
        };
        var candidate = new MiningLocationCandidate
        {
            SystemName = "Mine A",
            RingName = "Mine A 1 A Ring",
            HotspotCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Platinum"] = 1
            }
        };

        MiningLocationCandidate enriched = Assert.Single(
            await service.EnrichAsync(query, [candidate], CancellationToken.None));

        Assert.Equal(80_000, enriched.BestSellPrice);
        Assert.Equal("Valid", enriched.BestSellSystemName);
        Assert.Equal("Large Port", enriched.BestSellStationName);
    }

    private static TradeMarketOrder Buyer(
        string commodity,
        int sellPrice,
        string system,
        string station,
        double distanceLy,
        long demand,
        DateTimeOffset updated,
        string stationType = "Coriolis Starport",
        int maxPad = 3) =>
        new()
        {
            CommodityName = commodity,
            SellToStationPrice = sellPrice,
            SystemName = system,
            StationName = station,
            StationType = stationType,
            MaxLandingPadSize = maxPad,
            Demand = demand,
            UpdatedAt = updated,
            ReferenceDistanceLy = distanceLy
        };

    private sealed class FakeTradeProvider(
        IReadOnlyList<TradeMarketOrder> imports) : ITradeDataProvider
    {
        public string Name => "Fake";

        public Task<TradeSystemLocation> ResolveSystemAsync(
            TradeSystemReference system,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TradeSystemLocation(
                1,
                system.Name,
                0,
                0,
                0));

        public Task<IReadOnlyList<TradeCommoditySummary>> GetCommoditySummariesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeCommoditySummary>>(Array.Empty<TradeCommoditySummary>());

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
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(Array.Empty<TradeMarketOrder>());

        public Task<IReadOnlyList<TradeMarketOrder>> GetNearbyImportsAsync(
            TradeSystemLocation system,
            string commodityName,
            int maxDistanceLy,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                imports
                    .Where(order => order.CommodityName.Equals(
                        commodityName,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray());
    }
}
