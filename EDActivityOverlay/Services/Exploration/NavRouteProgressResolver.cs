using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Exploration;

public sealed record NavRouteProgress(
    IReadOnlyList<NavRouteStar> RouteFromCurrent,
    IReadOnlyList<NavRouteStar> Ahead)
{
    public static NavRouteProgress Empty { get; } =
        new(
            Array.Empty<NavRouteStar>(),
            Array.Empty<NavRouteStar>());

    public NavRouteStar? Current =>
        RouteFromCurrent.Count > 0
            ? RouteFromCurrent[0]
            : null;

    public NavRouteStar? Next =>
        Ahead.Count > 0
            ? Ahead[0]
            : null;

    public int RemainingJumps =>
        Ahead.Count;
}

public static class NavRouteProgressResolver
{
    public static NavRouteProgress Resolve(
        GameStateSnapshot state)
    {
        if (state.NavRoute.Count == 0)
        {
            return
                NavRouteProgress.Empty;
        }

        int currentIndex;

        if (string.IsNullOrWhiteSpace(
                state.StarSystem))
        {
            // During bootstrap/status races Journal location can be temporarily
            // unavailable while NavRoute.json is already populated. Preserve
            // the legacy interpretation in that narrow case so route/fuel
            // advice remains usable.
            currentIndex =
                0;
        }
        else
        {
            currentIndex =
                -1;

            for (int index = 0;
                 index < state.NavRoute.Count;
                 index++)
            {
                if (state.NavRoute[index].System.Equals(
                        state.StarSystem,
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex =
                        index;

                    break;
                }
            }

            // When a live current system is known but cannot anchor the route,
            // NavRoute is stale or unrelated. Do not present a false next hop.
            if (currentIndex < 0)
            {
                return
                    NavRouteProgress.Empty;
            }
        }

        NavRouteStar[] fromCurrent =
            state.NavRoute
                .Skip(
                    currentIndex)
                .ToArray();

        NavRouteStar[] ahead =
            fromCurrent
                .Skip(1)
                .ToArray();

        return
            new NavRouteProgress(
                fromCurrent,
                ahead);
    }
}
