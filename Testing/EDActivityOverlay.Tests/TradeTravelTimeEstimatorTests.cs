using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeTravelTimeEstimatorTests
{
    private readonly TradeTravelTimeEstimator estimator =
        new();

    [Theory]
    [InlineData(1_000, 60)]
    [InlineData(10_000, 170)]
    [InlineData(100_000, 385)]
    [InlineData(1_000_000, 1_165)]
    [InlineData(3_000_000, 2_370)]
    public void SupercruiseCurveKeepsReferenceAnchors(
        double distanceLs,
        double expectedSeconds)
    {
        TimeSpan estimate =
            TradeTravelTimeEstimator.EstimateSupercruiseTime(
                distanceLs);

        Assert.InRange(
            estimate.TotalSeconds,
            expectedSeconds - 0.01,
            expectedSeconds + 0.01);
    }

    [Fact]
    public void CargoReducesJournalProvidedJumpRange()
    {
        var ship =
            new GameStateSnapshot
            {
                MaxJumpRangeLy =
                    30,
                UnladenMassTonnes =
                    300
            };

        double empty =
            estimator.EstimateLoadedJumpRangeLy(
                ship,
                cargoTons:
                    0);

        double loaded =
            estimator.EstimateLoadedJumpRangeLy(
                ship,
                cargoTons:
                    300);

        Assert.Equal(
            30,
            empty,
            8);

        Assert.Equal(
            15,
            loaded,
            8);

        Assert.Equal(
            3,
            TradeTravelTimeEstimator.EstimateJumpCount(
                45,
                loaded));
    }

    [Fact]
    public void LongInSystemCruiseCanDominateSameInterstellarLeg()
    {
        GameStateSnapshot ship =
            Ship();

        TradeRouteCandidate near =
            OneWay(
                targetDistanceLs:
                    1_000);

        TradeRouteCandidate far =
            OneWay(
                targetDistanceLs:
                    100_000);

        TradeRouteTravelEstimate nearEstimate =
            estimator.EstimateOneWay(
                near,
                ship);

        TradeRouteTravelEstimate farEstimate =
            estimator.EstimateOneWay(
                far,
                ship);

        Assert.Equal(
            nearEstimate.Outbound.EstimatedJumps,
            farEstimate.Outbound.EstimatedJumps);

        Assert.True(
            farEstimate.OneWayTime
            > nearEstimate.OneWayTime
              + TimeSpan.FromMinutes(
                  5));
    }

    [Fact]
    public void RoundTripUsesBothStationCruisesAndIndependentCargoLoads()
    {
        GameStateSnapshot ship =
            Ship();

        TradeRoundTripCandidate roundTrip =
            RoundTrip();

        TradeRouteTravelEstimate estimate =
            estimator.EstimateRoundTrip(
                roundTrip,
                ship);

        Assert.NotNull(
            estimate.Return);

        Assert.Equal(
            750,
            estimate.Outbound.CargoTons);

        Assert.Equal(
            250,
            estimate.Return!.CargoTons);

        Assert.True(
            estimate.Outbound.LoadedJumpRangeLy
            < estimate.Return.LoadedJumpRangeLy);

        Assert.Equal(
            100_000,
            estimate.Outbound.StationDistanceLs);

        Assert.Equal(
            1_000,
            estimate.Return.StationDistanceLs);

        Assert.Equal(
            estimate.Outbound.TotalTime
            + estimate.Return.TotalTime,
            estimate.CycleTime);
    }

    [Fact]
    public void ProfitPerHourUsesEstimatedWholeCycleTime()
    {
        TradeRouteTravelEstimate estimate =
            estimator.EstimateRoundTrip(
                RoundTrip(),
                Ship());

        long perHour =
            estimate.ProfitPerHour(
                40_000_000);

        Assert.True(
            perHour > 0);

        Assert.Equal(
            (long)Math.Round(
                40_000_000
                * 3600d
                / estimate.CycleTime.TotalSeconds),
            perHour);
    }

    [Fact]
    public void MissingLoadoutMarksEstimateUnavailableForRanking()
    {
        TradeRouteTravelEstimate estimate =
            estimator.EstimateOneWay(
                OneWay(
                    1_000),
                new GameStateSnapshot());

        Assert.Equal(
            TradeTravelEstimateConfidence.Unavailable,
            estimate.Confidence);
    }

    private static GameStateSnapshot Ship() =>
        new()
        {
            MaxJumpRangeLy =
                30,
            UnladenMassTonnes =
                300
        };

    private static TradeRouteCandidate OneWay(
        double targetDistanceLs) =>
        new()
        {
            Source =
                Order(
                    "gold",
                    10,
                    "A",
                    "System A",
                    distanceLs:
                        1_000),
            Target =
                Order(
                    "gold",
                    20,
                    "B",
                    "System B",
                    distanceLs:
                        targetDistanceLs),
            ProfitPerTon =
                50_000,
            TradableAmount =
                300,
            ProfitPerTrip =
                15_000_000,
            OriginToSourceDistanceLy =
                10,
            SourceToTargetDistanceLy =
                45,
            SourceAge =
                TimeSpan.FromHours(
                    1),
            TargetAge =
                TimeSpan.FromHours(
                    1)
        };

    private static TradeRoundTripCandidate RoundTrip()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        TradeRouteCandidate outbound =
            OneWay(
                100_000)
            with
            {
                Source =
                    Order(
                        "gold",
                        10,
                        "A",
                        "System A",
                        distanceLs:
                            1_000),
                Target =
                    Order(
                        "gold",
                        20,
                        "B",
                        "System B",
                        distanceLs:
                            100_000),
                TradableAmount =
                    750,
                ProfitPerTrip =
                    37_500_000
            };

        return
            new TradeRoundTripCandidate
            {
                Outbound =
                    outbound,
                ReturnSource =
                    Order(
                        "silver",
                        20,
                        "B",
                        "System B",
                        distanceLs:
                            100_000),
                ReturnTarget =
                    Order(
                        "silver",
                        10,
                        "A",
                        "System A",
                        distanceLs:
                            1_000),
                ReturnProfitPerTon =
                    2_000,
                ReturnTradableAmount =
                    250,
                ReturnProfitPerTrip =
                    500_000,
                ReturnSourceAge =
                    TimeSpan.FromHours(
                        1),
                ReturnTargetAge =
                    TimeSpan.FromHours(
                        1)
            };
    }

    private static TradeMarketOrder Order(
        string commodity,
        long market,
        string station,
        string system,
        double distanceLs) =>
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
                distanceLs,
            MaxLandingPadSize =
                3,
            SystemAddress =
                market == 10
                    ? 100
                    : 200,
            SystemName =
                system,
            BuyFromStationPrice =
                market == 10
                    ? 1_000
                    : 500,
            SellToStationPrice =
                market == 20
                    ? 51_000
                    : 2_500,
            Stock =
                10_000,
            Demand =
                10_000,
            UpdatedAt =
                DateTimeOffset.UtcNow
        };
}
