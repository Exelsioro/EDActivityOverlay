using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Exploration;

public static class FuelRouteAdvisor
{
    public static FuelRouteAssessment Evaluate(GameStateSnapshot state)
    {
        double percent = state.FuelCapacityMain > 0
            ? Math.Clamp(state.FuelMain / state.FuelCapacityMain * 100, 0, 100)
            : 0;
        IReadOnlyList<NavRouteStar> route = state.NavRoute;
        int remaining = Math.Max(0, route.Count - 1);
        int? scoopIndex = null;
        for (int index = 1; index < route.Count; index++)
        {
            if (route[index].IsScoopable)
            {
                scoopIndex = index;
                break;
            }
        }

        double? needed = null;
        if (scoopIndex is > 0 && state.FuelPerLightYearEstimate > 0)
        {
            double distance = 0;
            bool complete = true;
            for (int index = 0; index < scoopIndex.Value; index++)
            {
                double? leg = route[index].DistanceTo(route[index + 1]);
                if (leg is null)
                {
                    complete = false;
                    break;
                }
                distance += leg.Value;
            }
            if (complete) needed = distance * state.FuelPerLightYearEstimate;
        }

        double reserve = Math.Max(state.LastJumpFuelUsed, state.FuelCapacityMain * 0.1);
        FuelRouteSeverity severity;
        if (state.LowFuel || needed is { } fuelNeeded && state.FuelMain < fuelNeeded + reserve)
        {
            severity = FuelRouteSeverity.Critical;
        }
        else if (percent > 0 && percent < 35
                 || scoopIndex is null && remaining >= 2
                 || needed is { } cautionFuel && state.FuelMain < cautionFuel + reserve * 2)
        {
            severity = FuelRouteSeverity.Caution;
        }
        else if (state.FuelCapacityMain > 0)
        {
            severity = FuelRouteSeverity.Safe;
        }
        else
        {
            severity = FuelRouteSeverity.Unknown;
        }

        return new FuelRouteAssessment(
            severity, percent, remaining, scoopIndex,
            scoopIndex is { } found ? route[found].System : string.Empty,
            needed, reserve, state.FuelPerLightYearEstimate > 0);
    }
}
