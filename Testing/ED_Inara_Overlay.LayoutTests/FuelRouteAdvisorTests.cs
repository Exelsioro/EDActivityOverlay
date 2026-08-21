using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Exploration;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class FuelRouteAdvisorTests
{
    [Fact]
    public void FindsNextScoopableAndProtectsReserve()
    {
        var state = new GameStateSnapshot
        {
            FuelMain = 5,
            FuelCapacityMain = 32,
            LastJumpFuelUsed = 3,
            FuelPerLightYearEstimate = 0.1,
            NavRoute = new[]
            {
                new NavRouteStar("A", "L", 0, 0, 0),
                new NavRouteStar("B", "T", 30, 0, 0),
                new NavRouteStar("C", "K", 60, 0, 0)
            }
        };

        FuelRouteAssessment result = FuelRouteAdvisor.Evaluate(state);

        Assert.Equal(2, result.JumpsToNextScoopable);
        Assert.Equal("C", result.NextScoopableSystem);
        Assert.Equal(6, result.EstimatedFuelToNextScoopable);
        Assert.Equal(FuelRouteSeverity.Critical, result.Severity);
    }
}
