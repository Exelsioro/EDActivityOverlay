using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;

namespace EDActivityOverlay.Services;

public static class ShipStatusPresentationBuilder
{
    public static ShipStatusPresentation Build(GameStateSnapshot state)
    {
        NavRouteStar? currentRouteStar =
            state.NavRoute.FirstOrDefault(
                star =>
                    star.System.Equals(
                        state.StarSystem,
                        StringComparison.OrdinalIgnoreCase));

        NavRouteStar? next =
            state.NavRoute.Skip(1).FirstOrDefault();

        string currentStarClass =
            !string.IsNullOrWhiteSpace(
                state.CurrentStarClass)
                ? state.CurrentStarClass
                : currentRouteStar?.StarClass
                  ?? string.Empty;

        FuelRouteAssessment fuel =
            FuelRouteAdvisor.Evaluate(
                state);

        ShipStatusAdvisoryKind advisory =
            fuel.Severity switch
            {
                FuelRouteSeverity.Critical =>
                    ShipStatusAdvisoryKind.FuelCritical,
                _ when next is { } star
                       && (star.IsNeutron
                           || star.IsWhiteDwarf) =>
                    ShipStatusAdvisoryKind.HazardousNextStar,
                _ when state.NavRoute.Count > 2
                       && state.NavRoute.Skip(1)
                           .All(
                               star =>
                                   !star.IsScoopable) =>
                    ShipStatusAdvisoryKind.NoScoopableStars,
                FuelRouteSeverity.Caution =>
                    ShipStatusAdvisoryKind.FuelCaution,
                _ =>
                    ShipStatusAdvisoryKind.None
            };

        return
            new ShipStatusPresentation(
                state.StarSystem,
                next?.System
                ?? string.Empty,
                next?.StarClass
                ?? string.Empty,
                Math.Max(
                    0,
                    state.NavRoute.Count - 1),
                next?.IsScoopable == true,
                fuel.FuelPercent,
                advisory)
            {
                CurrentStarClass =
                    currentStarClass,
                CurrentStarScoopable =
                    IsScoopableStarClass(
                        currentStarClass)
            };
    }

    private static bool IsScoopableStarClass(
        string? starClass) =>
        starClass?.Trim().ToUpperInvariant()
            is "O"
            or "B"
            or "A"
            or "F"
            or "G"
            or "K"
            or "M";
}