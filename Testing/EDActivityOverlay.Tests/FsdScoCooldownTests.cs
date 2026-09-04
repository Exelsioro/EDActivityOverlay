using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Hardware;
using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class FsdScoCooldownTests
{
    [Fact]
    public void StatusTracksScoAndStartsTenSecondDerivedCooldown()
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
            reducer.Current.ScoCooldownUntilUtc);

        DateTimeOffset before =
            DateTimeOffset.UtcNow;

        reducer.ApplyStatusJson(
            $$"""
            {
              "Flags":{{supercruise}},
              "Flags2":0
            }
            """);

        DateTimeOffset after =
            DateTimeOffset.UtcNow;

        GameStateSnapshot cooling =
            reducer.Current;

        Assert.False(
            cooling.ScoActive);

        DateTimeOffset until =
            Assert.IsType<DateTimeOffset>(
                cooling.ScoCooldownUntilUtc);

        Assert.InRange(
            until,
            before.AddSeconds(10),
            after.AddSeconds(10));

        Assert.True(
            cooling.GetScoCooldownRemainingSeconds(
                after) > 9.5);

        reducer.ApplyStatusJson(
            """
            {
              "Flags":0,
              "Flags2":0
            }
            """);

        Assert.Null(
            reducer.Current.ScoCooldownUntilUtc);
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
            reducer.Current.ScoCooldownUntilUtc);
    }

    [Fact]
    public void CompactDriveStatusPrioritizesActiveAndDirectCooldown()
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
            "FSD+SCO 7.3s",
            FsdScoStatusPresentation.BuildCompact(
                GameStateSnapshot.Empty with
                {
                    FsdCooldown = true,
                    ScoCooldownUntilUtc =
                        now.AddSeconds(7.25)
                },
                now));

        Assert.Equal(
            "FSD CD | SCO 7.3s",
            FsdScoStatusPresentation.BuildOverlay(
                GameStateSnapshot.Empty with
                {
                    FsdCooldown = true,
                    ScoCooldownUntilUtc =
                        now.AddSeconds(7.25)
                },
                now));

        Assert.Equal(
            "SCO CD 7.3s",
            FsdScoStatusPresentation.BuildCompact(
                GameStateSnapshot.Empty with
                {
                    ScoCooldownUntilUtc =
                        now.AddSeconds(7.25)
                },
                now));

        Assert.Equal(
            string.Empty,
            FsdScoStatusPresentation.BuildCompact(
                GameStateSnapshot.Empty with
                {
                    ScoCooldownUntilUtc =
                        now.AddMilliseconds(-1)
                },
                now));
    }

    [Fact]
    public void X52UsesDerivedScoCountdownInContextLine()
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
                    ScoCooldownUntilUtc =
                        now.AddSeconds(5.25)
                },
                ActivityType.Trade,
                now);

        Assert.Equal(
            "SCO CD 5.3s",
            lines[2]);
    }
}
