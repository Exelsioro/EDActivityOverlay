using EDActivityOverlay.Models;
using EDActivityOverlay.Services;

namespace EDActivityOverlay.UserControls;

public partial class MiningWorkspaceControl
{
    private static string BuildIntelligenceText(
        MiningIntelligenceSnapshot intelligence)
    {
        var lines = new List<string>();

        string phase = Loc.Get(
            intelligence.Phase switch
            {
                MiningHudPhase.Searching => "Loc_MINING_INTEL_PHASE_SEARCHING",
                MiningHudPhase.ProspectDecision => "Loc_MINING_INTEL_PHASE_PROSPECT",
                MiningHudPhase.Mining => "Loc_MINING_INTEL_PHASE_MINING",
                MiningHudPhase.NearFull => "Loc_MINING_INTEL_PHASE_NEAR_FULL",
                MiningHudPhase.Full => "Loc_MINING_INTEL_PHASE_FULL",
                _ => "Loc_MINING_INTEL_PHASE_IDLE"
            });

        string field = Loc.Get(
            intelligence.FieldQuality switch
            {
                MiningFieldQuality.Good => "Loc_MINING_INTEL_FIELD_GOOD",
                MiningFieldQuality.Stable => "Loc_MINING_INTEL_FIELD_STABLE",
                MiningFieldQuality.Declining => "Loc_MINING_INTEL_FIELD_DECLINING",
                _ => "Loc_MINING_INTEL_FIELD_UNKNOWN"
            });

        string threshold = intelligence.AdaptiveThreshold.Ready
            ? Loc.Format(
                "Loc_MINING_INTEL_THRESHOLD_FORMAT",
                intelligence.AdaptiveThreshold.Suggested,
                intelligence.AdaptiveThreshold.Baseline)
            : Loc.Get("Loc_MINING_INTEL_THRESHOLD_WARMING");

        lines.Add($"{phase} · {field} · {threshold}");

        string limpets = intelligence.Limpets.Ready
            ? Loc.Format(
                intelligence.Limpets.Critical
                    ? "Loc_MINING_INTEL_LIMPETS_CRITICAL_FORMAT"
                    : intelligence.Limpets.Low
                        ? "Loc_MINING_INTEL_LIMPETS_LOW_FORMAT"
                        : "Loc_MINING_INTEL_LIMPETS_FORMAT",
                intelligence.Limpets.Remaining,
                intelligence.Limpets.EstimatedRequired,
                intelligence.Limpets.SafeExcess)
            : Loc.Format(
                "Loc_MINING_INTEL_LIMPETS_WARMING_FORMAT",
                intelligence.Limpets.Remaining);

        string collectors = !intelligence.Collectors.Available
            ? Loc.Get("Loc_MINING_INTEL_COLLECTORS_UNKNOWN")
            : intelligence.Collectors.TopUpRecommended > 0
                ? Loc.Format(
                    "Loc_MINING_INTEL_COLLECTORS_TOPUP_FORMAT",
                    intelligence.Collectors.EstimatedActive,
                    intelligence.Collectors.Capacity,
                    intelligence.Collectors.TopUpRecommended)
                : Loc.Format(
                    "Loc_MINING_INTEL_COLLECTORS_OK_FORMAT",
                    intelligence.Collectors.EstimatedActive,
                    intelligence.Collectors.Capacity);

        lines.Add($"{limpets} · {collectors}");

        string leave = intelligence.Leave.Recommendation switch
        {
            MiningLeaveRecommendation.CargoFull =>
                Loc.Get("Loc_MINING_INTEL_LEAVE_FULL"),
            MiningLeaveRecommendation.LeaveNow =>
                Loc.Format(
                    "Loc_MINING_INTEL_LEAVE_NOW_FORMAT",
                    intelligence.Leave.EffectiveMineralRoom),
            MiningLeaveRecommendation.FinishCurrentRock =>
                Loc.Format(
                    "Loc_MINING_INTEL_LEAVE_FINISH_FORMAT",
                    intelligence.Leave.EffectiveMineralRoom),
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(leave))
        {
            lines.Add(leave);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
