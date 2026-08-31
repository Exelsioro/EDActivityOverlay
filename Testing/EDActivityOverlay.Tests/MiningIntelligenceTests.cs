using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class MiningIntelligenceTests
{
    [Fact]
    public void CollectorEstimatorUsesStandardControllerCapacity()
    {
        MiningLoadoutSnapshot loadout = BuildLoadout(
            new MiningLoadoutModuleSnapshot(
                "Slot03_Size5",
                "int_dronecontrol_collection_size5_class5",
                MiningModuleKind.CollectorController,
                5,
                "A",
                true));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        MiningCollectorActivitySnapshot result =
            MiningCollectorEstimator.Calculate(
                loadout,
                [
                    now.AddMinutes(-2),
                    now.AddMinutes(-1)
                ],
                now);

        Assert.True(result.Available);
        Assert.Equal(3, result.Capacity);
        Assert.Equal(2, result.EstimatedActive);
        Assert.Equal(1, result.TopUpRecommended);
    }

    [Fact]
    public void CollectorEstimatorRecognizesMkTwoMiningMulti()
    {
        MiningLoadoutSnapshot loadout = BuildLoadout(
            new MiningLoadoutModuleSnapshot(
                "Slot05_Size5",
                "int_multidronecontrol_miningv2_size5_class5",
                MiningModuleKind.MiningMultiLimpetController,
                5,
                "A",
                true));

        MiningCollectorActivitySnapshot result =
            MiningCollectorEstimator.Calculate(
                loadout,
                Array.Empty<DateTimeOffset>(),
                DateTimeOffset.UtcNow);

        Assert.Equal(14, result.Capacity);
        Assert.Equal(14, result.TopUpRecommended);
        Assert.Equal(TimeSpan.FromMinutes(15), result.AssumedLifetime);
    }

    [Fact]
    public void AdaptiveThresholdWaitsForEnoughProspects()
    {
        MiningSessionSnapshot session = BuildSession(
            prospects: Enumerable.Range(1, 8)
                .Select(index => Prospect(index, 30))
                .ToArray());

        MiningAdaptiveThresholdAdvice advice =
            MiningIntelligenceCalculator.CalculateAdaptiveThreshold(
                session,
                "Platinum",
                25);

        Assert.False(advice.Ready);
        Assert.Equal(25, advice.Suggested);
    }

    [Fact]
    public void AdaptiveThresholdBecomesStricterInRichField()
    {
        MiningSessionSnapshot session = BuildSession(
            prospects: Enumerable.Range(1, 16)
                .Select(index => Prospect(index, 38))
                .ToArray(),
            cargoUsed: 90,
            cargoCapacity: 100);

        MiningAdaptiveThresholdAdvice advice =
            MiningIntelligenceCalculator.CalculateAdaptiveThreshold(
                session,
                "Platinum",
                25);

        Assert.True(advice.Ready);
        Assert.True(advice.Suggested > 25);
    }

    [Fact]
    public void LimpetManagerDetectsSafeExcessAfterWarmup()
    {
        MiningSessionSnapshot session = BuildSession(
            refinements: Enumerable.Range(1, 20)
                .Select(index => new MiningRefinementSnapshot(
                    index,
                    DateTimeOffset.UtcNow.AddSeconds(index),
                    "platinum",
                    "Platinum"))
                .ToArray(),
            prospectorsLaunched: 3,
            collectorsLaunched: 5,
            cargoUsed: 90,
            cargoCapacity: 128,
            limpets: 50);

        MiningLimpetAdvice advice =
            MiningIntelligenceCalculator.CalculateLimpets(session);

        Assert.True(advice.Ready);
        Assert.True(advice.EstimatedRequired > 0);
        Assert.True(advice.SafeExcess > 0);
        Assert.False(advice.Critical);
    }

    [Fact]
    public void FieldQualityDetectsRecentDrop()
    {
        MiningProspectSnapshot[] prospects =
        [
            .. Enumerable.Range(1, 12)
                .Select(index => Prospect(index, 32)),
            .. Enumerable.Range(13, 12)
                .Select(index => Prospect(index, 0, includeTarget: false))
        ];

        MiningSessionSnapshot session = BuildSession(prospects: prospects);

        MiningFieldQuality quality =
            MiningIntelligenceCalculator.CalculateFieldQuality(
                session,
                "Platinum",
                25);

        Assert.Equal(MiningFieldQuality.Declining, quality);
    }

    [Fact]
    public void LeaveAdvisorPromotesFinishCurrentRockNearFull()
    {
        MiningSessionSnapshot session = BuildSession(
            cargoUsed: 97,
            cargoCapacity: 100,
            limpets: 5);

        MiningLeaveAdvice advice =
            MiningIntelligenceCalculator.CalculateLeave(
                session,
                TimeSpan.FromMinutes(6));

        Assert.Equal(
            MiningLeaveRecommendation.FinishCurrentRock,
            advice.Recommendation);
    }

    private static MiningProspectSnapshot Prospect(
        int sequence,
        double proportion,
        bool includeTarget = true) =>
        new(
            sequence,
            DateTimeOffset.UtcNow.AddSeconds(sequence),
            "High",
            100,
            string.Empty,
            string.Empty,
            includeTarget
                ? [
                    new MiningProspectMaterialSnapshot(
                        "platinum",
                        "Platinum",
                        proportion)
                ]
                : [
                    new MiningProspectMaterialSnapshot(
                        "silver",
                        "Silver",
                        20)
                ]);

    private static MiningSessionSnapshot BuildSession(
        IReadOnlyList<MiningProspectSnapshot>? prospects = null,
        IReadOnlyList<MiningRefinementSnapshot>? refinements = null,
        int prospectorsLaunched = 0,
        int collectorsLaunched = 0,
        int cargoUsed = 0,
        int cargoCapacity = 256,
        int limpets = 0) =>
        new(
            Guid.NewGuid(),
            MiningSessionState.Active,
            DateTimeOffset.UtcNow.AddMinutes(-20),
            DateTimeOffset.UtcNow,
            null,
            MiningSessionEndReason.None,
            "CMDR",
            1,
            "Test",
            1,
            "Test 1",
            "Test 1 A Ring",
            prospectorsLaunched,
            collectorsLaunched,
            0,
            cargoUsed,
            cargoCapacity,
            limpets,
            prospects ?? Array.Empty<MiningProspectSnapshot>(),
            refinements ?? Array.Empty<MiningRefinementSnapshot>());

    private static MiningLoadoutSnapshot BuildLoadout(
        params MiningLoadoutModuleSnapshot[] modules) =>
        new(
            true,
            "Test",
            modules,
            true,
            "A",
            true,
            true,
            true,
            false,
            MiningModeReadiness.Unknown(MiningLoadoutMode.Laser),
            MiningModeReadiness.Unknown(MiningLoadoutMode.Core),
            MiningModeReadiness.Unknown(MiningLoadoutMode.Subsurface),
            MiningModeReadiness.Unknown(MiningLoadoutMode.Surface));
}
