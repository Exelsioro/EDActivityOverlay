using System;
using System.Collections.Generic;
using System.IO;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class JournalBootstrapLiveSplitTests
{
    [Fact]
    public void BootstrapBatchPublishesOneCurrentStateButKeepsEventOrigin()
    {
        var reducer =
            new JournalStateReducer();

        var states =
            new List<GameStateChangedEventArgs>();

        var events =
            new List<JournalEventReceivedEventArgs>();

        reducer.StateChanged +=
            (_, e) => states.Add(e);

        reducer.JournalEventReceived +=
            (_, e) => events.Add(e);

        reducer.BeginStateBatch(
            JournalEventOrigin.Bootstrap);

        reducer.ApplyJournalLine(
            """
            {"timestamp":"2026-08-28T10:00:00Z","event":"Location","StarSystem":"OLD","SystemAddress":1}
            """,
            JournalEventOrigin.Bootstrap);

        reducer.ApplyJournalLine(
            """
            {"timestamp":"2026-08-28T11:00:00Z","event":"FSDJump","StarSystem":"CURRENT","SystemAddress":2}
            """,
            JournalEventOrigin.Bootstrap);

        Assert.Empty(states);
        Assert.Equal(
            2,
            events.Count);

        Assert.All(
            events,
            item =>
                Assert.Equal(
                    JournalEventOrigin.Bootstrap,
                    item.Origin));

        reducer.EndStateBatch();

        GameStateChangedEventArgs state =
            Assert.Single(states);

        Assert.Equal(
            JournalEventOrigin.Bootstrap,
            state.Origin);

        Assert.Equal(
            "CURRENT",
            state.State.StarSystem);
    }

    [Fact]
    public void LiveEventStillPublishesImmediately()
    {
        var reducer =
            new JournalStateReducer();

        GameStateChangedEventArgs? changed =
            null;

        JournalEventReceivedEventArgs? received =
            null;

        reducer.StateChanged +=
            (_, e) => changed = e;

        reducer.JournalEventReceived +=
            (_, e) => received = e;

        reducer.ApplyJournalLine(
            """
            {"timestamp":"2026-08-28T12:00:00Z","event":"Location","StarSystem":"LIVE","SystemAddress":3}
            """);

        Assert.NotNull(changed);
        Assert.NotNull(received);

        Assert.Equal(
            JournalEventOrigin.Live,
            changed!.Origin);

        Assert.Equal(
            JournalEventOrigin.Live,
            received!.Origin);
    }

    [Fact]
    public void UiFacingConsumersExplicitlyIgnoreBootstrapReplay()
    {
        string notification =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Services",
                    "Notifications",
                    "NotificationCenterService.cs"));

        string explorationLog =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Services",
                    "Exploration",
                    "ExplorationLogService.cs"));

        string trade =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Services",
                    "Journal",
                    "TradeRouteProgressTracker.cs"));

        Assert.Contains(
            "journalEvent.Origin == JournalEventOrigin.Bootstrap",
            notification,
            StringComparison.Ordinal);

        Assert.Contains(
            "journalEvent.Origin == JournalEventOrigin.Bootstrap",
            explorationLog,
            StringComparison.Ordinal);

        Assert.Contains(
            "e.Origin == JournalEventOrigin.Bootstrap",
            trade,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplorationLogOnlyExposesEntriesCreatedThisAppSession()
    {
        string explorationLog =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Services",
                    "Exploration",
                    "ExplorationLogService.cs"));

        Assert.Contains(
            "sessionEntryIds.Contains",
            explorationLog,
            StringComparison.Ordinal);

        Assert.Contains(
            "sessionEntryIds.Add",
            explorationLog,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StatefulEngineeringConsumerStillAcceptsBootstrapEvents()
    {
        string engineering =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Services",
                    "Engineering",
                    "EngineeringService.cs"));

        Assert.DoesNotContain(
            "JournalEventOrigin.Bootstrap",
            engineering,
            StringComparison.Ordinal);
    }

    private static string FindProjectFile(
        params string[] relative)
    {
        for (
            DirectoryInfo? directory =
                new(
                    AppContext.BaseDirectory);
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
                return candidate;
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
