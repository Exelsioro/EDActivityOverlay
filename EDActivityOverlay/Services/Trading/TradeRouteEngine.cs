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
        ArgumentNullException.ThrowIfNull(
            origin);

        ArgumentNullException.ThrowIfNull(
            sourceOrders);

        ArgumentNullException.ThrowIfNull(
            targetOrders);

        ArgumentNullException.ThrowIfNull(
            constraints);

        constraints.Validate();

        if (maxResults < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults));
        }

        DateTimeOffset currentTime =
            now
            ?? DateTimeOffset.UtcNow;

        TradeMarketOrder[] sources =
            sourceOrders
                .GroupBy(
                    order =>
                        order.MarketId)
                .Select(
                    group =>
                        group.First())
                .Where(
                    order =>
                        IsUsableSource(
                            order,
                            origin,
                            constraints,
                            currentTime))
                .ToArray();

        TradeMarketOrder[] targets =
            targetOrders
                .GroupBy(
                    order =>
                        order.MarketId)
                .Select(
                    group =>
                        group.First())
                .Where(
                    order =>
                        IsUsableTarget(
                            order,
                            constraints,
                            currentTime))
                .ToArray();

        // The global result only keeps MaxResults routes. Therefore the global
        // Top-N can never require route N+1 from a single commodity: that
        // commodity alone already has N routes ranked above it. Keeping Top-N
        // per commodity is exact, not heuristic.
        var best =
            new PriorityQueue<
                TradeRouteCandidate,
                CandidatePriority>();

        foreach (TradeMarketOrder source
                 in sources)
        {
            double originToSource =
                Distance(
                    origin.X,
                    origin.Y,
                    origin.Z,
                    source.SystemX,
                    source.SystemY,
                    source.SystemZ);

            TimeSpan sourceAge =
                Age(
                    currentTime,
                    source.UpdatedAt);

            foreach (TradeMarketOrder target
                     in targets)
            {
                if (!source.CommodityName.Equals(
                        target.CommodityName,
                        StringComparison.OrdinalIgnoreCase)
                    || source.MarketId
                       == target.MarketId)
                {
                    continue;
                }

                double sourceToTarget =
                    Distance(
                        source.SystemX,
                        source.SystemY,
                        source.SystemZ,
                        target.SystemX,
                        target.SystemY,
                        target.SystemZ);

                if (sourceToTarget
                    > constraints.TargetSearchRadiusLy)
                {
                    continue;
                }

                int profitPerTon =
                    target.SellToStationPrice
                    - source.BuyFromStationPrice;

                if (profitPerTon <= 0)
                {
                    continue;
                }

                long usableDemand =
                    target.HasInfiniteDemand
                        ? constraints.CargoCapacity
                        : Math.Max(
                            0,
                            target.Demand);

                long amount =
                    Math.Min(
                        constraints.CargoCapacity,
                        Math.Min(
                            Math.Max(
                                0,
                                source.Stock),
                            usableDemand));

                if (amount <= 0)
                {
                    continue;
                }

                long profitPerTrip =
                    checked(
                        (long)profitPerTon
                        * amount);

                double totalDistance =
                    originToSource
                    + sourceToTarget;

                var priority =
                    new CandidatePriority(
                        profitPerTrip,
                        profitPerTon,
                        -totalDistance);

                if (best.Count >= maxResults
                    && best.TryPeek(
                        out _,
                        out CandidatePriority worst)
                    && priority.CompareTo(
                           worst)
                       <= 0)
                {
                    // The heap root is the worst retained route. Do not even
                    // allocate a TradeRouteCandidate unless this pair can enter
                    // the exact Top-N.
                    continue;
                }

                TimeSpan targetAge =
                    Age(
                        currentTime,
                        target.UpdatedAt);

                var candidate =
                    new TradeRouteCandidate
                    {
                        Source =
                            source,
                        Target =
                            target,
                        ProfitPerTon =
                            profitPerTon,
                        TradableAmount =
                            checked(
                                (int)Math.Min(
                                    amount,
                                    int.MaxValue)),
                        ProfitPerTrip =
                            profitPerTrip,
                        OriginToSourceDistanceLy =
                            originToSource,
                        SourceToTargetDistanceLy =
                            sourceToTarget,
                        SourceAge =
                            sourceAge,
                        TargetAge =
                            targetAge
                    };

                best.Enqueue(
                    candidate,
                    priority);

                if (best.Count > maxResults)
                {
                    _ =
                        best.Dequeue();
                }
            }
        }

        return
            best.UnorderedItems
                .Select(
                    item =>
                        item.Element)
                .OrderByDescending(
                    candidate =>
                        candidate.ProfitPerTrip)
                .ThenByDescending(
                    candidate =>
                        candidate.ProfitPerTon)
                .ThenBy(
                    candidate =>
                        candidate.TotalTravelDistanceLy)
                .ToArray();
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
