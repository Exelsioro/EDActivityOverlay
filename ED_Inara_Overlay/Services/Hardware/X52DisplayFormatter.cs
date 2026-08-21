using System.Globalization;
using System.Text;
using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Hardware;

internal static class X52DisplayFormatter
{
    public const int MaximumLineLength = 16;

    public static string[] BuildLines(GameStateSnapshot state, ActivityType activity)
    {
        string activityName = activity switch
        {
            ActivityType.Engineering => "ENGINEERING",
            ActivityType.Exploration => "EXPLORATION",
            ActivityType.Mining => "MINING",
            _ => "TRADE"
        };
        string location = string.IsNullOrWhiteSpace(state.StarSystem) ? "WAITING JOURNAL" : state.StarSystem;
        string context = BuildContext(state);
        return
        [
            NormalizeLine($"ED {activityName}"),
            NormalizeLine(location),
            NormalizeLine(context)
        ];
    }

    internal static IReadOnlyDictionary<int, bool> BuildLedComponents(
        GameStateSnapshot state,
        ActivityType activity,
        long animationStep = 0)
    {
        var result = Enumerable.Range(0, 20).ToDictionary(index => index, _ => false);
        bool pulseOn = animationStep % 2 == 0;

        // Informative baseline: controls stay illuminated green, active systems
        // become amber and warnings override them with red/pulsing red.
        result[0] = !state.IsInDanger || pulseOn;
        SetColor(result, 1, 2, state.IsInDanger
            ? (pulseOn ? X52LedColor.Red : X52LedColor.Off)
            : state.HardpointsDeployed ? X52LedColor.Amber : X52LedColor.Green);
        SetColor(result, 3, 4, state.FsdMassLocked ? X52LedColor.Red
            : state.FsdCharging ? X52LedColor.Amber
            : state.InSupercruise || state.FsdCooldown ? X52LedColor.Amber
            : X52LedColor.Green);
        SetColor(result, 5, 6, state.OverHeating
            ? (pulseOn ? X52LedColor.Red : X52LedColor.Off)
            : state.LowFuel ? X52LedColor.Red
            : state.FuelScooping ? X52LedColor.Amber
            : X52LedColor.Green);
        SetColor(result, 7, 8, state.Docked || state.Landed || state.LandingGearDown
            ? X52LedColor.Amber : X52LedColor.Green);
        SetColor(result, 9, 10, !state.JournalAvailable || state.Docked || state.Landed || state.OnFoot || state.ShieldsUp
            ? X52LedColor.Green : X52LedColor.Red);
        SetColor(result, 11, 12, state.SilentRunning ? X52LedColor.Red
            : state.CargoScoopDeployed ? X52LedColor.Amber
            : state.OnFoot ? X52LedColor.Amber
            : X52LedColor.Green);
        SetColor(result, 13, 14, state.NightVision || state.LightsOn
            ? X52LedColor.Amber
            : activity is ActivityType.Exploration or ActivityType.Mining
                ? X52LedColor.Amber : X52LedColor.Green);
        SetColor(result, 15, 16, string.IsNullOrWhiteSpace(state.Destination)
            ? X52LedColor.Green : X52LedColor.Amber);
        SetColor(result, 17, 18, state.IsInDanger || state.FsdMassLocked
            ? X52LedColor.Red : X52LedColor.Green);
        result[19] = !state.LowFuel || pulseOn;

        if (state.FsdCharging)
        {
            // A moving amber marker over a green T1-T6 baseline. It remains
            // readable as a charging animation without turning the cockpit dark.
            // Red warnings retain priority over the decorative animation.
            int activePair = (int)(animationStep % 3);
            bool shieldsWarning = state.JournalAvailable && !state.Docked && !state.Landed && !state.OnFoot && !state.ShieldsUp;
            SetColor(result, 9, 10, shieldsWarning
                ? X52LedColor.Red
                : activePair == 0 ? X52LedColor.Amber : X52LedColor.Green);
            SetColor(result, 11, 12, state.SilentRunning
                ? X52LedColor.Red
                : activePair == 1 ? X52LedColor.Amber : X52LedColor.Green);
            SetColor(result, 13, 14, activePair == 2 ? X52LedColor.Amber : X52LedColor.Green);
        }
        return result;
    }

    internal static string NormalizeLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(MaximumLineLength);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            char output = character is >= ' ' and <= '~' ? character : '?';
            result.Append(output);
            if (result.Length == MaximumLineLength) break;
        }
        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string BuildContext(GameStateSnapshot state)
    {
        if (state.IsInDanger) return "! DANGER !";
        if (state.LowFuel) return "! LOW FUEL !";
        if (state.FsdCharging) return "FSD CHARGING";
        if (!string.IsNullOrWhiteSpace(state.Destination)) return $"> {state.Destination}";
        if (state.Docked && !string.IsNullOrWhiteSpace(state.Station)) return state.Station;
        if (state.OnFoot) return "ON FOOT";
        if (state.InSrv) return "SRV";
        if (state.InSupercruise) return "SUPERCRUISE";
        return !string.IsNullOrWhiteSpace(state.ShipName) ? state.ShipName : state.Ship;
    }

    private static void SetColor(Dictionary<int, bool> values, int red, int green, X52LedColor color)
    {
        values[red] = color is X52LedColor.Red or X52LedColor.Amber;
        values[green] = color is X52LedColor.Green or X52LedColor.Amber;
    }

    private enum X52LedColor { Off, Red, Green, Amber }
}
