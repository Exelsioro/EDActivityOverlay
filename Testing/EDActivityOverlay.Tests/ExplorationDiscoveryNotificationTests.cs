using System.Text.Json;
using EDActivityOverlay.Services.Notifications;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class ExplorationDiscoveryNotificationTests
{
    [Theory]
    [InlineData(false, false, "Loc_Notification_First_Discovery_And_Mapping_Format")]
    [InlineData(false, true, "Loc_Notification_First_Discovery_Format")]
    [InlineData(true, false, "Loc_Notification_First_Mapping_Format")]
    [InlineData(true, true, null)]
    public void CandidateMessageReflectsIndependentJournalFlags(
        bool wasDiscovered,
        bool wasMapped,
        string? expected)
    {
        using JsonDocument document = JsonDocument.Parse(
            $$"""
            {
              "event":"Scan",
              "BodyName":"Test 1",
              "PlanetClass":"Rocky body",
              "WasDiscovered":{{wasDiscovered.ToString().ToLowerInvariant()}},
              "WasMapped":{{wasMapped.ToString().ToLowerInvariant()}}
            }
            """);

        Assert.Equal(
            expected,
            NotificationCenterService.GetDiscoveryCandidateMessageKey(
                document.RootElement));
    }

    [Theory]
    [InlineData(false, false, "Loc_Notification_First_Discovery_Format")]
    [InlineData(true, false, null)]
    public void StarScanNeverTreatsWasMappedAsFirstMappingCandidate(
        bool wasDiscovered,
        bool wasMapped,
        string? expected)
    {
        using JsonDocument document = JsonDocument.Parse(
            $$"""
            {
              "event":"Scan",
              "BodyName":"Test A",
              "StarType":"K",
              "WasDiscovered":{{wasDiscovered.ToString().ToLowerInvariant()}},
              "WasMapped":{{wasMapped.ToString().ToLowerInvariant()}}
            }
            """);

        Assert.Equal(
            expected,
            NotificationCenterService.GetDiscoveryCandidateMessageKey(
                document.RootElement));
    }

    [Fact]
    public void MissingJournalFlagsAreNotTreatedAsCandidates()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{"event":"Scan","BodyName":"Test 1","PlanetClass":"Rocky body"}""");

        Assert.Null(
            NotificationCenterService.GetDiscoveryCandidateMessageKey(
                document.RootElement));
    }

    [Fact]
    public void FirstScanTrackerSuppressesLaterScansUntilReset()
    {
        var tracker = new ExplorationFirstScanTracker();

        var first = new ExplorationScanBaseline(
            "AutoScan",
            true,
            true,
            false);

        var later = new ExplorationScanBaseline(
            "Detailed",
            true,
            false,
            false);

        Assert.True(
            tracker.TryObserve(
                "12345:6",
                first,
                out ExplorationScanBaseline baseline));

        Assert.Equal(
            first,
            baseline);

        Assert.False(
            tracker.TryObserve(
                "12345:6",
                later,
                out baseline));

        Assert.Equal(
            first,
            baseline);

        tracker.Reset();

        Assert.True(
            tracker.TryObserve(
                "12345:6",
                later,
                out baseline));

        Assert.Equal(
            later,
            baseline);
    }

    [Fact]
    public void DetailedScanCanRevealMappingCandidateAfterAutoScanSaidMapped()
    {
        var tracker = new ExplorationFirstScanTracker();
        var autoScan = new ExplorationScanBaseline("AutoScan", true, true, false);
        var detailed = new ExplorationScanBaseline("Detailed", true, false, false);

        Assert.True(tracker.TryObserve("12345:6", autoScan, out _));
        Assert.Null(tracker.TryClaimCandidateMessage("12345:6", autoScan, isPlanet: true));

        Assert.False(tracker.TryObserve("12345:6", detailed, out _));
        Assert.Equal(
            "Loc_Notification_First_Mapping_Format",
            tracker.TryClaimCandidateMessage("12345:6", detailed, isPlanet: true));
        Assert.Null(tracker.TryClaimCandidateMessage("12345:6", detailed, isPlanet: true));
    }

    [Fact]
    public void DetailedScanAfterDssCompletionDoesNotSuggestMappingAgain()
    {
        var tracker = new ExplorationFirstScanTracker();
        var autoScan = new ExplorationScanBaseline("AutoScan", true, true, false);
        var detailed = new ExplorationScanBaseline("Detailed", true, false, false);

        Assert.Null(tracker.TryClaimCandidateMessage("12345:6", autoScan, isPlanet: true));
        tracker.MarkMapped("12345:6");
        Assert.Null(tracker.TryClaimCandidateMessage("12345:6", detailed, isPlanet: true));

        tracker.Reset();
        Assert.Equal(
            "Loc_Notification_First_Mapping_Format",
            tracker.TryClaimCandidateMessage("12345:6", detailed, isPlanet: true));
    }

    [Fact]
    public void DiscoveryAndMappingCandidatesAreClaimedIndependently()
    {
        var tracker = new ExplorationFirstScanTracker();
        var first = new ExplorationScanBaseline("AutoScan", true, false, false);
        var later = new ExplorationScanBaseline("Detailed", false, false, false);

        Assert.Equal(
            "Loc_Notification_First_Mapping_Format",
            tracker.TryClaimCandidateMessage("12345:6", first, isPlanet: true));
        Assert.Equal(
            "Loc_Notification_First_Discovery_Format",
            tracker.TryClaimCandidateMessage("12345:6", later, isPlanet: true));
        Assert.Null(tracker.TryClaimCandidateMessage("12345:6", later, isPlanet: true));

        Assert.Equal(
            "Loc_Notification_First_Discovery_And_Mapping_Format",
            tracker.TryClaimCandidateMessage(
                "12345:7",
                new ExplorationScanBaseline("Detailed", false, false, false),
                isPlanet: true));
        Assert.Null(tracker.TryClaimCandidateMessage(
            "12345:7",
            new ExplorationScanBaseline("Detailed", false, false, false),
            isPlanet: true));
    }

    [Fact]
    public void StarNeverClaimsFirstMappingAndResetAllowsNewVisit()
    {
        var tracker = new ExplorationFirstScanTracker();
        var star = new ExplorationScanBaseline("AutoScan", true, false, false);

        Assert.Null(tracker.TryClaimCandidateMessage("12345:1", star, isPlanet: false));
        Assert.True(tracker.TryMarkIncompleteFss());
        Assert.False(tracker.TryMarkIncompleteFss());

        tracker.Reset();

        Assert.True(tracker.TryMarkIncompleteFss());
        Assert.Equal(
            "Loc_Notification_First_Mapping_Format",
            tracker.TryClaimCandidateMessage("12345:1", star, isPlanet: true));
    }
}
