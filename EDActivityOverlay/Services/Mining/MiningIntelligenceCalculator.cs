using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Mining;

public static class MiningCollectorEstimator
{
    public static MiningCollectorActivitySnapshot Calculate(
        MiningLoadoutSnapshot loadout,
        IEnumerable<DateTimeOffset> launches,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        ArgumentNullException.ThrowIfNull(launches);

        MiningLoadoutModuleSnapshot[] controllers = loadout.Modules
            .Where(item =>
                item.Enabled
                && item.Kind is MiningModuleKind.CollectorController
                    or MiningModuleKind.MiningMultiLimpetController)
            .ToArray();

        if (!loadout.Available || controllers.Length == 0)
        {
            return MiningCollectorActivitySnapshot.Empty;
        }

        int capacity = controllers.Sum(ControllerCapacity);
        TimeSpan lifetime = controllers
            .Select(ControllerLifetime)
            .DefaultIfEmpty(TimeSpan.FromMinutes(5))
            .Max();

        DateTimeOffset cutoff = now - lifetime;
        int estimatedActive = Math.Min(
            capacity,
            launches.Count(timestamp =>
                timestamp <= now
                && timestamp >= cutoff));

        return new MiningCollectorActivitySnapshot(
            true,
            capacity,
            estimatedActive,
            Math.Max(0, capacity - estimatedActive),
            lifetime);
    }

    private static int ControllerCapacity(
        MiningLoadoutModuleSnapshot module)
    {
        if (module.Kind == MiningModuleKind.MiningMultiLimpetController)
        {
            return module.Item.Contains(
                "multidronecontrol_miningv2",
                StringComparison.OrdinalIgnoreCase)
                ? 14
                : 4;
        }

        return module.Size switch
        {
            >= 7 => 4,
            >= 5 => 3,
            >= 3 => 2,
            _ => 1
        };
    }

    private static TimeSpan ControllerLifetime(
        MiningLoadoutModuleSnapshot module)
    {
        if (module.Kind == MiningModuleKind.MiningMultiLimpetController)
        {
            if (module.Item.Contains(
                    "multidronecontrol_miningv2",
                    StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.FromMinutes(15);
            }

            return module.Rating.Equals("C", StringComparison.OrdinalIgnoreCase)
                ? TimeSpan.FromSeconds(510)
                : TimeSpan.FromMinutes(5);
        }

        return module.Rating.ToUpperInvariant() switch
        {
            "A" => TimeSpan.FromMinutes(12),
            "B" => TimeSpan.FromMinutes(7),
            _ => TimeSpan.FromMinutes(5)
        };
    }
}

public static class MiningIntelligenceCalculator
{
    private const int AdaptiveWindow = 16;
    private const int MinimumAdaptiveProspects = 12;
    private const int MinimumAdaptiveTargetSamples = 6;
    private const int MinimumLimpetTons = 10;

    public static MiningIntelligenceSnapshot Calculate(
        MiningSessionSnapshot session,
        MiningCollectorActivitySnapshot collectors,
        string? targetCommodity,
        double baselineThreshold,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        double baseline = Math.Clamp(baselineThreshold, 0, 100);

        MiningAdaptiveThresholdAdvice adaptive =
            CalculateAdaptiveThreshold(
                session,
                targetCommodity,
                baseline);

        MiningFieldQuality field =
            CalculateFieldQuality(
                session,
                targetCommodity,
                adaptive.Ready ? adaptive.Suggested : baseline);

        MiningLimpetAdvice limpets =
            CalculateLimpets(session);

        MiningSessionAnalyticsSnapshot analytics =
            MiningSessionAnalyticsCalculator.Calculate(
                session,
                targetCommodity,
                adaptive.Ready ? adaptive.Suggested : baseline,
                timestamp);

        MiningLeaveAdvice leave =
            CalculateLeave(
                session,
                analytics.EstimatedTimeToFull);

        MiningHudPhase phase =
            CalculatePhase(
                session,
                leave,
                timestamp);

        return new MiningIntelligenceSnapshot(
            phase,
            field,
            limpets,
            collectors,
            adaptive,
            leave);
    }

    public static MiningAdaptiveThresholdAdvice CalculateAdaptiveThreshold(
        MiningSessionSnapshot session,
        string? targetCommodity,
        double baselineThreshold)
    {
        double baseline = Math.Clamp(baselineThreshold, 0, 100);
        if (!session.IsActive
            || string.IsNullOrWhiteSpace(targetCommodity)
            || session.ProspectedAsteroids < MinimumAdaptiveProspects)
        {
            return new MiningAdaptiveThresholdAdvice(
                false,
                baseline,
                baseline,
                0,
                0);
        }

        MiningProspectSnapshot[] window = session.Prospects
            .TakeLast(AdaptiveWindow)
            .ToArray();

        MiningProspectAdvice[] evaluated = window
            .Select(item => MiningProspectorAdvisor.Evaluate(
                item,
                targetCommodity,
                baseline))
            .ToArray();

        double[] targetValues = evaluated
            .Where(item => item.TargetProportion.HasValue)
            .Select(item => item.TargetProportion!.Value)
            .OrderBy(value => value)
            .ToArray();

        if (targetValues.Length < MinimumAdaptiveTargetSamples)
        {
            return new MiningAdaptiveThresholdAdvice(
                false,
                baseline,
                baseline,
                evaluated.Count(item => item.TargetFound)
                    / (double)Math.Max(1, evaluated.Length),
                Median(targetValues));
        }

        double recentHitRate =
            evaluated.Count(item => item.TargetFound)
            / (double)evaluated.Length;
        double recentMedian = Median(targetValues);

        double adjustment = 0;
        if (recentHitRate < 0.25)
        {
            adjustment -= 4;
        }
        else if (recentHitRate > 0.55)
        {
            adjustment += 3;
        }

        if (recentMedian > baseline + 8)
        {
            adjustment += 2;
        }
        else if (recentMedian < baseline - 5)
        {
            adjustment -= 2;
        }

        if (session.CargoCapacity > 0)
        {
            int minedCargo = Math.Max(
                0,
                session.CargoUsed - session.LimpetsRemaining);
            double fill = minedCargo
                / (double)session.CargoCapacity;
            if (fill >= 0.85)
            {
                adjustment += 3;
            }
        }

        double suggested = Math.Clamp(
            baseline + adjustment,
            Math.Max(5, baseline - 8),
            Math.Min(60, baseline + 8));

        return new MiningAdaptiveThresholdAdvice(
            true,
            baseline,
            suggested,
            recentHitRate,
            recentMedian);
    }

    public static MiningLimpetAdvice CalculateLimpets(
        MiningSessionSnapshot session)
    {
        int remaining = Math.Max(0, session.LimpetsRemaining);
        if (!session.IsActive
            || session.RefinedTons < MinimumLimpetTons
            || session.CargoCapacity <= 0)
        {
            return new MiningLimpetAdvice(
                false,
                remaining,
                0,
                0,
                0,
                false,
                false);
        }

        int launched =
            Math.Max(
                0,
                session.ProspectorsLaunched
                + session.CollectorsLaunched);

        double usagePerTon =
            launched / (double)Math.Max(1, session.RefinedTons);

        int nonLimpetCargo =
            Math.Max(
                0,
                session.CargoUsed - remaining);

        int effectiveMineralRoom =
            Math.Max(
                0,
                session.CargoCapacity - nonLimpetCargo);

        int estimatedRequired =
            (int)Math.Ceiling(
                Math.Min(
                    remaining,
                    effectiveMineralRoom * usagePerTon));

        int reserve =
            (int)Math.Ceiling(
                estimatedRequired * 1.15);

        int safeExcess =
            Math.Max(
                0,
                remaining - reserve);

        bool low =
            remaining < Math.Max(
                8,
                estimatedRequired);

        bool critical =
            remaining <= 4
            || (estimatedRequired > 0
                && remaining < estimatedRequired * 0.6);

        return new MiningLimpetAdvice(
            true,
            remaining,
            usagePerTon,
            estimatedRequired,
            safeExcess,
            low,
            critical);
    }

    public static MiningFieldQuality CalculateFieldQuality(
        MiningSessionSnapshot session,
        string? targetCommodity,
        double threshold)
    {
        if (!session.IsActive
            || string.IsNullOrWhiteSpace(targetCommodity)
            || session.ProspectedAsteroids < 8)
        {
            return MiningFieldQuality.Unknown;
        }

        MiningTargetStatistics overall =
            MiningTargetAnalytics.Calculate(
                session,
                targetCommodity,
                threshold);

        MiningProspectSnapshot[] recentProspects =
            session.Prospects
                .TakeLast(Math.Min(12, session.ProspectedAsteroids))
                .ToArray();

        int targetBearing = recentProspects
            .Select(item => MiningProspectorAdvisor.Evaluate(
                item,
                targetCommodity,
                threshold))
            .Count(item => item.TargetFound);

        double recentHitRate =
            targetBearing
            / (double)Math.Max(1, recentProspects.Length);

        if (session.ProspectedAsteroids >= 20
            && recentHitRate + 0.12 < overall.HitRate)
        {
            return MiningFieldQuality.Declining;
        }

        if (recentHitRate >= 0.50)
        {
            return MiningFieldQuality.Good;
        }

        return MiningFieldQuality.Stable;
    }

    public static MiningLeaveAdvice CalculateLeave(
        MiningSessionSnapshot session,
        TimeSpan? eta)
    {
        if (!session.IsActive || session.CargoCapacity <= 0)
        {
            return new MiningLeaveAdvice(
                MiningLeaveRecommendation.Unknown,
                0,
                0,
                eta);
        }

        int physicalFreeCargo =
            Math.Max(
                0,
                session.CargoCapacity - session.CargoUsed);

        int nonLimpetCargo =
            Math.Max(
                0,
                session.CargoUsed - session.LimpetsRemaining);

        int effectiveMineralRoom =
            Math.Max(
                0,
                session.CargoCapacity - nonLimpetCargo);

        if (effectiveMineralRoom == 0)
        {
            return new MiningLeaveAdvice(
                MiningLeaveRecommendation.CargoFull,
                physicalFreeCargo,
                effectiveMineralRoom,
                eta);
        }

        double mineralFill =
            1 - effectiveMineralRoom
                / (double)session.CargoCapacity;

        MiningLeaveRecommendation recommendation =
            mineralFill >= 0.97 || effectiveMineralRoom <= 4
                ? MiningLeaveRecommendation.LeaveNow
                : mineralFill >= 0.90 || effectiveMineralRoom <= 12
                    ? MiningLeaveRecommendation.FinishCurrentRock
                    : MiningLeaveRecommendation.Continue;

        return new MiningLeaveAdvice(
            recommendation,
            physicalFreeCargo,
            effectiveMineralRoom,
            eta);
    }

    private static MiningHudPhase CalculatePhase(
        MiningSessionSnapshot session,
        MiningLeaveAdvice leave,
        DateTimeOffset now)
    {
        if (!session.IsActive)
        {
            return MiningHudPhase.Idle;
        }

        if (leave.Recommendation == MiningLeaveRecommendation.CargoFull)
        {
            return MiningHudPhase.Full;
        }

        if (leave.Recommendation
            is MiningLeaveRecommendation.LeaveNow
            or MiningLeaveRecommendation.FinishCurrentRock)
        {
            return MiningHudPhase.NearFull;
        }

        MiningProspectSnapshot? lastProspect =
            session.Prospects.LastOrDefault();

        if (lastProspect is not null
            && now - lastProspect.Timestamp <= TimeSpan.FromSeconds(8))
        {
            return MiningHudPhase.ProspectDecision;
        }

        MiningRefinementSnapshot? lastRefinement =
            session.Refinements.LastOrDefault();

        if (lastRefinement is not null
            && now - lastRefinement.Timestamp <= TimeSpan.FromSeconds(20))
        {
            return MiningHudPhase.Mining;
        }

        return MiningHudPhase.Searching;
    }

    private static double Median(double[] sorted)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1]
               + sorted[sorted.Length / 2]) / 2.0;
    }
}
