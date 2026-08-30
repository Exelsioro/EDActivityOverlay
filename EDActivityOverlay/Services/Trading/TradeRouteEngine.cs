namespace EDActivityOverlay.Services.Trading;

public static class TradeRouteEngine
{
    public static IReadOnlyList<TradeRouteCandidate> BuildOneWayCandidates(
        TradeSystemLocation origin,
        IEnumerable<TradeMarketOrder> sourceOrders,
        IEnumerable<TradeMarketOrder> targetOrders,
        TradeSearchConstraints constraints,
        DateTimeOffset? now = null,
        int maxResults = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(sourceOrders);
        ArgumentNullException.ThrowIfNull(targetOrders);
        ArgumentNullException.ThrowIfNull(constraints);

        constraints.Validate();

        if (maxResults < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }

        DateTimeOffset currentTime = now ?? DateTimeOffset.UtcNow;

        TradeMarketOrder[] sources =
            sourceOrders
                .GroupBy(order => order.MarketId)
                .Select(group => group.First())
                .Where(order => IsUsableSource(order, origin, constraints, currentTime))
                .ToArray();

        TradeMarketOrder[] targets =
            targetOrders
                .GroupBy(order => order.MarketId)
                .Select(group => group.First())
                .Where(order => IsUsableTarget(order, constraints, currentTime))
                .ToArray();

        var bestProfit = new PriorityQueue<TradeRouteCandidate, CandidatePriority>();
        var bestPerTon = new PriorityQueue<TradeRouteCandidate, PerTonPriority>();
        var bestDistance = new PriorityQueue<TradeRouteCandidate, DistancePriority>();
        var bestFirstRun = new PriorityQueue<TradeRouteCandidate, FirstRunPriority>();
        var bestFreshness = new PriorityQueue<TradeRouteCandidate, FreshnessPriority>();

        foreach (TradeMarketOrder source in sources)
        {
            double originToSource = Distance(
                origin.X, origin.Y, origin.Z,
                source.SystemX, source.SystemY, source.SystemZ);

            TimeSpan sourceAge = Age(currentTime, source.UpdatedAt);

            foreach (TradeMarketOrder target in targets)
            {
                if (!source.CommodityName.Equals(
                        target.CommodityName,
                        StringComparison.OrdinalIgnoreCase)
                    || source.MarketId == target.MarketId)
                {
                    continue;
                }

                double sourceToTarget = Distance(
                    source.SystemX, source.SystemY, source.SystemZ,
                    target.SystemX, target.SystemY, target.SystemZ);

                if (sourceToTarget > constraints.TargetSearchRadiusLy)
                {
                    continue;
                }

                int profitPerTon =
                    target.SellToStationPrice - source.BuyFromStationPrice;

                if (profitPerTon <= 0)
                {
                    continue;
                }

                long usableDemand = target.HasInfiniteDemand
                    ? constraints.CargoCapacity
                    : Math.Max(0, target.Demand);

                long affordableAmount = constraints.AvailableCredits is { } availableCredits
                    ? Math.Max(0, availableCredits) / source.BuyFromStationPrice
                    : long.MaxValue;

                long amount = Math.Min(
                    constraints.CargoCapacity,
                    Math.Min(
                        Math.Max(0, source.Stock),
                        Math.Min(usableDemand, affordableAmount)));

                if (amount <= 0)
                {
                    continue;
                }

                long profitPerTrip = checked((long)profitPerTon * amount);
                TimeSpan targetAge = Age(currentTime, target.UpdatedAt);
                TimeSpan worstAge = sourceAge >= targetAge ? sourceAge : targetAge;

                double sourceArrival = source.DistanceToArrivalLs ?? 1_000_000d;
                double targetArrival = target.DistanceToArrivalLs ?? 1_000_000d;
                double firstRunBurden =
                    originToSource
                    + sourceToTarget
                    + sourceArrival / 50_000d
                    + targetArrival / 50_000d;

                var profitPriority =
                    new CandidatePriority(profitPerTrip, profitPerTon, -sourceToTarget);

                var perTonPriority =
                    new PerTonPriority(profitPerTon, profitPerTrip, -sourceToTarget);

                var distancePriority =
                    new DistancePriority(-sourceToTarget, profitPerTrip, profitPerTon);

                var firstRunPriority =
                    new FirstRunPriority(-firstRunBurden, profitPerTrip);

                var freshnessPriority =
                    new FreshnessPriority(-worstAge.Ticks, profitPerTrip);

                bool wantsProfit = CanEnter(bestProfit, profitPriority, maxResults);
                bool wantsPerTon =
                    constraints.DiversifyCandidatePool
                    && CanEnter(bestPerTon, perTonPriority, maxResults);
                bool wantsDistance =
                    constraints.DiversifyCandidatePool
                    && CanEnter(bestDistance, distancePriority, maxResults);
                bool wantsFirstRun =
                    constraints.DiversifyCandidatePool
                    && CanEnter(bestFirstRun, firstRunPriority, maxResults);
                bool wantsFreshness =
                    constraints.DiversifyCandidatePool
                    && CanEnter(bestFreshness, freshnessPriority, maxResults);

                if (!wantsProfit
                    && !wantsPerTon
                    && !wantsDistance
                    && !wantsFirstRun
                    && !wantsFreshness)
                {
                    continue;
                }

                var candidate = new TradeRouteCandidate
                {
                    Source = source,
                    Target = target,
                    ProfitPerTon = profitPerTon,
                    TradableAmount = checked((int)Math.Min(amount, int.MaxValue)),
                    ProfitPerTrip = profitPerTrip,
                    OriginToSourceDistanceLy = originToSource,
                    SourceToTargetDistanceLy = sourceToTarget,
                    SourceAge = sourceAge,
                    TargetAge = targetAge
                };

                if (wantsProfit)
                    EnqueueBounded(bestProfit, candidate, profitPriority, maxResults);
                if (wantsPerTon)
                    EnqueueBounded(bestPerTon, candidate, perTonPriority, maxResults);
                if (wantsDistance)
                    EnqueueBounded(bestDistance, candidate, distancePriority, maxResults);
                if (wantsFirstRun)
                    EnqueueBounded(bestFirstRun, candidate, firstRunPriority, maxResults);
                if (wantsFreshness)
                    EnqueueBounded(bestFreshness, candidate, freshnessPriority, maxResults);
            }
        }

        IEnumerable<TradeRouteCandidate> retained =
            bestProfit.UnorderedItems.Select(item => item.Element);

        if (!constraints.DiversifyCandidatePool)
        {
            return retained
                .OrderByDescending(candidate => candidate.ProfitPerTrip)
                .ThenByDescending(candidate => candidate.ProfitPerTon)
                .ThenBy(candidate => candidate.SourceToTargetDistanceLy)
                .ToArray();
        }

        retained = retained
            .Concat(bestPerTon.UnorderedItems.Select(item => item.Element))
            .Concat(bestDistance.UnorderedItems.Select(item => item.Element))
            .Concat(bestFirstRun.UnorderedItems.Select(item => item.Element))
            .Concat(bestFreshness.UnorderedItems.Select(item => item.Element));

        return TradeCandidateRetention.SelectDiversified(retained, maxResults);
    }
    private static bool IsUsableSource(
        TradeMarketOrder order,
        TradeSystemLocation origin,
        TradeSearchConstraints constraints,
        DateTimeOffset now)
    {
        if (!IsUsableStation(
                order,
                constraints,
                now))
        {
            return
                false;
        }

        if (order.BuyFromStationPrice <= 0
            || order.Stock
               < constraints.MinSupply)
        {
            return
                false;
        }

        double originToSource =
            Distance(
                origin.X,
                origin.Y,
                origin.Z,
                order.SystemX,
                order.SystemY,
                order.SystemZ);

        return
            originToSource
            <= constraints.SourceSearchRadiusLy;
    }

    private static bool IsUsableTarget(
        TradeMarketOrder order,
        TradeSearchConstraints constraints,
        DateTimeOffset now)
    {
        if (!IsUsableStation(
                order,
                constraints,
                now))
        {
            return
                false;
        }

        if (order.SellToStationPrice <= 0)
        {
            return
                false;
        }

        return
            order.HasInfiniteDemand
            || order.Demand
               >= constraints.MinDemand;
    }

    private static bool IsUsableStation(
        TradeMarketOrder order,
        TradeSearchConstraints constraints,
        DateTimeOffset now)
    {
        if (!constraints.IncludeFleetCarriers
            && order.IsFleetCarrier)
        {
            return
                false;
        }

        if (order.MaxLandingPadSize
            < constraints.MinLandingPadSize)
        {
            return
                false;
        }

        if (constraints.MaxStationDistanceLs
            is { } maxDistance)
        {
            if (order.DistanceToArrivalLs
                is not { } stationDistance
                || stationDistance > maxDistance)
            {
                return
                    false;
            }
        }

        if (order.UpdatedAt
            == DateTimeOffset.MinValue)
        {
            return
                false;
        }

        return
            Age(
                now,
                order.UpdatedAt)
            <= constraints.MaxDataAge;
    }

    private static TimeSpan Age(
        DateTimeOffset now,
        DateTimeOffset updatedAt)
    {
        TimeSpan age =
            now
            - updatedAt;

        return
            age < TimeSpan.Zero
                ? TimeSpan.Zero
                : age;
    }

    private static double Distance(
        double x1,
        double y1,
        double z1,
        double x2,
        double y2,
        double z2)
    {
        double dx =
            x1
            - x2;

        double dy =
            y1
            - y2;

        double dz =
            z1
            - z2;

        return
            Math.Sqrt(
                dx * dx
                + dy * dy
                + dz * dz);
    }

    private static bool CanEnter<TPriority>(
        PriorityQueue<TradeRouteCandidate, TPriority> queue,
        TPriority priority,
        int maxResults)
        where TPriority : IComparable<TPriority>
    {
        if (queue.Count < maxResults)
        {
            return true;
        }

        return queue.TryPeek(out _, out TPriority worst)
               && priority.CompareTo(worst) > 0;
    }

    private static void EnqueueBounded<TPriority>(
        PriorityQueue<TradeRouteCandidate, TPriority> queue,
        TradeRouteCandidate candidate,
        TPriority priority,
        int maxResults)
        where TPriority : IComparable<TPriority>
    {
        queue.Enqueue(candidate, priority);

        if (queue.Count > maxResults)
        {
            _ = queue.Dequeue();
        }
    }

    private readonly record struct PerTonPriority(
        int ProfitPerTon,
        long ProfitPerTrip,
        double NegativeDistance)
        : IComparable<PerTonPriority>
    {
        public int CompareTo(PerTonPriority other)
        {
            int perTon = ProfitPerTon.CompareTo(other.ProfitPerTon);
            if (perTon != 0) return perTon;

            int profit = ProfitPerTrip.CompareTo(other.ProfitPerTrip);
            if (profit != 0) return profit;

            return NegativeDistance.CompareTo(other.NegativeDistance);
        }
    }

    private readonly record struct DistancePriority(
        double NegativeDistance,
        long ProfitPerTrip,
        int ProfitPerTon)
        : IComparable<DistancePriority>
    {
        public int CompareTo(DistancePriority other)
        {
            int distance = NegativeDistance.CompareTo(other.NegativeDistance);
            if (distance != 0) return distance;

            int profit = ProfitPerTrip.CompareTo(other.ProfitPerTrip);
            if (profit != 0) return profit;

            return ProfitPerTon.CompareTo(other.ProfitPerTon);
        }
    }

    private readonly record struct FirstRunPriority(
        double NegativeBurden,
        long ProfitPerTrip)
        : IComparable<FirstRunPriority>
    {
        public int CompareTo(FirstRunPriority other)
        {
            int burden = NegativeBurden.CompareTo(other.NegativeBurden);
            return burden != 0
                ? burden
                : ProfitPerTrip.CompareTo(other.ProfitPerTrip);
        }
    }

    private readonly record struct FreshnessPriority(
        long NegativeWorstAgeTicks,
        long ProfitPerTrip)
        : IComparable<FreshnessPriority>
    {
        public int CompareTo(FreshnessPriority other)
        {
            int age = NegativeWorstAgeTicks.CompareTo(other.NegativeWorstAgeTicks);
            return age != 0
                ? age
                : ProfitPerTrip.CompareTo(other.ProfitPerTrip);
        }
    }
    private readonly record struct CandidatePriority(

        long ProfitPerTrip,
        int ProfitPerTon,
        double NegativeTotalDistance)
        : IComparable<CandidatePriority>
    {
        public int CompareTo(
            CandidatePriority other)
        {
            int profit =
                ProfitPerTrip.CompareTo(
                    other.ProfitPerTrip);

            if (profit != 0)
            {
                return
                    profit;
            }

            int perTon =
                ProfitPerTon.CompareTo(
                    other.ProfitPerTon);

            if (perTon != 0)
            {
                return
                    perTon;
            }

            return
                NegativeTotalDistance.CompareTo(
                    other.NegativeTotalDistance);
        }
    }
}
