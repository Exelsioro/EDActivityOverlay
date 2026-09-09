using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class ExplorationJumpVisitTrackerTests
{
    [Fact]
    public void PinsStartJumpDestinationUntilArrivalDespiteFollowingTargetChanging()
    {
        var tracker = new ExplorationJumpVisitTracker((commander, address, name) =>
            new ExplorationSystemHistorySnapshot(
                commander,
                address,
                name,
                null,
                null,
                Array.Empty<ExplorationHistoryBodySnapshot>()));
        var game = GameStateSnapshot.Empty with
        {
            Commander = "Test",
            StarSystem = "A",
            DestinationName = "B",
            DestinationSystemAddress = 2,
            DestinationIsSystemTarget = true
        };

        Assert.True(tracker.Apply(
            Event("StartJump", """
                {"JumpType":"Hyperspace","StarSystem":"B","SystemAddress":2}
                """),
            game));

        // Status/FSDTarget may already point to C during the A -> B animation.
        game = game with
        {
            DestinationName = "C",
            DestinationSystemAddress = 3
        };
        Assert.Equal("B", tracker.Current.SystemName);
        Assert.False(tracker.Current.Arrived);

        Assert.True(tracker.Apply(
            Event("FSDJump", """
                {"StarSystem":"B","SystemAddress":2}
                """),
            game));

        Assert.Equal("B", tracker.Current.SystemName);
        Assert.True(tracker.Current.Arrived);
        Assert.False(tracker.Current.WasVisitedBeforeJump);
    }

    [Fact]
    public void NextStartJumpReplacesArrivedSystemAndPreservesPreviousVisitTime()
    {
        DateTimeOffset previous = new(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var tracker = new ExplorationJumpVisitTracker((commander, address, name) =>
            new ExplorationSystemHistorySnapshot(
                commander,
                address,
                name,
                previous,
                previous,
                Array.Empty<ExplorationHistoryBodySnapshot>()));
        var game = GameStateSnapshot.Empty with { Commander = "Test", StarSystem = "B" };

        tracker.Apply(
            Event("StartJump", """
                {"JumpType":"Hyperspace","StarSystem":"C","SystemAddress":3}
                """),
            game);

        Assert.Equal("C", tracker.Current.SystemName);
        Assert.True(tracker.Current.WasVisitedBeforeJump);
        Assert.Equal(previous, tracker.Current.LastVisitedUtc);
    }

    [Fact]
    public void BootstrapReplayDoesNotCreateStaleJumpBanner()
    {
        var tracker = new ExplorationJumpVisitTracker((_, _, _) =>
            ExplorationSystemHistorySnapshot.Empty);

        bool changed = tracker.Apply(
            Event(
                "StartJump",
                """{"JumpType":"Hyperspace","StarSystem":"Old B","SystemAddress":2}""",
                JournalEventOrigin.Bootstrap),
            GameStateSnapshot.Empty);

        Assert.False(changed);
        Assert.False(tracker.Current.Available);
    }

    private static JournalEventReceivedEventArgs Event(
        string name,
        string json,
        JournalEventOrigin origin = JournalEventOrigin.Live)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new JournalEventReceivedEventArgs(
            name,
            new DateTimeOffset(2026, 9, 9, 20, 0, 0, TimeSpan.Zero),
            document.RootElement.Clone(),
            origin);
    }
}
