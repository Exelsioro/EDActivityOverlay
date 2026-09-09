using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services;

internal static class FsdScoStatusPresentation
{
    public static string BuildCompact(
        GameStateSnapshot state,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ScoActive)
        {
            return "SCO ACTIVE";
        }

        if (state.FsdCooldown)
        {
            return "FSD COOLDOWN";
        }

        return string.Empty;
    }

    public static string BuildOverlay(
        GameStateSnapshot state,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ScoActive)
        {
            return Loc.Get("Loc_SCO_ACTIVE");
        }

        if (state.FsdCooldown)
        {
            return Loc.Get("Loc_FSD_COOLDOWN");
        }

        return string.Empty;
    }
}
