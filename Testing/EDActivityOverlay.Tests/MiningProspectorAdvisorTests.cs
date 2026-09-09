using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class MiningProspectorAdvisorTests
{
    [Fact]
    public void MotherlodeTargetIsCoreAndCoreIsRecommendedMethod()
    {
        MiningProspectSnapshot prospect = Prospect(
            1,
            "alexandrite",
            "Alexandrite",
            ("platinum", "Platinum", 18));

        MiningProspectAdvice advice = MiningProspectorAdvisor.Evaluate(
            prospect,
            "Alexandrite",
            25);

        Assert.Equal(MiningProspectDecision.Core, advice.Decision);
        Assert.Equal(MiningExtractionMethod.Core, advice.RecommendedMethod);
        Assert.Equal(MiningExtractionMethod.Core, advice.TargetMethod);
        Assert.True(advice.TargetFound);
        Assert.True(advice.MotherlodeMatches);
    }

    [Fact]
    public void LaserTargetAtOrAboveThresholdIsMine()
    {
        MiningProspectSnapshot prospect = Prospect(
            1,
            ("platinum", "Platinum", 31.4),
            ("osmium", "Osmium", 8.2));

        MiningProspectAdvice advice = MiningProspectorAdvisor.Evaluate(
            prospect,
            "Platinum",
            25);

        Assert.Equal(MiningProspectDecision.Mine, advice.Decision);
        Assert.Equal(MiningExtractionMethod.Laser, advice.RecommendedMethod);
        Assert.Equal(MiningExtractionMethod.Laser, advice.TargetMethod);
        Assert.Equal(31.4, advice.TargetProportion);
    }

    [Fact]
    public void LaserTargetBelowThresholdIsSkip()
    {
        MiningProspectAdvice advice = MiningProspectorAdvisor.Evaluate(
            Prospect(1, ("platinum", "Platinum", 13.7)),
            "Platinum",
            25);

        Assert.Equal(MiningProspectDecision.Skip, advice.Decision);
        Assert.True(advice.TargetFound);
        Assert.Equal(MiningExtractionMethod.Laser, advice.TargetMethod);
    }

    [Fact]
    public void LocalizedDisplayNameCanBeUsedAsTarget()
    {
        MiningProspectAdvice advice = MiningProspectorAdvisor.Evaluate(
            Prospect(1, ("platinum", "Платина", 28.5)),
            "Платина",
            25);

        Assert.Equal(MiningProspectDecision.Mine, advice.Decision);
        Assert.Equal("Платина", advice.MatchedDisplayName);
    }

    [Fact]
    public void CoreAsteroidStillReportsCoreAsBestKnownMethodWhenConfiguredTargetIsAbsent()
    {
        MiningProspectAdvice advice = MiningProspectorAdvisor.Evaluate(
            Prospect(
                1,
                "alexandrite",
                "Alexandrite",
                ("osmium", "Osmium", 12)),
            "Platinum",
            25);

        Assert.Equal(MiningProspectDecision.Skip, advice.Decision);
        Assert.Equal(MiningExtractionMethod.Core, advice.RecommendedMethod);
        Assert.Equal(MiningExtractionMethod.Unknown, advice.TargetMethod);
    }

    [Fact]
    public void TargetAnalyticsUsesRawProspectDistribution()
    {
        MiningSessionSnapshot session = Session(
            Prospect(1, ("platinum", "Platinum", 10)),
            Prospect(2, ("platinum", "Platinum", 30)),
            Prospect(3, ("platinum", "Platinum", 40)));

        MiningTargetStatistics stats = MiningTargetAnalytics.Calculate(
            session,
            "Platinum",
            25);

        Assert.Equal(3, stats.Prospected);
        Assert.Equal(3, stats.TargetBearing);
        Assert.Equal(2, stats.Accepted);
        Assert.Equal(1, stats.HitRate, 6);
        Assert.Equal(2.0 / 3.0, stats.AcceptanceRate, 6);
        Assert.Equal(80.0 / 3.0, stats.AverageProportion, 6);
        Assert.Equal(30, stats.MedianProportion, 6);
        Assert.Equal(40, stats.BestProportion, 6);
    }

    private static MiningProspectSnapshot Prospect(
        int sequence,
        params (string Id, string Name, double Proportion)[] materials) =>
        Prospect(sequence, string.Empty, string.Empty, materials);

    private static MiningProspectSnapshot Prospect(
        int sequence,
        string motherlodeId,
        string motherlodeName,
        params (string Id, string Name, double Proportion)[] materials) =>
        new(
            sequence,
            new DateTimeOffset(2026, 8, 31, 0, sequence, 0, TimeSpan.Zero),
            "High",
            100,
            motherlodeId,
            motherlodeName,
            materials.Select(item => new MiningProspectMaterialSnapshot(
                item.Id,
                item.Name,
                item.Proportion)).ToArray());

    private static MiningSessionSnapshot Session(params MiningProspectSnapshot[] prospects) =>
        new(
            Guid.NewGuid(),
            MiningSessionState.Active,
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 0, 30, 0, TimeSpan.Zero),
            null,
            MiningSessionEndReason.None,
            "CMDR Test",
            42,
            "Test",
            1,
            "Test 1 A Ring",
            "Test 1 A Ring",
            prospects.Length,
            0,
            0,
            10,
            100,
            20,
            prospects,
            Array.Empty<MiningRefinementSnapshot>());
}
