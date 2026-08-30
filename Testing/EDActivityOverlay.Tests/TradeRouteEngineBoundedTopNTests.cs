using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeRouteEngineBoundedTopNTests
{
    private static readonly DateTimeOffset Now =
        new(
            2026,
            8,
            29,
            12,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public void BoundedTopNMatchesPrefixOfFullRanking()
    {
        TradeSystemLocation origin =
            new(
                1,
                "Origin",
                0,
                0,
                0);

        TradeSearchConstraints constraints =
            new()
            {
                OriginSystemName =
                    "Origin",
                CargoCapacity =
                    100,
                SourceSearchRadiusLy =
                    100,
                TargetSearchRadiusLy =
                    100,
                MaxDataAge =
                    TimeSpan.FromDays(
                        3),
                MinLandingPadSize =
                    1
            };

        TradeMarketOrder[] sources =
            Enumerable.Range(
                    1,
                    40)
                .Select(
                    index =>
                        Source(
                            index,
                            buy:
                                1_000
                                + index,
                            x:
                                index))
                .ToArray();

        TradeMarketOrder[] targets =
            Enumerable.Range(
                    1,
                    40)
                .Select(
                    index =>
                        Target(
                            1_000
                            + index,
                            sell:
                                5_000
                                - index,
                            x:
                                50
                                + index / 10d))
                .ToArray();

        IReadOnlyList<TradeRouteCandidate> full =
            TradeRouteEngine.BuildOneWayCandidates(
                origin,
                sources,
                targets,
                constraints,
                Now);

        IReadOnlyList<TradeRouteCandidate> bounded =
            TradeRouteEngine.BuildOneWayCandidates(
                origin,
                sources,
                targets,
                constraints,
                Now,
                maxResults: 25);

        Assert.Equal(
            25,
            bounded.Count);

        Assert.Equal(
            full.Take(
                    25)
                .Select(
                    Identity),
            bounded.Select(
                Identity));
    }

    [Fact]
    public void BoundedTopNDoesNotReturnMoreThanRequested()
    {
        TradeSystemLocation origin =
            new(
                1,
                "Origin",
                0,
                0,
                0);

        TradeSearchConstraints constraints =
            new()
            {
                OriginSystemName =
                    "Origin",
                CargoCapacity =
                    750,
                SourceSearchRadiusLy =
                    100,
                TargetSearchRadiusLy =
                    80,
                MaxDataAge =
                    TimeSpan.FromDays(
                        3),
                MinLandingPadSize =
                    1
            };

        TradeMarketOrder[] sources =
            Enumerable.Range(
                    1,
                    100)
                .Select(
                    index =>
                        Source(
                            index,
                            buy:
                                1_000
                                + index,
                            x:
                                index / 2d))
                .ToArray();

        TradeMarketOrder[] targets =
            Enumerable.Range(
                    1,
                    100)
                .Select(
                    index =>
                        Target(
                            1_000
                            + index,
                            sell:
                                10_000
                                - index,
                            x:
                                40
                                + index / 3d))
                .ToArray();

        IReadOnlyList<TradeRouteCandidate> routes =
            TradeRouteEngine.BuildOneWayCandidates(
                origin,
                sources,
                targets,
                constraints,
                Now,
                maxResults: 50);

        Assert.Equal(
            50,
            routes.Count);
    }

    private static string Identity(
        TradeRouteCandidate route) =>
        $"{route.Source.MarketId}:{route.Target.MarketId}:{route.ProfitPerTrip}:{route.ProfitPerTon}:{route.TotalTravelDistanceLy:F8}";

    private static TradeMarketOrder Source(
        int market,
        int buy,
        double x) =>
        new()
        {
            CommodityName =
                "gold",
            MarketId =
                market,
            StationName =
                $"S{market}",
            StationType =
                "Coriolis",
            DistanceToArrivalLs =
                100,
            MaxLandingPadSize =
                3,
            SystemAddress =
                market,
            SystemName =
                $"Source {market}",
            SystemX =
                x,
            BuyFromStationPrice =
                buy,
            SellToStationPrice =
                0,
            Stock =
                100_000,
            UpdatedAt =
                Now
                - TimeSpan.FromHours(
                    1)
        };

    private static TradeMarketOrder Target(
        int market,
        int sell,
        double x) =>
        new()
        {
            CommodityName =
                "gold",
            MarketId =
                market,
            StationName =
                $"T{market}",
            StationType =
                "Orbis",
            DistanceToArrivalLs =
                100,
            MaxLandingPadSize =
                3,
            SystemAddress =
                market,
            SystemName =
                $"Target {market}",
            SystemX =
                x,
            BuyFromStationPrice =
                0,
            SellToStationPrice =
                sell,
            Demand =
                100_000,
            UpdatedAt =
                Now
                - TimeSpan.FromHours(
                    1)
        };
}
