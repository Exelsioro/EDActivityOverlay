using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Mining;

public sealed record MiningYieldBucket(
    string Label,
    int Count,
    double Share);

public sealed record MiningSessionAnalyticsSnapshot(
    TimeSpan Duration,
    bool RateReady,
    double TonsPerHour,
    double RefinementsPerMinute,
    double ProspectsPerTon,
    double ProspectorsPerTon,
    double CoresPerHour,
    double CargoFill,
    TimeSpan? EstimatedTimeToFull,
    double TargetP75,
    MiningTargetStatistics Target,
    IReadOnlyList<MiningYieldBucket> YieldBuckets);

public sealed record MiningHistoryAnalyticsSnapshot(
    int Sessions,
    int RefinedTons,
    TimeSpan TotalDuration,
    double AverageTonsPerHour,
    double BestTonsPerHour,
    string BestLocation);

public static class MiningSessionAnalyticsCalculator
{
    public static readonly TimeSpan MinimumRateDuration = TimeSpan.FromMinutes(5);
    public const int MinimumRateTons = 5;

    public static MiningSessionAnalyticsSnapshot Calculate(
        MiningSessionSnapshot session,
        string? targetCommodity,
        double minimumProportion,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        TimeSpan duration = ResolveDuration(session, now ?? DateTimeOffset.UtcNow);
        bool rateReady = duration >= MinimumRateDuration
                         && session.RefinedTons >= MinimumRateTons;
        double hours = Math.Max(0, duration.TotalHours);
        double minutes = Math.Max(0, duration.TotalMinutes);
        double tonsPerHour = rateReady && hours > 0
            ? session.RefinedTons / hours
            : 0;
        double refinementsPerMinute = rateReady && minutes > 0
            ? session.RefinedTons / minutes
            : 0;
        double prospectsPerTon = session.RefinedTons > 0
            ? session.ProspectedAsteroids / (double)session.RefinedTons
            : 0;
        double prospectorsPerTon = session.RefinedTons > 0
            ? session.ProspectorsLaunched / (double)session.RefinedTons
            : 0;
        double coresPerHour = hours > 0
            ? session.CrackedAsteroids / hours
            : 0;
        double cargoFill = session.CargoCapacity > 0
            ? Math.Clamp(session.CargoUsed / (double)session.CargoCapacity, 0, 1)
            : 0;

        TimeSpan? eta = null;
        int cargoRemaining = Math.Max(0, session.CargoCapacity - session.CargoUsed);
        if (rateReady && tonsPerHour > 0 && cargoRemaining > 0)
        {
            eta = TimeSpan.FromHours(cargoRemaining / tonsPerHour);
        }

        MiningTargetStatistics target = MiningTargetAnalytics.Calculate(
            session,
            targetCommodity,
            minimumProportion);
        double[] proportions = ResolveTargetProportions(
            session,
            targetCommodity,
            minimumProportion);

        return new MiningSessionAnalyticsSnapshot(
            duration,
            rateReady,
            tonsPerHour,
            refinementsPerMinute,
            prospectsPerTon,
            prospectorsPerTon,
            coresPerHour,
            cargoFill,
            eta,
            Percentile(proportions, 0.75),
            target,
            BuildBuckets(proportions));
    }

    public static MiningHistoryAnalyticsSnapshot CalculateHistory(
        IEnumerable<MiningSessionSnapshot> sessions,
        string? targetCommodity,
        double minimumProportion)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        MiningSessionSnapshot[] finished = sessions
            .Where(item => item.State == MiningSessionState.Finished && item.HasMiningEvidence)
            .ToArray();
        if (finished.Length == 0)
        {
            return new MiningHistoryAnalyticsSnapshot(
                0,
                0,
                TimeSpan.Zero,
                0,
                0,
                string.Empty);
        }

        TimeSpan totalDuration = TimeSpan.FromTicks(
            finished.Sum(item => Math.Max(0, item.Duration.Ticks)));
        int refined = finished.Sum(item => item.RefinedTons);
        double average = totalDuration >= MinimumRateDuration
                         && refined >= MinimumRateTons
                         && totalDuration.TotalHours > 0
            ? refined / totalDuration.TotalHours
            : 0;

        var rates = finished
            .Select(item => new
            {
                Session = item,
                Analytics = Calculate(
                    item,
                    targetCommodity,
                    minimumProportion,
                    item.EndedUtc ?? item.LastActivityUtc)
            })
            .Where(item => item.Analytics.RateReady)
            .OrderByDescending(item => item.Analytics.TonsPerHour)
            .ToArray();

        var best = rates.FirstOrDefault();
        string bestLocation = best is null
            ? string.Empty
            : BuildLocation(best.Session);

        return new MiningHistoryAnalyticsSnapshot(
            finished.Length,
            refined,
            totalDuration,
            average,
            best?.Analytics.TonsPerHour ?? 0,
            bestLocation);
    }

    private static TimeSpan ResolveDuration(
        MiningSessionSnapshot session,
        DateTimeOffset now)
    {
        if (session.State == MiningSessionState.Idle)
        {
            return TimeSpan.Zero;
        }

        DateTimeOffset end = session.EndedUtc
                             ?? (session.IsActive ? now : session.LastActivityUtc);
        TimeSpan duration = end - session.StartedUtc;
        return duration < TimeSpan.Zero
            ? TimeSpan.Zero
            : duration;
    }

    private static double[] ResolveTargetProportions(
        MiningSessionSnapshot session,
        string? targetCommodity,
        double minimumProportion)
    {
        if (string.IsNullOrWhiteSpace(targetCommodity))
        {
            return Array.Empty<double>();
        }

        return session.Prospects
            .Select(item => MiningProspectorAdvisor.Evaluate(
                item,
                targetCommodity,
                minimumProportion))
            .Where(item => item.TargetProportion.HasValue)
            .Select(item => item.TargetProportion!.Value)
            .OrderBy(value => value)
            .ToArray();
    }

    private static IReadOnlyList<MiningYieldBucket> BuildBuckets(double[] values)
    {
        string[] labels = ["0–10%", "10–20%", "20–30%", "30–40%", "40–50%", "50%+"];
        int[] counts = new int[labels.Length];
        foreach (double value in values)
        {
            int index = Math.Clamp((int)(Math.Max(0, value) / 10), 0, counts.Length - 1);
            counts[index]++;
        }

        return labels
            .Select((label, index) => new MiningYieldBucket(
                label,
                counts[index],
                values.Length == 0 ? 0 : counts[index] / (double)values.Length))
            .ToArray();
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
        {
            return 0;
        }

        double position = Math.Clamp(percentile, 0, 1) * (sortedValues.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        double weight = position - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }

    private static string BuildLocation(MiningSessionSnapshot session)
    {
        return string.Join(
            " / ",
            new[] { session.SystemName, session.RingName, session.BodyName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
