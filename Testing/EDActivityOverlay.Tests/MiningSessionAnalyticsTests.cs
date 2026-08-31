using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class MiningSessionAnalyticsTests
{
    [Fact]
    public void RateStaysInWarmupBeforeMinimumDurationAndTonnage()
    {
        MiningSessionSnapshot session = Session(
            TimeSpan.FromMinutes(4),
            refined: 4,
            cargoUsed: 4,
            cargoCapacity: 20);

        MiningSessionAnalyticsSnapshot analytics =
            MiningSessionAnalyticsCalculator.Calculate(
                session,
                "Platinum",
                20,
                session.StartedUtc + TimeSpan.FromMinutes(4));

        Assert.False(analytics.RateReady);
        Assert.Equal(0, analytics.TonsPerHour);
        Assert.Null(analytics.EstimatedTimeToFull);
    }

    [Fact]
    public void StableRateAndEtaUseWallClockSessionTime()
    {
        MiningSessionSnapshot session = Session(
            TimeSpan.FromMinutes(10),
            refined: 10,
            cargoUsed: 10,
            cargoCapacity: 20);

        MiningSessionAnalyticsSnapshot analytics =
            MiningSessionAnalyticsCalculator.Calculate(
                session,
                "Platinum",
                20,
                session.StartedUtc + TimeSpan.FromMinutes(10));

        Assert.True(analytics.RateReady);
        Assert.Equal(60, analytics.TonsPerHour, 6);
        Assert.NotNull(analytics.EstimatedTimeToFull);
        Assert.Equal(10, analytics.EstimatedTimeToFull!.Value.TotalMinutes, 6);
    }

    [Fact]
    public void TargetDistributionAndP75AreDerivedFromStoredProspects()
    {
        MiningSessionSnapshot session = Session(
            TimeSpan.FromMinutes(10),
            refined: 10,
            cargoUsed: 10,
            cargoCapacity: 20,
            targetValues: [5, 15, 25, 35, 55]);

        MiningSessionAnalyticsSnapshot analytics =
            MiningSessionAnalyticsCalculator.Calculate(
                session,
                "Platinum",
                20,
                session.StartedUtc + TimeSpan.FromMinutes(10));

        Assert.Equal(35, analytics.TargetP75, 6);
        Assert.Equal(5, analytics.Target.TargetBearing);
        Assert.Equal(3, analytics.Target.Accepted);
        Assert.Equal(1, analytics.YieldBuckets[0].Count);
        Assert.Equal(1, analytics.YieldBuckets[1].Count);
        Assert.Equal(1, analytics.YieldBuckets[2].Count);
        Assert.Equal(1, analytics.YieldBuckets[3].Count);
        Assert.Equal(1, analytics.YieldBuckets[5].Count);
    }

    [Fact]
    public void HistoryUsesWeightedTimeAndRejectsInflatedShortBestRates()
    {
        MiningSessionSnapshot shortSession = Session(
            TimeSpan.FromMinutes(1),
            refined: 4,
            cargoUsed: 4,
            cargoCapacity: 20) with
        {
            State = MiningSessionState.Finished,
            EndedUtc = Start + TimeSpan.FromMinutes(1)
        };
        MiningSessionSnapshot normalSession = Session(
            TimeSpan.FromMinutes(30),
            refined: 30,
            cargoUsed: 20,
            cargoCapacity: 20) with
        {
            State = MiningSessionState.Finished,
            EndedUtc = Start + TimeSpan.FromMinutes(30),
            SystemName = "Best System",
            RingName = "A Ring"
        };

        MiningHistoryAnalyticsSnapshot history =
            MiningSessionAnalyticsCalculator.CalculateHistory(
                [shortSession, normalSession],
                "Platinum",
                20);

        Assert.Equal(2, history.Sessions);
        Assert.Equal(34, history.RefinedTons);
        Assert.Equal(60, history.BestTonsPerHour, 6);
        Assert.Contains("Best System", history.BestLocation);
    }

    private static readonly DateTimeOffset Start =
        new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);

    private static MiningSessionSnapshot Session(
        TimeSpan duration,
        int refined,
        int cargoUsed,
        int cargoCapacity,
        double[]? targetValues = null)
    {
        targetValues ??= [25];
        MiningProspectSnapshot[] prospects = targetValues
            .Select((value, index) => new MiningProspectSnapshot(
                index + 1,
                Start + TimeSpan.FromSeconds(index),
                "High",
                100,
                string.Empty,
                string.Empty,
                [new MiningProspectMaterialSnapshot("platinum", "Platinum", value)]))
            .ToArray();
        MiningRefinementSnapshot[] refinements = Enumerable.Range(1, refined)
            .Select(index => new MiningRefinementSnapshot(
                index,
                Start + TimeSpan.FromSeconds(index * 10),
                "platinum",
                "Platinum"))
            .ToArray();

        return new MiningSessionSnapshot(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            MiningSessionState.Active,
            Start,
            Start + duration,
            null,
            MiningSessionEndReason.None,
            "CMDR Test",
            42,
            "Test System",
            1,
            "Test Body",
            "A Ring",
            prospects.Length,
            2,
            0,
            cargoUsed,
            cargoCapacity,
            10,
            prospects,
            refinements);
    }
}
