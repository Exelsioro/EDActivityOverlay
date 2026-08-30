using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeV8Tests
{
    private static readonly DateTimeOffset Now =
        new(
            2026,
            8,
            30,
            12,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public void CommanderBudgetCapsTradableAmount()
    {
        TradeSearchConstraints constraints =
            Constraints() with
            {
                CargoCapacity = 100,
                AvailableCredits = 25_000
            };

        TradeRouteCandidate candidate =
            Assert.Single(
                TradeRouteEngine.BuildOneWayCandidates(
                    new TradeSystemLocation(
                        1,
                        "Origin",
                        0,
                        0,
                        0),
                    [Order(10, "Source", 10, 1_000, 0, 1_000, 0)],
                    [Order(20, "Target", 20, 0, 2_000, 0, 1_000)],
                    constraints,
                    Now));

        Assert.Equal(25, candidate.TradableAmount);
        Assert.Equal(25_000, candidate.ProfitPerTrip);
    }

    [Fact]
    public void DiversifiedPoolRetainsShortAlternativeBeforeUiTimeRanking()
    {
        TradeSearchConstraints constraints =
            Constraints() with
            {
                DiversifyCandidatePool = true,
                TargetSearchRadiusLy = 100
            };

        TradeMarketOrder source =
            Order(10, "Source", 0, 1_000, 0, 100_000, 0);

        TradeMarketOrder far =
            Order(20, "Far", 90, 0, 10_000, 0, 100_000);

        TradeMarketOrder medium =
            Order(21, "Medium", 70, 0, 9_000, 0, 100_000);

        TradeMarketOrder near =
            Order(22, "Near", 5, 0, 4_000, 0, 100_000);

        IReadOnlyList<TradeRouteCandidate> routes =
            TradeRouteEngine.BuildOneWayCandidates(
                new TradeSystemLocation(1, "Origin", 0, 0, 0),
                [source],
                [far, medium, near],
                constraints,
                Now,
                maxResults: 2);

        Assert.Equal(2, routes.Count);
        Assert.Contains(routes, item => item.Target.MarketId == far.MarketId);
        Assert.Contains(routes, item => item.Target.MarketId == near.MarketId);
    }

    [Fact]
    public void FirstRunTravelIncludesEntryToSupplier()
    {
        var candidate =
            new TradeRouteCandidate
            {
                Source = Order(10, "Source", 20, 1_000, 0, 1_000, 0),
                Target = Order(20, "Target", 60, 0, 2_000, 0, 1_000),
                ProfitPerTon = 1_000,
                TradableAmount = 100,
                ProfitPerTrip = 100_000,
                OriginToSourceDistanceLy = 20,
                SourceToTargetDistanceLy = 40,
                SourceAge = TimeSpan.FromHours(1),
                TargetAge = TimeSpan.FromHours(1)
            };

        var ship =
            new GameStateSnapshot
            {
                MaxJumpRangeLy = 30,
                UnladenMassTonnes = 300
            };

        TradeRouteTravelEstimate estimate =
            new TradeTravelTimeEstimator()
                .EstimateOneWay(candidate, ship);

        Assert.NotNull(estimate.Entry);
        Assert.True(estimate.FirstRunTime > estimate.OneWayTime);
        Assert.True(
            estimate.FirstRunEstimatedJumps
            > estimate.Outbound.EstimatedJumps);
    }

    [Fact]
    public void ProductionWorkspaceUsesJournalBalanceAndDiversifiedPool()
    {
        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml.cs");

        Assert.Contains("AvailableCredits =", code, StringComparison.Ordinal);
        Assert.Contains("currentJournal.Balance", code, StringComparison.Ordinal);
        Assert.Contains("DiversifyCandidatePool =", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AdvancedFilterToggleIsSmallAndInline()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        int start = xaml.IndexOf(
            "x:Name=\"AdvancedFiltersButton\"",
            StringComparison.Ordinal);

        Assert.True(start >= 0);

        int end = xaml.IndexOf(
            "</Button>",
            start,
            StringComparison.Ordinal);

        Assert.True(end > start);

        string button = xaml[start..end];

        Assert.Contains("Width=\"108\"", button, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.ColumnSpan=\"11\"", button, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalContentAlignment=\"Stretch\"", button, StringComparison.Ordinal);
    }

    private static TradeSearchConstraints Constraints() =>
        new()
        {
            OriginSystemName = "Origin",
            CargoCapacity = 100,
            SourceSearchRadiusLy = 30,
            TargetSearchRadiusLy = 100,
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
        long demand) =>
        new()
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

    private static string ReadProjectFile(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
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
            string.Join(Path.DirectorySeparatorChar, relative));
    }
}