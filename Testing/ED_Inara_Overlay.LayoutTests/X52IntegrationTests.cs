using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Hardware;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class X52IntegrationTests
{
    [Fact]
    public void MfdSelectBounceProducesOnlyOneToggle()
    {
        var filter = new X52SoftButtonFilter();

        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_000));
        Assert.Equal(X52ControlAction.ToggleActivity, filter.Process(0, 1_100));
        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_130));
        Assert.Null(filter.Process(0, 1_150));
        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_600));
        Assert.Equal(X52ControlAction.ToggleActivity, filter.Process(0, 1_680));
    }

    [Fact]
    public void HoldingMfdSelectTogglesInteractionWithoutAlsoTogglingActivity()
    {
        var filter = new X52SoftButtonFilter();

        Assert.Null(filter.Process(X52SoftButtonFilter.SelectMask, 1_000));
        Assert.Null(filter.ProcessHold(1_699));
        Assert.Equal(X52ControlAction.ToggleInteraction, filter.ProcessHold(1_700));
        Assert.Null(filter.Process(0, 1_800));
    }

    [Fact]
    public void OverlayControllerLayerIsRestrictedToInteractiveMode()
    {
        string repository = FindRepositoryRoot();
        string main = File.ReadAllText(Path.Combine(repository, "ED_Inara_Overlay", "Windows", "MainWindow.xaml.cs"));
        string pointer = File.ReadAllText(Path.Combine(repository, "ED_Inara_Overlay", "Services", "Hardware", "X52OverlayPointerController.cs"));

        Assert.Contains("x52OverlayPointerController.Enabled = canInteract", main, StringComparison.Ordinal);
        Assert.Contains("Pov", pointer, StringComparison.Ordinal);
        Assert.Contains("FireAButtonMask", pointer, StringComparison.Ordinal);
        Assert.Contains("mouse_event(MouseLeftDown", pointer, StringComparison.Ordinal);
    }

    [Fact]
    public void MfdWheelSuppressesRepeatedAndOppositeBounce()
    {
        var filter = new X52SoftButtonFilter();

        Assert.Equal(X52ControlAction.PreviousActivity, filter.Process(X52SoftButtonFilter.ScrollUpMask, 2_000));
        Assert.Null(filter.Process(X52SoftButtonFilter.ScrollUpMask, 2_020));
        Assert.Null(filter.Process(X52SoftButtonFilter.ScrollDownMask, 2_060));
        Assert.Equal(X52ControlAction.NextActivity, filter.Process(X52SoftButtonFilter.ScrollDownMask, 2_200));
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
            if (File.Exists(Path.Combine(directory.FullName, "ED_Inara_Overlay", "ED_Inara_Overlay.csproj")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
