using System.Globalization;
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

        double remaining =
            state.GetScoCooldownRemainingSeconds(
                now ?? DateTimeOffset.UtcNow);

        if (state.FsdCooldown
            && remaining > 0)
        {
            return $"FSD+SCO {FormatSeconds(remaining)}";
        }

        if (state.FsdCooldown)
        {
            return "FSD COOLDOWN";
        }

        return remaining > 0
            ? $"SCO CD {FormatSeconds(remaining)}"
            : string.Empty;
    }

    public static string BuildOverlay(
        GameStateSnapshot state,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ScoActive)
        {
            return "SCO ACTIVE";
        }

        double remaining =
            state.GetScoCooldownRemainingSeconds(
                now ?? DateTimeOffset.UtcNow);

        if (state.FsdCooldown
            && remaining > 0)
        {
            return $"FSD CD | SCO {FormatSeconds(remaining)}";
        }

        if (state.FsdCooldown)
        {
            return "FSD COOLDOWN";
        }

        return remaining > 0
            ? $"SCO CD {FormatSeconds(remaining)}"
            : string.Empty;
    }

    private static string FormatSeconds(
        double seconds) =>
        $"{seconds.ToString("0.0", CultureInfo.InvariantCulture)}s";
}
