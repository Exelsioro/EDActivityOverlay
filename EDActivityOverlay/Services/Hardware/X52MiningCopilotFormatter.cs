using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Mining;

namespace EDActivityOverlay.Services.Hardware;

internal static class X52MiningCopilotFormatter
{
    public static string[] BuildLines(
        MiningSessionSnapshot session,
        MiningCollectorActivitySnapshot collectors,
        string? targetCommodity,
        double baselineThreshold,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(collectors);

        MiningIntelligenceSnapshot intelligence =
            MiningIntelligenceCalculator.Calculate(
                session,
                collectors,
                targetCommodity,
                baselineThreshold,
                now);

        double effectiveThreshold =
            intelligence.AdaptiveThreshold.Ready
                ? intelligence.AdaptiveThreshold.Suggested
                : Math.Clamp(baselineThreshold, 0, 100);

        return
        [
            X52DisplayFormatter.NormalizeLine(
                BuildPrimary(
                    session,
                    intelligence,
                    targetCommodity,
                    effectiveThreshold)),
            X52DisplayFormatter.NormalizeLine(
                session.CargoCapacity > 0
                    ? $"C{session.CargoUsed}/{session.CargoCapacity} L{session.LimpetsRemaining}"
                    : $"C? L{session.LimpetsRemaining}"),
            X52DisplayFormatter.NormalizeLine(
                BuildAdvisory(intelligence))
        ];
    }

    public static IReadOnlyDictionary<int, bool> BuildLedComponents(
        GameStateSnapshot game,
        MiningSessionSnapshot session,
        MiningCollectorActivitySnapshot collectors,
        string? targetCommodity,
        double baselineThreshold,
        long animationStep = 0,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(collectors);

        Dictionary<int, bool> result =
            X52DisplayFormatter.BuildLedComponents(
                    game,
                    ActivityType.Mining,
                    animationStep)
                .ToDictionary(item => item.Key, item => item.Value);

        MiningIntelligenceSnapshot intelligence =
            MiningIntelligenceCalculator.Calculate(
                session,
                collectors,
                targetCommodity,
                baselineThreshold,
                now);

        double effectiveThreshold =
            intelligence.AdaptiveThreshold.Ready
                ? intelligence.AdaptiveThreshold.Suggested
                : Math.Clamp(baselineThreshold, 0, 100);

        bool pulseOn = animationStep % 2 == 0;

        if (session.Prospects.LastOrDefault() is { } prospect
            && intelligence.Phase == MiningHudPhase.ProspectDecision)
        {
            MiningProspectAdvice advice =
                MiningProspectorAdvisor.Evaluate(
                    prospect,
                    targetCommodity,
                    effectiveThreshold);

            SetColor(
                result,
                9,
                10,
                advice.Decision switch
                {
                    MiningProspectDecision.Mine => LedColor.Green,
                    MiningProspectDecision.Core => LedColor.Amber,
                    MiningProspectDecision.Skip => LedColor.Red,
                    _ => LedColor.Green
                });
        }

        SetColor(
            result,
            11,
            12,
            collectors.Available && collectors.TopUpRecommended > 0
                ? LedColor.Amber
                : LedColor.Green);

        SetColor(
            result,
            13,
            14,
            intelligence.Limpets.Critical
                ? (pulseOn ? LedColor.Red : LedColor.Off)
                : intelligence.Limpets.Low
                    ? LedColor.Amber
                    : LedColor.Green);

        SetColor(
            result,
            15,
            16,
            intelligence.Leave.Recommendation switch
            {
                MiningLeaveRecommendation.CargoFull
                    or MiningLeaveRecommendation.LeaveNow
                    => LedColor.Red,
                MiningLeaveRecommendation.FinishCurrentRock
                    => LedColor.Amber,
                _ => LedColor.Green
            });

        return result;
    }

    private static string BuildPrimary(
        MiningSessionSnapshot session,
        MiningIntelligenceSnapshot intelligence,
        string? targetCommodity,
        double threshold)
    {
        if (session.Prospects.LastOrDefault() is { } prospect
            && intelligence.Phase == MiningHudPhase.ProspectDecision)
        {
            MiningProspectAdvice advice =
                MiningProspectorAdvisor.Evaluate(
                    prospect,
                    targetCommodity,
                    threshold);

            string decision = advice.Decision switch
            {
                MiningProspectDecision.Mine => "MINE",
                MiningProspectDecision.Skip => "SKIP",
                MiningProspectDecision.Core => "CORE",
                _ => "NO TARGET"
            };

            return advice.TargetProportion is { } proportion
                ? $"{decision} {proportion:0.#}%"
                : decision;
        }

        string target = string.IsNullOrWhiteSpace(targetCommodity)
            ? "NO TARGET"
            : targetCommodity.Trim();

        return intelligence.Phase switch
        {
            MiningHudPhase.Full => "CARGO FULL",
            MiningHudPhase.NearFull => "NEAR FULL",
            MiningHudPhase.Mining => $"MINING {target}",
            MiningHudPhase.Searching => $"FIND {target}",
            _ => "MINING READY"
        };
    }

    private static string BuildAdvisory(
        MiningIntelligenceSnapshot intelligence)
    {
        if (intelligence.Leave.Recommendation
            == MiningLeaveRecommendation.CargoFull)
        {
            return "CARGO FULL";
        }

        if (intelligence.Leave.Recommendation
            == MiningLeaveRecommendation.LeaveNow)
        {
            return "LEAVE NOW";
        }

        if (intelligence.Leave.Recommendation
            == MiningLeaveRecommendation.FinishCurrentRock)
        {
            return "FINISH ROCK";
        }

        if (intelligence.Limpets.Critical)
        {
            return "LIMPETS CRIT";
        }

        if (intelligence.Limpets.Low)
        {
            return "LIMPETS LOW";
        }

        if (intelligence.Collectors.Available
            && intelligence.Collectors.TopUpRecommended > 0)
        {
            return $"COL ~{intelligence.Collectors.EstimatedActive}/{intelligence.Collectors.Capacity} +{intelligence.Collectors.TopUpRecommended}";
        }

        return intelligence.FieldQuality switch
        {
            MiningFieldQuality.Declining => "MOVE FIELD",
            MiningFieldQuality.Good => "FIELD GOOD",
            MiningFieldQuality.Stable => "FIELD STABLE",
            _ => intelligence.AdaptiveThreshold.Ready
                ? $"THR {intelligence.AdaptiveThreshold.Suggested:0.#}%"
                : "COPILOT READY"
        };
    }

    private static void SetColor(
        Dictionary<int, bool> values,
        int red,
        int green,
        LedColor color)
    {
        values[red] = color is LedColor.Red or LedColor.Amber;
        values[green] = color is LedColor.Green or LedColor.Amber;
    }

    private enum LedColor
    {
        Off,
        Red,
        Green,
        Amber
    }
}
