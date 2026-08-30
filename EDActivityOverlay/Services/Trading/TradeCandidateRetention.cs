namespace EDActivityOverlay.Services.Trading;

public static class TradeCandidateRetention
{
    public static IReadOnlyList<TradeRouteCandidate> SelectDiversified(
        IEnumerable<TradeRouteCandidate> candidates,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (maxResults < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }

        TradeRouteCandidate[] distinct =
            candidates
                .GroupBy(Key, StringComparer.Ordinal)
                .Select(group =>
                    group
                        .OrderByDescending(item => item.ProfitPerTrip)
                        .ThenByDescending(item => item.ProfitPerTon)
                        .First())
                .ToArray();

        if (distinct.Length <= maxResults)
        {
            return DefaultOrder(distinct);
        }

        // Profit/trip deliberately appears twice. It remains the dominant
        // signal, but short/fresh/high-margin routes cannot disappear before
        // UI-side travel-time ranking gets a chance to evaluate them.
        TradeRouteCandidate[][] rankings =
        {
            distinct
                .OrderByDescending(item => item.ProfitPerTrip)
                .ThenByDescending(item => item.ProfitPerTon)
                .ThenBy(item => item.SourceToTargetDistanceLy)
                .ToArray(),
            distinct
                .OrderBy(item => item.SourceToTargetDistanceLy)
                .ThenBy(item => item.Target.DistanceToArrivalLs ?? double.MaxValue)
                .ThenByDescending(item => item.ProfitPerTrip)
                .ToArray(),
            distinct
                .OrderByDescending(item => item.ProfitPerTrip)
                .ThenByDescending(item => item.ProfitPerTon)
                .ToArray(),
            distinct
                .OrderByDescending(item => item.ProfitPerTon)
                .ThenByDescending(item => item.ProfitPerTrip)
                .ToArray(),
            distinct
                .OrderBy(FirstRunBurden)
                .ThenByDescending(item => item.ProfitPerTrip)
                .ToArray(),
            distinct
                .OrderBy(item => item.WorstDataAge)
                .ThenByDescending(item => item.ProfitPerTrip)
                .ToArray(),
            distinct
                .OrderByDescending(item =>
                    TradeRouteConfidenceCalculator.Evaluate(
                        item,
                        item.TradableAmount)
                    .Score)
                .ThenByDescending(item => item.ProfitPerTrip)
                .ToArray()
        };

        int[] positions = new int[rankings.Length];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<TradeRouteCandidate>(maxResults);

        while (selected.Count < maxResults)
        {
            bool addedAny = false;

            for (int rankingIndex = 0;
                 rankingIndex < rankings.Length && selected.Count < maxResults;
                 rankingIndex++)
            {
                TradeRouteCandidate[] ranking = rankings[rankingIndex];

                while (positions[rankingIndex] < ranking.Length)
                {
                    TradeRouteCandidate candidate = ranking[positions[rankingIndex]++];

                    if (!seen.Add(Key(candidate)))
                    {
                        continue;
                    }

                    selected.Add(candidate);
                    addedAny = true;
                    break;
                }
            }

            if (!addedAny)
            {
                break;
            }
        }

        return DefaultOrder(selected);
    }

    private static IReadOnlyList<TradeRouteCandidate> DefaultOrder(
        IEnumerable<TradeRouteCandidate> candidates) =>
        candidates
            .OrderByDescending(item => item.ProfitPerTrip)
            .ThenByDescending(item => item.ProfitPerTon)
            .ThenBy(item => item.SourceToTargetDistanceLy)
            .ToArray();

    private static double FirstRunBurden(TradeRouteCandidate candidate)
    {
        double sourceArrival = candidate.Source.DistanceToArrivalLs ?? 1_000_000d;
        double targetArrival = candidate.Target.DistanceToArrivalLs ?? 1_000_000d;

        // Retention-only proxy. Exact time remains the job of
        // TradeTravelTimeEstimator once the Journal ship profile is available.
        return candidate.TotalTravelDistanceLy
               + sourceArrival / 50_000d
               + targetArrival / 50_000d;
    }

    private static string Key(TradeRouteCandidate candidate) =>
        $"{candidate.Source.MarketId}:{candidate.Target.MarketId}:"
        + candidate.Source.CommodityName.ToLowerInvariant();
}