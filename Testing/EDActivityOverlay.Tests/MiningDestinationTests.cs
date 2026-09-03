using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MiningDestinationTests
{
    [Fact]
    public void DestinationPreservesSelectedSystemBodyAndRing()
    {
        var candidate = new MiningLocationCandidate
        {
            SystemName = "Lalande 34968",
            BodyName = "Lalande 34968 AB 8",
            RingName = "Lalande 34968 AB 8 A Ring",
            RingClass = "Metallic",
            ReserveLevel = "Pristine",
            DistanceLy = 38.2,
            DistanceToArrivalLs = 1240,
            PrimaryCommodityId = "Platinum",
            HotspotCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Platinum"] = 1
            },
            SpecialSites =
            [
                new MiningLocationSpecialSite(
                    "Lalande 34968",
                    "Lalande 34968 AB 8 A Ring",
                    "Platinum",
                    0,
                    MiningResSiteType.Hazardous,
                    "test")
            ]
        };

        MiningDestinationSnapshot destination =
            MiningDestinationSnapshot.FromCandidate(
                candidate,
                new DateTimeOffset(2026, 9, 3, 18, 0, 0, TimeSpan.Zero));

        Assert.Equal("Lalande 34968", destination.SystemName);
        Assert.Equal("AB 8", destination.BodyName);
        Assert.Equal("A Ring", destination.RingDisplayName);
        Assert.Equal("Lalande 34968 AB 8 A Ring", destination.RingName);
        Assert.Equal(MiningResSiteType.Hazardous, destination.ResType);
        Assert.True(destination.Available);
    }

    [Fact]
    public void AutoTargetsDoNotExpandUnknownRingToWholeCatalog()
    {
        var unknown = new MiningRingContextSnapshot(
            1,
            "Test",
            "Test 1 A Ring",
            string.Empty,
            "Pristine",
            Array.Empty<string>());

        Assert.Empty(MiningTargetSelector.GetAutoCandidates(unknown));
    }

    [Fact]
    public void UnknownRingCanStillUseObservedHotspots()
    {
        var unknown = new MiningRingContextSnapshot(
            1,
            "Test",
            "Test 1 A Ring",
            string.Empty,
            "Pristine",
            ["Platinum", "Osmium"]);

        IReadOnlyList<string> result =
            MiningTargetSelector.GetAutoCandidates(unknown);

        Assert.Equal(["Platinum", "Osmium"], result);
        Assert.DoesNotContain("Monazite", result);
    }
}
