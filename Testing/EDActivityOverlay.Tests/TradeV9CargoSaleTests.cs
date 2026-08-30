using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeV9CargoSaleTests
{
    [Theory]
    [InlineData("$gold_name;", "gold")]
    [InlineData("Gold", "gold")]
    [InlineData("  TRITIUM  ", "tritium")]
    public void CommodityIdentityIsStableAcrossJournalRepresentations(
        string source,
        string expected)
    {
        Assert.Equal(
            expected,
            CommodityIdentity.Normalize(source));
    }

    [Fact]
    public async Task CargoSaleAggregatesMixedCargoAtOneBuyer()
    {
        var provider =
            new FakeProvider(
                new Dictionary<string, IReadOnlyList<TradeMarketOrder>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["gold"] =
                    [
                        Order(
                            "gold",
                            100,
                            "Buyer",
                            15_000,
                            demand: 500)
                    ],
                    ["silver"] =
                    [
                        Order(
                            "silver",
                            100,
                            "Buyer",
                            7_000,
                            demand: 500)
                    ]
                });

        var state =
            Snapshot(
                new CargoCommoditySnapshot(
                    "gold",
                    "Золото",
                    10),
                new CargoCommoditySnapshot(
                    "silver",
                    "Серебро",
                    20));

        var service =
            new CargoSaleSearchService(
                provider);

        CargoSaleCandidate candidate =
            Assert.Single(
                await service.SearchAsync(
                    state,
                    Constraints(
                        cargo: 30)));

        Assert.Equal(30, candidate.SellableUnits);
        Assert.Equal(290_000, candidate.TotalRevenue);
        Assert.Equal(2, candidate.Lines.Count);
        Assert.Equal(1d, candidate.CoverageRatio, 6);
    }

    [Fact]
    public async Task LiveMarketJsonOverridesArdentForCurrentDockedMarket()
    {
        var provider =
            new FakeProvider(
                new Dictionary<string, IReadOnlyList<TradeMarketOrder>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["gold"] =
                    [
                        Order(
                            "gold",
                            42,
                            "Current Station",
                            5_000,
                            demand: 500)
                    ]
                });

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        GameStateSnapshot state =
            Snapshot(
                new CargoCommoditySnapshot(
                    "gold",
                    "Золото",
                    10))
            with
            {
                Docked = true,
                Station = "Current Station",
                MarketId = 42,
                MarketSnapshotId = 42,
                MarketSystem = "Sol",
                MarketStation = "Current Station",
                MarketUpdatedUtc = now,
                MarketByCommodityId =
                    new Dictionary<string, MarketItemSnapshot>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["gold"] =
                            new MarketItemSnapshot(
                                "Золото",
                                0,
                                6_000,
                                0,
                                500)
                    }
            };

        var service =
            new CargoSaleSearchService(
                provider);

        CargoSaleCandidate candidate =
            Assert.Single(
                await service.SearchAsync(
                    state,
                    Constraints(
                        cargo: 10)));

        Assert.True(candidate.IsCurrentMarket);
        Assert.Equal(60_000, candidate.TotalRevenue);
        Assert.Equal(6_000, candidate.Lines[0].SellPrice);
    }

    [Fact]
    public void TradeWorkspaceExposesCargoSaleMode()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        Assert.Contains(
            "Tag=\"cargo\"",
            xaml,
            StringComparison.Ordinal);

        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.CargoSale.cs");

        Assert.Contains(
            "CargoSaleSearchService",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "Loc_TRADE_CARGO_SEARCH",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void JournalReducerStoresStableCommodityKeys()
    {
        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "Services",
                "Journal",
                "JournalStateReducer.cs");

        Assert.Contains(
            "CommodityIdentity.Normalize",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "CargoByCommodityId",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "MarketByCommodityId",
            code,
            StringComparison.Ordinal);
    }

    private static GameStateSnapshot Snapshot(
        params CargoCommoditySnapshot[] cargo) =>
        new()
        {
            JournalAvailable = true,
            StarSystem = "Sol",
            SystemAddress = 1,
            SystemX = 0,
            SystemY = 0,
            SystemZ = 0,
            MaxJumpRangeLy = 30,
            UnladenMassTonnes = 300,
            CargoByCommodityId =
                cargo.ToDictionary(
                    item => item.CommodityId,
                    item => item,
                    StringComparer.OrdinalIgnoreCase)
        };

    private static TradeSearchConstraints Constraints(
        int cargo) =>
        new()
        {
            OriginSystemName = "Sol",
            OriginSystemAddress = 1,
            CargoCapacity = cargo,
            SourceSearchRadiusLy = 0,
            TargetSearchRadiusLy = 80,
            MaxDataAge = TimeSpan.FromDays(3),
            MinLandingPadSize = 1,
            MinSupply = 1,
            MinDemand = 1,
            MaxResults = 100,
            MaxConcurrentCommoditySearches = 4
        };

    private static TradeMarketOrder Order(
        string commodity,
        long marketId,
        string station,
        int sellPrice,
        long demand) =>
        new()
        {
            CommodityName = commodity,
            MarketId = marketId,
            StationName = station,
            StationType = "Coriolis",
            DistanceToArrivalLs = 500,
            MaxLandingPadSize = 3,
            SystemAddress = 2,
            SystemName = "Buyer System",
            SystemX = 10,
            SystemY = 0,
            SystemZ = 0,
            SellToStationPrice = sellPrice,
            Demand = demand,
            UpdatedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10),
            ReferenceDistanceLy = 10
        };

    private sealed class FakeProvider : ITradeDataProvider
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<TradeMarketOrder>> imports;

        public FakeProvider(
            IReadOnlyDictionary<string, IReadOnlyList<TradeMarketOrder>> imports)
        {
            this.imports = imports;
        }

        public string Name => "fake";

        public Task<TradeSystemLocation> ResolveSystemAsync(
            TradeSystemReference system,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new TradeSystemLocation(
                    system.SystemAddress,
                    system.Name,
                    0,
                    0,
                    0));

        public Task<IReadOnlyList<TradeCommoditySummary>> GetCommoditySummariesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeCommoditySummary>>(
                Array.Empty<TradeCommoditySummary>());

        public Task<IReadOnlyList<TradeMarketOrder>> GetSystemCommodityOrdersAsync(
            TradeSystemLocation system,
            string commodityName,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                Array.Empty<TradeMarketOrder>());

        public Task<IReadOnlyList<TradeMarketOrder>> GetNearbyExportsAsync(
            TradeSystemLocation system,
            string commodityName,
            int maxDistanceLy,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                Array.Empty<TradeMarketOrder>());

        public Task<IReadOnlyList<TradeMarketOrder>> GetNearbyImportsAsync(
            TradeSystemLocation system,
            string commodityName,
            int maxDistanceLy,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                imports.TryGetValue(
                    commodityName,
                    out IReadOnlyList<TradeMarketOrder>? value)
                    ? value
                    : (IReadOnlyList<TradeMarketOrder>)Array.Empty<TradeMarketOrder>());
    }

    private static string ReadProjectFile(
        params string[] relative)
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
        {
            string candidate =
                Path.Combine(
                    [
                        directory.FullName,
                        .. relative
                    ]);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
