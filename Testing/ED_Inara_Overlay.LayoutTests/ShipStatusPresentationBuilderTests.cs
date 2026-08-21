using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ShipStatusPresentationBuilderTests
{
    [Fact]
    public void PresentsNextRouteStarWithoutDuplicatingNormalShipGauges()
    {
        var state = new GameStateSnapshot
        {
            StarSystem = "Sol",
            FuelMain = 24,
            FuelCapacityMain = 32,
            NavRoute =
            [
                new NavRouteStar("Sol", "G"),
                new NavRouteStar("Barnard's Star", "M")
            ]
        };

        ShipStatusPresentation result = ShipStatusPresentationBuilder.Build(state);

        Assert.Equal("Sol", result.CurrentSystem);
        Assert.Equal("Barnard's Star", result.NextSystem);
        Assert.True(result.NextStarScoopable);
        Assert.Equal(ShipStatusAdvisoryKind.None, result.Advisory);
    }

    [Fact]
    public void CriticalFuelHasPriorityOverRouteHazard()
    {
        var state = new GameStateSnapshot
        {
            StarSystem = "A",
            LowFuel = true,
            FuelMain = 1,
            FuelCapacityMain = 32,
            NavRoute =
            [
                new NavRouteStar("A", "G"),
                new NavRouteStar("Danger", "N")
            ]
        };

        Assert.Equal(ShipStatusAdvisoryKind.FuelCritical,
            ShipStatusPresentationBuilder.Build(state).Advisory);
    }
}
