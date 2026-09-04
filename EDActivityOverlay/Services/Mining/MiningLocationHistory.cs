using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Mining;

public sealed record MiningLocationRefinedCommodity(
    string CommodityId,
    int Tons,
    double Share);

public sealed record MiningLocationHistorySnapshot
{
    public int Sessions { get; init; }
    public int RateSessions { get; init; }
    public int RefinedTons { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public double AverageTonsPerHour { get; init; }
    public double BestTonsPerHour { get; init; }
    public int ProspectedAsteroids { get; init; }
    public int TargetBearingAsteroids { get; init; }
    public double HitRate { get; init; }
    public double AverageTargetContentPercent { get; init; }
    public DateTimeOffset? LastSessionUtc { get; init; }
    public IReadOnlyList<MiningLocationRefinedCommodity> RefinedComposition { get; init; } =
        Array.Empty<MiningLocationRefinedCommodity>();

    public static MiningLocationHistorySnapshot Empty { get; } = new();

    public bool Available => Sessions > 0;

    // Do not replace a larger external survey with a one-rock anecdote.
    // Five prospectors / three target-bearing rocks is deliberately only a
    // minimum credibility gate; the raw personal history is still displayed
    // before this threshold is reached.
    public bool HasQualitySignal =>
        ProspectedAsteroids >= 5
        && TargetBearingAsteroids >= 3
        && AverageTargetContentPercent > 0;
}

public interface IMiningLocationHistoryProvider
{
    IReadOnlyList<MiningSessionSnapshot> LoadRecent();
}

internal sealed class MiningSessionLocationHistoryProvider :
    IMiningLocationHistoryProvider
{
    private const int HistoryLimit = 500;

    public IReadOnlyList<MiningSessionSnapshot> LoadRecent() =>
        MiningSessionService.Instance.LoadRecentSessions(HistoryLimit);
}

internal sealed class NullMiningLocationHistoryProvider :
    IMiningLocationHistoryProvider
{
    public IReadOnlyList<MiningSessionSnapshot> LoadRecent() =>
        Array.Empty<MiningSessionSnapshot>();
}

public static class MiningLocationHistoryCalculator
{
    public static IReadOnlyDictionary<string, MiningLocationHistorySnapshot>
        CalculateByLocation(
            IEnumerable<MiningSessionSnapshot> sessions,
            IReadOnlyList<string> targetCommodityIds)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(targetCommodityIds);

        string[] selected = targetCommodityIds
            .Select(id => MiningTargetCatalog.Find(id)?.CommodityId ?? id?.Trim() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return sessions
            .Where(IsUsableSession)
            .Where(session => SessionTargetsMatch(
                session.DestinationContext,
                selected))
            .GroupBy(
                session => MiningLocationKey.For(
                    session.DestinationContext.SystemName,
                    session.DestinationContext.RingName),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group => Calculate(group.ToArray(), selected),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsUsableSession(MiningSessionSnapshot session) =>
        session.State == MiningSessionState.Finished
        && session.HasMiningEvidence
        && session.DestinationContext.Available
        && session.DestinationContext.Confirmed;

    private static bool SessionTargetsMatch(
        MiningSessionDestinationContext context,
        IReadOnlyList<string> selected)
    {
        if (selected.Count == 0)
        {
            return true;
        }

        if (selected.Contains(
                context.PrimaryCommodityId,
                StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return context.TargetCommodityIds.Any(
            id => selected.Contains(id, StringComparer.OrdinalIgnoreCase));
    }

    private static MiningLocationHistorySnapshot Calculate(
        IReadOnlyList<MiningSessionSnapshot> sessions,
        IReadOnlyList<string> selected)
    {
        var rateRows = sessions
            .Select(session => new
            {
                Session = session,
                Analytics = MiningSessionAnalyticsCalculator.Calculate(
                    session,
                    targetCommodity: null,
                    minimumProportion: 0,
                    session.EndedUtc ?? session.LastActivityUtc)
            })
            .Where(row => row.Analytics.RateReady)
            .ToArray();

        TimeSpan totalDuration = TimeSpan.FromTicks(
            sessions.Sum(session => Math.Max(0, session.Duration.Ticks)));

        TimeSpan rateDuration = TimeSpan.FromTicks(
            rateRows.Sum(row => Math.Max(0, row.Analytics.Duration.Ticks)));

        int rateTons = rateRows.Sum(row => row.Session.RefinedTons);
        double averageRate =
            rateDuration.TotalHours > 0
                ? rateTons / rateDuration.TotalHours
                : 0;

        double bestRate = rateRows
            .Select(row => row.Analytics.TonsPerHour)
            .DefaultIfEmpty(0)
            .Max();

        MiningProspectSnapshot[] prospects = sessions
            .SelectMany(session => session.Prospects)
            .ToArray();

        double[] targetContents = prospects
            .Select(prospect => BestTargetContent(prospect, selected))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        int totalRefined = sessions.Sum(session => session.RefinedTons);
        MiningLocationRefinedCommodity[] composition = sessions
            .SelectMany(session => session.Refinements)
            .Where(item => !string.IsNullOrWhiteSpace(item.CommodityId))
            .GroupBy(
                item => item.CommodityId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new MiningLocationRefinedCommodity(
                group.Key,
                group.Count(),
                totalRefined > 0
                    ? group.Count() / (double)totalRefined
                    : 0))
            .OrderByDescending(item => item.Tons)
            .ThenBy(item => item.CommodityId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MiningLocationHistorySnapshot
        {
            Sessions = sessions.Count,
            RateSessions = rateRows.Length,
            RefinedTons = totalRefined,
            TotalDuration = totalDuration,
            AverageTonsPerHour = averageRate,
            BestTonsPerHour = bestRate,
            ProspectedAsteroids = prospects.Length,
            TargetBearingAsteroids = targetContents.Length,
            HitRate = prospects.Length > 0
                ? targetContents.Length / (double)prospects.Length
                : 0,
            AverageTargetContentPercent = targetContents.Length > 0
                ? targetContents.Average()
                : 0,
            LastSessionUtc = sessions
                .Select(session => session.EndedUtc ?? session.LastActivityUtc)
                .DefaultIfEmpty()
                .Max(),
            RefinedComposition = composition
        };
    }

    private static double? BestTargetContent(
        MiningProspectSnapshot prospect,
        IReadOnlyList<string> selected)
    {
        if (selected.Count == 0)
        {
            return null;
        }

        double[] matches = prospect.Materials
            .Where(material => selected.Contains(
                material.CommodityId,
                StringComparer.OrdinalIgnoreCase))
            .Select(material => Math.Max(0, material.Proportion))
            .ToArray();

        return matches.Length == 0
            ? null
            : matches.Max();
    }
}
