using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeV10ContinuousTests
{
    [Fact]
    public async Task ExactCurrentMarketIsTheOnlyFirstHopSource()
    {
        FakeProvider provider =
            ScenarioProvider();

        var service =
            new TradeContinuousSearchService(
                provider);

        IReadOnlyList<TradeContinuousPlan> plans =
            await service.SearchAsync(
                Request());

        Assert.NotEmpty(plans);

        Assert.All(
            plans,
            plan =>
                Assert.Equal(
                    1,
                    plan.First.Source.MarketId));

        Assert.DoesNotContain(
            plans,
            plan =>
                plan.First.Source.MarketId
                == 99);
    }

    [Fact]
    public async Task LookaheadCanBeatGreedyImmediateProfit()
    {
        FakeProvider provider =
            ScenarioProvider();

        var service =
            new TradeContinuousSearchService(
                provider);

        IReadOnlyList<TradeContinuousPlan> plans =
            await service.SearchAsync(
                Request());

        TradeContinuousPlan best =
            plans.First();

        // z gives the bigger immediate B -> E profit. x wins because C has a
        // much stronger C -> D continuation.
        Assert.Equal(
            "x",
            best.First.Source.CommodityName);

        Assert.Equal(
            2,
            best.First.Target.MarketId);

        Assert.NotNull(
            best.Lookahead);

        Assert.Equal(
            4,
            best.Lookahead!.Target.MarketId);

        Assert.True(
            best.TotalProfit
            > best.First.ProfitPerTrip);
    }

    [Fact]
    public async Task RecentMarketReturnIsSoftPenaltyNotHardFilter()
    {
        FakeProvider provider =
            ScenarioProvider();

        var service =
            new TradeContinuousSearchService(
                provider);

        TradeContinuousSearchRequest request =
            Request() with
            {
                RecentMarketIds =
                    new long[] { 2 }
            };

        IReadOnlyList<TradeContinuousPlan> plans =
            await service.SearchAsync(
                request);

        TradeContinuousPlan returning =
            Assert.Single(
                plans.Where(plan =>
                    plan.First.Target.MarketId
                    == 2));

        Assert.True(
            returning.FirstBacktracks);

        Assert.True(
            returning.PlanningFactor < 1d);

        Assert.True(
            returning.ProfitPerHour
            >= returning.RankingProfitPerHour);
    }

    [Fact]
    public async Task LookaheadBudgetUsesCreditsAfterFirstSale()
    {
        FakeProvider provider =
            ScenarioProvider(
                secondBuyPrice:
                    300);

        var service =
            new TradeContinuousSearchService(
                provider);

        TradeContinuousSearchRequest request =
            Request() with
            {
                Constraints =
                    Constraints() with
                    {
                        AvailableCredits =
                            1_000
                    }
            };

        IReadOnlyList<TradeContinuousPlan> plans =
            await service.SearchAsync(
                request);

        TradeContinuousPlan viaC =
            Assert.Single(
                plans.Where(plan =>
                    plan.First.Target.MarketId
                    == 2));

        Assert.Equal(
            2_200L,
            viaC.CreditsAfterFirst);

        Assert.NotNull(
            viaC.Lookahead);

        Assert.Equal(
            7,
            viaC.Lookahead!.TradableAmount);
    }

    [Fact]
    public async Task SecondHopConfidenceAgesByEta()
    {
        FakeProvider provider =
            ScenarioProvider();

        var service =
            new TradeContinuousSearchService(
                provider);

        TradeContinuousPlan plan =
            (await service.SearchAsync(
                Request()))
            .First(item =>
                item.Lookahead is not null);

        Assert.True(
            plan.FirstTravel.TotalTime
            > TimeSpan.Zero);

        Assert.True(
            plan.EffectiveWorstDataAge
            > plan.Lookahead!.WorstDataAge);
    }

    [Fact]
    public async Task CurrentMarketSnapshotOverridesRemoteSourcePriceAndStock()
    {
        FakeProvider provider =
            ScenarioProvider();

        var service =
            new TradeContinuousSearchService(
                provider);

        GameStateSnapshot ship =
            Ship() with
            {
                MarketSnapshotId =
                    1,
                MarketUpdatedUtc =
                    DateTimeOffset.UtcNow,
                MarketByCommodityId =
                    new Dictionary<string, MarketItemSnapshot>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["x"] =
                            new MarketItemSnapshot(
                                "x",
                                BuyPrice:
                                    150,
                                SellPrice:
                                    0,
                                Supply:
                                    5,
                                Demand:
                                    0)
                    }
            };

        TradeContinuousSearchRequest request =
            Request() with
            {
                Ship =
                    ship
            };

        IReadOnlyList<TradeContinuousPlan> plans =
            await service.SearchAsync(
                request);

        TradeContinuousPlan x =
            Assert.Single(
                plans.Where(plan =>
                    plan.First.Source.CommodityName
                    == "x"));

        Assert.Equal(
            150,
            x.First.Source.BuyFromStationPrice);

        Assert.Equal(
            5L,
            x.First.Source.Stock);

        Assert.Equal(
            5,
            x.First.TradableAmount);
    }

    [Fact]
    public void WorkspaceExposesContinuousModeAndFirstHopOnlyPin()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        string continuous =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.Continuous.cs");

        string roundTrip =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.RoundTrip.cs");

        Assert.Contains(
            "Tag=\"continuous\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "PrepareContinuousPin(",
            continuous,
            StringComparison.Ordinal);

        string compactRoundTrip =
            RemoveWhitespace(
                roundTrip);

        Assert.Contains(
            "PrepareContinuousPin(plan);PinRequested?.Invoke(plan.First);",
            compactRoundTrip,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveHudHasRollingPreviewAndArrivalRevalidation()
    {
        string active =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.ActiveTrade.cs");

        string continuous =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.Continuous.cs");

        Assert.Contains(
            "UpdateContinuousPlanningForActiveTrade(",
            active,
            StringComparison.Ordinal);

        Assert.Contains(
            "continuationPreviewFinalized",
            continuous,
            StringComparison.Ordinal);

        Assert.Contains(
            "currentJournal.MarketUpdatedUtc",
            continuous,
            StringComparison.Ordinal);

        Assert.Contains(
            "HandleCompletedContinuousActionAsync",
            continuous,
            StringComparison.Ordinal);

        Assert.Contains(
            "ShowContinuationOptionsFull",
            continuous,
            StringComparison.Ordinal);
    }

    private static TradeContinuousSearchRequest Request() =>
        new()
        {
            StartSystem =
                new TradeSystemReference(
                    "B",
                    100),
            KnownStartLocation =
                new TradeSystemLocation(
                    100,
                    "B",
                    0,
                    0,
                    0),
            StartMarketId =
                1,
            Constraints =
                Constraints(),
            Ship =
                Ship(),
            RecentMarketIds =
                Array.Empty<long>()
        };

    private static TradeSearchConstraints Constraints() =>
        new()
        {
            OriginSystemName =
                "B",
            OriginSystemAddress =
                100,
            CargoCapacity =
                10,
            AvailableCredits =
                100_000,
            DiversifyCandidatePool =
                true,
            SourceSearchRadiusLy =
                0,
            TargetSearchRadiusLy =
                80,
            MaxDataAge =
                TimeSpan.FromDays(
                    3),
            MinLandingPadSize =
                1,
            MinSupply =
                1,
            MinDemand =
                1,
            MaxResults =
                100,
            MaxConcurrentCommoditySearches =
                6
        };

    private static GameStateSnapshot Ship() =>
        new()
        {
            JournalAvailable =
                true,
            StarSystem =
                "B",
            SystemAddress =
                100,
            CargoCapacity =
                10,
            MaxJumpRangeLy =
                30,
            UnladenMassTonnes =
                300
        };

    private static FakeProvider ScenarioProvider(
        int secondBuyPrice = 100)
    {
        DateTimeOffset updated =
            DateTimeOffset.UtcNow
            - TimeSpan.FromMinutes(
                5);

        TradeMarketOrder bX =
            Export(
                "x",
                1,
                100,
                "B",
                "B Station",
                0,
                100,
                1_000,
                updated);

        TradeMarketOrder bZ =
            Export(
                "z",
                1,
                100,
                "B",
                "B Station",
                0,
                100,
                1_000,
                updated);

        TradeMarketOrder wrongStation =
            Export(
                "x",
                99,
                100,
                "B",
                "Wrong Station",
                0,
                1,
                1_000,
                updated);

        TradeMarketOrder cY =
            Export(
                "y",
                2,
                200,
                "C",
                "C Station",
                10,
                secondBuyPrice,
                1_000,
                updated);

        TradeMarketOrder eW =
            Export(
                "w",
                3,
                300,
                "E",
                "E Station",
                12,
                100,
                1_000,
                updated);

        var exports =
            new Dictionary<long, IReadOnlyList<TradeMarketOrder>>
            {
                [100] =
                    new[] { bX, bZ, wrongStation },
                [200] =
                    new[] { cY },
                [300] =
                    new[] { eW },
                [400] =
                    Array.Empty<TradeMarketOrder>(),
                [500] =
                    Array.Empty<TradeMarketOrder>()
            };

        var imports =
            new Dictionary<string, IReadOnlyList<TradeMarketOrder>>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["100:x"] =
                    new[]
                    {
                        Import(
                            "x",
                            2,
                            200,
                            "C",
                            "C Station",
                            10,
                            220,
                            1_000,
                            updated)
                    },
                ["100:z"] =
                    new[]
                    {
                        Import(
                            "z",
                            3,
                            300,
                            "E",
                            "E Station",
                            12,
                            230,
                            1_000,
                            updated)
                    },
                ["200:y"] =
                    new[]
                    {
                        Import(
                            "y",
                            4,
                            400,
                            "D",
                            "D Station",
                            20,
                            400,
                            1_000,
                            updated)
                    },
                ["300:w"] =
                    new[]
                    {
                        Import(
                            "w",
                            5,
                            500,
                            "F",
                            "F Station",
                            22,
                            120,
                            1_000,
                            updated)
                    }
            };

        TradeCommoditySummary[] summaries =
        {
            new(
                "x",
                100,
                220,
                10_000,
                10_000),
            new(
                "z",
                100,
                230,
                10_000,
                10_000),
            new(
                "y",
                secondBuyPrice,
                400,
                10_000,
                10_000),
            new(
                "w",
                100,
                120,
                10_000,
                10_000)
        };

        return new FakeProvider(
            exports,
            imports,
            summaries);
    }

    private static TradeMarketOrder Export(
        string commodity,
        long market,
        long system,
        string systemName,
        string station,
        double x,
        int buy,
        long stock,
        DateTimeOffset updated) =>
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
                system,
            SystemName =
                systemName,
            SystemX =
                x,
            BuyFromStationPrice =
                buy,
            Stock =
                stock,
            UpdatedAt =
                updated
        };

    private static TradeMarketOrder Import(
        string commodity,
        long market,
        long system,
        string systemName,
        string station,
        double x,
        int sell,
        long demand,
        DateTimeOffset updated) =>
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
                system,
            SystemName =
                systemName,
            SystemX =
                x,
            SellToStationPrice =
                sell,
            Demand =
                demand,
            UpdatedAt =
                updated
        };

    private sealed class FakeProvider :
        ITradeDataProvider,
        ITradeSystemTradeSidesProvider
    {
        private readonly IReadOnlyDictionary<long, IReadOnlyList<TradeMarketOrder>> exports;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<TradeMarketOrder>> imports;
        private readonly IReadOnlyList<TradeCommoditySummary> summaries;

        public FakeProvider(
            IReadOnlyDictionary<long, IReadOnlyList<TradeMarketOrder>> exports,
            IReadOnlyDictionary<string, IReadOnlyList<TradeMarketOrder>> imports,
            IReadOnlyList<TradeCommoditySummary> summaries)
        {
            this.exports =
                exports;

            this.imports =
                imports;

            this.summaries =
                summaries;
        }

        public string Name =>
            "fake";

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
            Task.FromResult(
                summaries);

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
            CancellationToken cancellationToken = default)
        {
            string key =
                $"{system.SystemAddress}:{commodityName}";

            return Task.FromResult(
                imports.TryGetValue(
                    key,
                    out IReadOnlyList<TradeMarketOrder>? rows)
                    ? rows
                    : (IReadOnlyList<TradeMarketOrder>)Array.Empty<TradeMarketOrder>());
        }

        public Task<IReadOnlyList<TradeMarketOrder>> GetSystemExportsAsync(
            TradeSystemLocation system,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                exports.TryGetValue(
                    system.SystemAddress,
                    out IReadOnlyList<TradeMarketOrder>? rows)
                    ? rows
                    : (IReadOnlyList<TradeMarketOrder>)Array.Empty<TradeMarketOrder>());

        public Task<IReadOnlyList<TradeMarketOrder>> GetSystemImportsAsync(
            TradeSystemLocation system,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TradeMarketOrder>>(
                Array.Empty<TradeMarketOrder>());
    }

    private static string RemoveWhitespace(
        string value) =>
        string.Concat(
            value.Where(character =>
                !char.IsWhiteSpace(character)));

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
                directory.FullName;

            foreach (string part in relative)
            {
                candidate =
                    Path.Combine(
                        candidate,
                        part);
            }

            if (File.Exists(candidate))
            {
                return File.ReadAllText(
                    candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
