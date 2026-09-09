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
            """{"event":"Scan","BodyName":"Test 1"}""");

        Assert.Null(
            NotificationCenterService.GetDiscoveryCandidateMessageKey(
                document.RootElement));
    }
}
