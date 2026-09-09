using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Hardware;
using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class FsdScoCooldownTests
{
    [Fact]
    public void StatusTracksScoWithoutInventingAnEstimatedCooldown()
    {
        var reducer =
            new JournalStateReducer();

        ulong supercruise =
            1UL << 4;

        ulong sco =
            1UL << 20;

        reducer.ApplyStatusJson(
            $$"""
            {
              "Flags":{{supercruise}},
              "Flags2":{{sco}}
            }
            """);

        Assert.True(
            reducer.Current.ScoActive);

        Assert.Null(
            reducer.Current.ScoEstimatedCooldownUntilUtc);

        reducer.ApplyStatusJson(
            $$"""
            {
              "Flags":{{supercruise}},
              "Flags2":0
            }
            """);

        GameStateSnapshot cooling =
            reducer.Current;

        Assert.False(
            cooling.ScoActive);

        Assert.Null(
            cooling.ScoEstimatedCooldownUntilUtc);

        reducer.ApplyStatusJson(
            """
            {
              "Flags":0,
              "Flags2":0
            }
            """);

        Assert.Null(
            reducer.Current.ScoEstimatedCooldownUntilUtc);
    }

    [Fact]
    public void FsdCooldownRemainsDirectStatusFlag()
    {
        var reducer =
            new JournalStateReducer();

        ulong flags =
            (1UL << 4)
            | (1UL << 18);

        reducer.ApplyStatusJson(
            $$"""
            {
              "Flags":{{flags}},
              "Flags2":0
            }
            """);

        Assert.True(
            reducer.Current.FsdCooldown);

        Assert.False(
            reducer.Current.ScoActive);

        Assert.Null(
            reducer.Current.ScoEstimatedCooldownUntilUtc);
    }

    [Fact]
    public void CompactDriveStatusOnlyShowsDirectGameState()
    {
        DateTimeOffset now =
            DateTimeOffset.Parse(
                "2026-09-05T00:00:00Z");

        Assert.Equal(
            "SCO ACTIVE",
            FsdScoStatusPresentation.BuildCompact(
                GameStateSnapshot.Empty with
                {
                    ScoActive = true,
                    FsdCooldown = true
                },
                now));

        Assert.Equal(
            "FSD COOLDOWN",
            FsdScoStatusPresentation.BuildCompact(
                GameStateSnapshot.Empty with
                {
                    FsdCooldown = true
                },
                now));

        Assert.Equal(
            "FSD COOLDOWN",
            FsdScoStatusPresentation.BuildCompact(
                GameStateSnapshot.Empty with
                {
                    FsdCooldown = true,
                    ScoEstimatedCooldownUntilUtc =
                        now.AddSeconds(7.25)
                },
                now));

        Assert.Equal(
            string.Empty,
            FsdScoStatusPresentation.BuildCompact(
                GameStateSnapshot.Empty with
                {
                    ScoEstimatedCooldownUntilUtc =
                        now.AddSeconds(7.25)
                },
                now));
    }

    [Fact]
    public void X52IgnoresDisabledScoCountdownInContextLine()
    {
        DateTimeOffset now =
            DateTimeOffset.Parse(
                "2026-09-05T00:00:00Z");

        string[] lines =
            X52DisplayFormatter.BuildLines(
                GameStateSnapshot.Empty with
                {
                    StarSystem = "Sol",
                    InSupercruise = true,
                    ScoEstimatedCooldownUntilUtc =
                        now.AddSeconds(5.25)
                },
                ActivityType.Trade,
                now);

        Assert.Equal(
            "SUPERCRUISE",
            lines[2]);
    }
}
