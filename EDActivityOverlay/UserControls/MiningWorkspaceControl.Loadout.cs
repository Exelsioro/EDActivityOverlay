using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Mining;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.UserControls;

public partial class MiningWorkspaceControl
{
    private static string BuildLoadoutFooter()
    {
        MiningLoadoutSnapshot loadout =
            MiningLoadoutService.Instance.Current;

        var lines = new List<string>();

        if (!loadout.Available)
        {
            lines.Add("LOADOUT · ?");
        }
        else
        {
            lines.Add(
                $"LOADOUT · LASER {ReadinessMark(loadout.Laser.Level)}" +
                $" · CORE {ReadinessMark(loadout.Core.Level)}" +
                $" · SUB {ReadinessMark(loadout.Subsurface.Level)}" +
                $" · SURFACE {ReadinessMark(loadout.Surface.Level)}");

            string prospector =
                !loadout.HasProspector
                    ? "P —→A"
                    : loadout.HasAProspector
                        ? "P A"
                        : $"P {(
                            string.IsNullOrWhiteSpace(
                                loadout.BestProspectorRating)
                                ? "?"
                                : loadout.BestProspectorRating)}→A";

            lines.Add(
                $"{prospector}" +
                $" · C {(loadout.HasCollector ? "✓" : "—")}" +
                $" · DSS {(loadout.HasDetailedSurfaceScanner ? "✓" : "—")}" +
                $" · PWA {(loadout.HasPulseWaveAnalyzer ? "✓" : "—")}");
        }

        return string.Join(
            Environment.NewLine,
            lines);
    }

    private static string ReadinessMark(
        MiningReadinessLevel level) =>
        level switch
        {
            MiningReadinessLevel.FullKit => "✓",
            MiningReadinessLevel.Usable => "~",
            MiningReadinessLevel.MissingRequired => "×",
            _ => "?"
        };
}
