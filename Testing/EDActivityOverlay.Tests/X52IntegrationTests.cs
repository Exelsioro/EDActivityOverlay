using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Hardware;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class X52IntegrationTests
{
    [Fact]
    public void OneNoisySelectBurstDefersThenTogglesInteraction()
    {
        var filter = new X52SoftButtonFilter();

        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_000));
        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_016));
        Assert.Null(filter.Process(0, 1_031));
        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_047));
        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_078));
        Assert.Null(filter.Process(0, 1_094));
        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_109));

        Assert.Null(filter.ProcessPending(1_199));
        Assert.Null(filter.ProcessPending(1_608));

        Assert.Equal(
            X52ControlAction.ToggleInteraction,
            filter.ProcessPending(1_609));

        Assert.Null(filter.ProcessPending(1_800));
    }

    [Fact]
    public void DoubleSelectConsumesFirstClickAndOnlyTogglesOverlay()
    {
        var filter = new X52SoftButtonFilter();

        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_000));
        Assert.Null(filter.Process(0, 1_025));
        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_050));

        Assert.Null(filter.ProcessPending(1_140));
        Assert.Null(filter.ProcessPending(1_400));

        Assert.Equal(
            X52ControlAction.ToggleOverlay,
            filter.Process(X52SoftButtonFilter.SelectMask, 1_490));

        Assert.Null(filter.ProcessPending(1_550));
        Assert.Null(filter.ProcessPending(2_100));
    }

    [Fact]
    public void DoubleSelectWorksEvenWhenTimerDidNotCloseFirstBurst()
    {
        var filter = new X52SoftButtonFilter();

        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_000));
        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_050));

        Assert.Equal(
            X52ControlAction.ToggleOverlay,
            filter.Process(X52SoftButtonFilter.SelectMask, 1_250));

        Assert.Null(filter.ProcessPending(2_000));
    }

    [Fact]
    public void ExpiredFirstClickIsSingleAndNextBurstStartsNewGesture()
    {
        var filter = new X52SoftButtonFilter();

        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_000));
        Assert.Null(filter.ProcessPending(1_090));

        Assert.Equal(
            X52ControlAction.ToggleInteraction,
            filter.ProcessPending(1_500));

        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_700));
        Assert.Null(filter.ProcessPending(1_790));

        Assert.Equal(
            X52ControlAction.ToggleInteraction,
            filter.ProcessPending(2_200));
    }
    [Fact]
    public void OverlayUsesNativeX52MousePath()
    {
        string repository = FindRepositoryRoot();
        string pointer = File.ReadAllText(
            Path.Combine(
                repository,
                "EDActivityOverlay",
                "Services",
                "Hardware",
                "X52OverlayPointerController.cs"));

        Assert.Contains(
            "Compatibility no-op",
            pointer,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Vortice.DirectInput",
            pointer,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CooperativeLevel.",
            pointer,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "mouse_event",
            pointer,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SetCursorPos",
            pointer,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FireAButtonIndex",
            pointer,
            StringComparison.Ordinal);
    }
    [Fact]
    public void MfdWheelRequiresReleaseForSameDirectionButAllowsQuickReversal()
    {
        var filter = new X52SoftButtonFilter();

        Assert.Equal(
            X52ControlAction.PreviousActivity,
            filter.Process(X52SoftButtonFilter.ScrollUpMask, 1_000));

        Assert.Null(
            filter.Process(X52SoftButtonFilter.ScrollUpMask, 1_050));

        Assert.Null(filter.Process(0, 1_060));

        Assert.Equal(
            X52ControlAction.PreviousActivity,
            filter.Process(X52SoftButtonFilter.ScrollUpMask, 1_095));

        Assert.Equal(
            X52ControlAction.NextActivity,
            filter.Process(X52SoftButtonFilter.ScrollDownMask, 1_130));
    }
    [Fact]
    public void MfdFilterRejectsAmbiguousCombinedMasks()
    {
        var filter = new X52SoftButtonFilter();

        Assert.Null(filter.Process(
            X52SoftButtonFilter.ScrollUpMask | X52SoftButtonFilter.ScrollDownMask,
            3_000));
        Assert.Null(filter.Process(
            X52SoftButtonFilter.SelectMask | X52SoftButtonFilter.ScrollUpMask,
            3_500));
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public void DirectOutputConnectsToX52WhenHardwareProbeIsRequested()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ED_OVERLAY_X52_HARDWARE_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        using var client = new DirectOutputClient();
        client.InitializeClient();

        Assert.True(client.HasDevice);
        Assert.True(client.WriteLines(["ED OVERLAY TEST", "X52 CONNECTED", "PROBE OK"]));
        Assert.True(client.WriteLedComponents(
            Enumerable.Range(0, 20).ToDictionary(index => index, index => index is 2 or 4 or 6)));
    }

    [Fact]
    public void MfdLinesAreAsciiAndFitThePhysicalDisplay()
    {
        var state = GameStateSnapshot.Empty with
        {
            StarSystem = "Hyades Sector DR-V c2-23",
            Destination = "Очень длинная цель"
        };

        string[] lines = X52DisplayFormatter.BuildLines(state, ActivityType.Exploration);

        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => Assert.InRange(line.Length, 0, X52DisplayFormatter.MaximumLineLength));
        Assert.All(lines, line => Assert.All(line, character => Assert.InRange((int)character, 32, 126)));
        Assert.Equal("ED EXPLORATION", lines[0]);
    }

    [Fact]
    public void DangerAndFuelStateDriveIndependentLedComponents()
    {
        var state = GameStateSnapshot.Empty with { IsInDanger = true, LowFuel = true };

        IReadOnlyDictionary<int, bool> leds = X52DisplayFormatter.BuildLedComponents(state, ActivityType.Trade);

        Assert.True(leds[0]);
        Assert.True(leds[1]);
        Assert.False(leds[2]);
        Assert.True(leds[5]);
        Assert.False(leds[6]);
        Assert.True(leds[19]);
    }

    [Fact]
    public void FsdChargingUsesAmberFireB()
    {
        IReadOnlyDictionary<int, bool> leds = X52DisplayFormatter.BuildLedComponents(
            GameStateSnapshot.Empty with { FsdCharging = true }, ActivityType.Trade);

        Assert.True(leds[3]);
        Assert.True(leds[4]);
    }

    [Fact]
    public void InformativeBaselineKeepsEveryControllableGroupIlluminated()
    {
        IReadOnlyDictionary<int, bool> leds = X52DisplayFormatter.BuildLedComponents(
            GameStateSnapshot.Empty,
            ActivityType.Trade);

        Assert.True(leds[0]);
        for (int red = 1; red <= 17; red += 2)
        {
            Assert.True(leds[red] || leds[red + 1]);
        }
        Assert.True(leds[19]);
    }

    [Fact]
    public void FsdChargingMovesAmberMarkerAcrossToggleGroups()
    {
        var state = GameStateSnapshot.Empty with { FsdCharging = true };

        IReadOnlyDictionary<int, bool> first = X52DisplayFormatter.BuildLedComponents(state, ActivityType.Trade, 0);
        IReadOnlyDictionary<int, bool> second = X52DisplayFormatter.BuildLedComponents(state, ActivityType.Trade, 1);
        IReadOnlyDictionary<int, bool> third = X52DisplayFormatter.BuildLedComponents(state, ActivityType.Trade, 2);

        Assert.True(first[9] && first[10]);
        Assert.True(second[11] && second[12]);
        Assert.True(third[13] && third[14]);
        Assert.False(first[11]);
        Assert.False(second[13]);
        Assert.False(third[9]);
    }

    [Fact]
    public void CriticalWarningsPulseWithoutDisablingSteadyWarnings()
    {
        var state = GameStateSnapshot.Empty with
        {
            IsInDanger = true,
            LowFuel = true,
            OverHeating = true
        };

        IReadOnlyDictionary<int, bool> on = X52DisplayFormatter.BuildLedComponents(state, ActivityType.Trade, 0);
        IReadOnlyDictionary<int, bool> off = X52DisplayFormatter.BuildLedComponents(state, ActivityType.Trade, 1);

        Assert.True(on[0]);
        Assert.False(off[0]);
        Assert.True(on[1]);
        Assert.False(off[1]);
        Assert.True(on[5]);
        Assert.False(off[5]);
        Assert.True(on[19]);
        Assert.False(off[19]);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EDActivityOverlay", "EDActivityOverlay.csproj")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
