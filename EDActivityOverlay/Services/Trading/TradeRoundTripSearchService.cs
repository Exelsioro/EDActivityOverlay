using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace EDActivityOverlay.Services.Trading;

public sealed class TradeRoundTripSearchService
{
    private const int DefaultSeedLimit = 40;
    private const int MaxOutboundSeedsPerStationPair = 3;
    private const int PairConcurrency = 4;

    private readonly ITradeSystemTradeSidesProvider sideProvider;
    private readonly TradeSearchService oneWaySearch;

    public TradeRoundTripSearchService()
        : this(new ArdentMarketDataProvider())
    {
    }

    public TradeRoundTripSearchService(ITradeDataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        sideProvider = provider as ITradeSystemTradeSidesProvider
            ?? throw new ArgumentException(
                "Round-trip search requires system import/export market access.",
                nameof(provider));

        oneWaySearch = new TradeSearchService(provider);
    }

    public async IAsyncEnumerable<TradeRoundTripSearchProgress> SearchProgressAsync(
        TradeSearchConstraints constraints,
        int seedLimit = DefaultSeedLimit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        constraints.Validate();

        if (seedLimit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(seedLimit));
        }

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<TradeRouteCandidate> outbound = Array.Empty<TradeRouteCandidate>();
        int outboundCompleted = 0;
        int outboundTotal = 0;

        await foreach (TradeSearchProgress progress
                       in oneWaySearch.SearchProgressAsync(constraints, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            outboundCompleted = progress.CompletedCommodities;
            outboundTotal = progress.TotalCommodities;

            if (progress.BestCandidates.Count > 0)
            {
                outbound = progress.BestCandidates;
            }

            if (progress.Stage != TradeSearchStage.Completed)
            {
                yield return new TradeRoundTripSearchProgress
                {
                    Stage = TradeRoundTripSearchStage.DiscoveringOutbound,
                    CompletedOutboundCommodities = outboundCompleted,
                    TotalOutboundCommodities = outboundTotal,
                    PotentialOutboundRoutes = outbound.Count,
                    Elapsed = stopwatch.Elapsed
                };
            }
        }

        TradeRouteCandidate[] seeds = BuildSeeds(outbound, seedLimit);

        if (seeds.Length == 0)
        {
            yield return new TradeRoundTripSearchProgress
            {
                Stage = TradeRoundTripSearchStage.Completed,
                CompletedOutboundCommodities = outboundCompleted,
                TotalOutboundCommodities = outboundTotal,
                PotentialOutboundRoutes = outbound.Count,
                CompletedPairs = 0,
                TotalPairs = 0,
                BestCandidates = Array.Empty<TradeRoundTripCandidate>(),
                Elapsed = stopwatch.Elapsed
            };
            yield break;
        }

        var exportsCache = new ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>>();
        var importsCache = new ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>>();

        using var gate = new SemaphoreSlim(PairConcurrency, PairConcurrency);
        List<Task<PairOutcome>> pending = seeds
            .Select(seed => EnrichPairBoundedAsync(
                seed,
                constraints,
                exportsCache,
                importsCache,
                gate,
                cancellationToken))
            .ToList();

        IReadOnlyList<TradeRoundTripCandidate> best = Array.Empty<TradeRoundTripCandidate>();
        int completed = 0;
        int failed = 0;

        yield return new TradeRoundTripSearchProgress
        {
            Stage = TradeRoundTripSearchStage.EnrichingPairs,
            CompletedOutboundCommodities = outboundCompleted,
            TotalOutboundCommodities = outboundTotal,
            PotentialOutboundRoutes = outbound.Count,
            CompletedPairs = 0,
            TotalPairs = seeds.Length,
            BestCandidates = best,
            Elapsed = stopwatch.Elapsed
        };

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<PairOutcome> finished = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(finished);
            PairOutcome outcome = await finished.ConfigureAwait(false);
            completed++;

            if (outcome.Error is not null)
            {
                failed++;
            }
            else if (outcome.Candidate is not null)
            {
                best = MergeBest(best, outcome.Candidate, constraints.MaxResults);
            }

            yield return new TradeRoundTripSearchProgress
            {
                Stage = TradeRoundTripSearchStage.EnrichingPairs,
                CompletedOutboundCommodities = outboundCompleted,
                TotalOutboundCommodities = outboundTotal,
                PotentialOutboundRoutes = outbound.Count,
                CompletedPairs = completed,
                TotalPairs = seeds.Length,
                FailedPairs = failed,
                BestCandidates = best,
                Elapsed = stopwatch.Elapsed
            };
        }

        yield return new TradeRoundTripSearchProgress
        {
            Stage = TradeRoundTripSearchStage.Completed,
            CompletedOutboundCommodities = outboundCompleted,
            TotalOutboundCommodities = outboundTotal,
            PotentialOutboundRoutes = outbound.Count,
            CompletedPairs = completed,
            TotalPairs = seeds.Length,
            FailedPairs = failed,
            BestCandidates = best,
            Elapsed = stopwatch.Elapsed
        };
    }

    private static TradeRouteCandidate[] BuildSeeds(
        IReadOnlyList<TradeRouteCandidate> candidates,
        int seedLimit)
    {
        TradeRouteCandidate[] perPair =
            candidates
                .GroupBy(candidate => (candidate.Source.MarketId, candidate.Target.MarketId))
                .SelectMany(group => group
                    .OrderByDescending(candidate => candidate.ProfitPerTrip)
                    .ThenByDescending(candidate => candidate.ProfitPerTon)
                    .Take(MaxOutboundSeedsPerStationPair))
                .ToArray();

        return TradeCandidateRetention
            .SelectDiversified(perPair, seedLimit)
            .ToArray();
    }
    private async Task<PairOutcome> EnrichPairBoundedAsync(
        TradeRouteCandidate outbound,
        TradeSearchConstraints constraints,
        ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>> exportsCache,
        ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>> importsCache,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                TradeRoundTripCandidate? candidate = await EnrichPairAsync(
                        outbound,
                        constraints,
                        exportsCache,
                        importsCache,
                        cancellationToken)
                    .ConfigureAwait(false);

                return new PairOutcome(candidate, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Logger.Warning(
                    $"Round-trip enrichment failed for "
                    + $"{outbound.Source.SystemName}/{outbound.Source.StationName} -> "
                    + $"{outbound.Target.SystemName}/{outbound.Target.StationName}: {ex.Message}");

                return new PairOutcome(null, ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TradeRoundTripCandidate?> EnrichPairAsync(
        TradeRouteCandidate outbound,
        TradeSearchConstraints constraints,
        ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>> exportsCache,
        ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>> importsCache,
        CancellationToken cancellationToken)
    {
        if (outbound.Source.SystemAddress == 0 || outbound.Target.SystemAddress == 0)
        {
            return null;
        }

        // Return leg is B -> A. Ask Ardent for the two directional market sets
        // instead of the generic /commodities payload:
        //   B exports: commodities the commander can buy at B.
        //   A imports: commodities the commander can sell at A.
        Task<IReadOnlyList<TradeMarketOrder>> targetExportsTask = GetSystemExportsAsync(
            outbound.Target,
            constraints,
            exportsCache,
            cancellationToken);

        Task<IReadOnlyList<TradeMarketOrder>> sourceImportsTask = GetSystemImportsAsync(
            outbound.Source,
            constraints,
            importsCache,
            cancellationToken);

        await Task.WhenAll(targetExportsTask, sourceImportsTask).ConfigureAwait(false);

        TradeMarketOrder[] targetStationExports =
            (await targetExportsTask.ConfigureAwait(false))
                .Where(order => order.MarketId == outbound.Target.MarketId)
                .Select(order => WithKnownStationMetadata(order, outbound.Target))
                .ToArray();

        TradeMarketOrder[] sourceStationImports =
            (await sourceImportsTask.ConfigureAwait(false))
                .Where(order => order.MarketId == outbound.Source.MarketId)
                .Select(order => WithKnownStationMetadata(order, outbound.Source))
                .ToArray();

        if (targetStationExports.Length == 0 || sourceStationImports.Length == 0)
        {
            Logger.Logger.Info(
                $"Round-trip pair has no exact-market side rows: "
                + $"B exports={targetStationExports.Length}, A imports={sourceStationImports.Length}, "
                + $"A={outbound.Source.MarketId}, B={outbound.Target.MarketId}");
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TradeRoundTripCandidate? best = null;
        int commodityMatches = 0;

        long? creditsAtTarget = null;
        if (constraints.AvailableCredits is { } startingCredits)
        {
            long outboundCost = checked(
                (long)outbound.Source.BuyFromStationPrice * outbound.TradableAmount);
            long outboundRevenue = checked(
                (long)outbound.Target.SellToStationPrice * outbound.TradableAmount);

            creditsAtTarget = Math.Max(
                0,
                checked(startingCredits - outboundCost + outboundRevenue));
        }

        foreach (TradeMarketOrder returnSource in targetStationExports)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (returnSource.CommodityName.Equals(
                    outbound.Source.CommodityName,
                    StringComparison.OrdinalIgnoreCase)
                || !IsUsableReturnSource(returnSource, constraints, now))
            {
                continue;
            }

            foreach (TradeMarketOrder returnTarget in sourceStationImports)
            {
                if (!returnTarget.CommodityName.Equals(
                        returnSource.CommodityName,
                        StringComparison.OrdinalIgnoreCase)
                    || !IsUsableReturnTarget(returnTarget, constraints, now))
                {
                    continue;
                }

                commodityMatches++;

                int profitPerTon =
                    returnTarget.SellToStationPrice - returnSource.BuyFromStationPrice;

                if (profitPerTon <= 0)
                {
                    continue;
                }

                long usableDemand = returnTarget.HasInfiniteDemand
                    ? constraints.CargoCapacity
                    : Math.Max(0, returnTarget.Demand);

                long affordableAmount = creditsAtTarget is { } credits
                    ? credits / returnSource.BuyFromStationPrice
                    : long.MaxValue;

                long amount = Math.Min(
                    constraints.CargoCapacity,
                    Math.Min(
                        Math.Max(0, returnSource.Stock),
                        Math.Min(usableDemand, affordableAmount)));

                if (amount <= 0)
                {
                    continue;
                }

                long profitPerTrip = checked((long)profitPerTon * amount);

                var candidate = new TradeRoundTripCandidate
                {
                    Outbound = outbound,
                    ReturnSource = returnSource,
                    ReturnTarget = returnTarget,
                    ReturnProfitPerTon = profitPerTon,
                    ReturnTradableAmount = checked((int)Math.Min(amount, int.MaxValue)),
                    ReturnProfitPerTrip = profitPerTrip,
                    ReturnSourceAge = Age(now, returnSource.UpdatedAt),
                    ReturnTargetAge = Age(now, returnTarget.UpdatedAt)
                };

                if (best is null || IsBetterReturn(candidate, best))
                {
                    best = candidate;
                }
            }
        }

        if (best is null)
        {
            Logger.Logger.Info(
                $"Round-trip pair produced no profitable return: "
                + $"B exports={targetStationExports.Length}, A imports={sourceStationImports.Length}, "
                + $"commodity matches={commodityMatches}, "
                + $"{outbound.Source.SystemName}/{outbound.Source.StationName} <-> "
                + $"{outbound.Target.SystemName}/{outbound.Target.StationName}");
        }

        return best;
    }

    private Task<IReadOnlyList<TradeMarketOrder>> GetSystemExportsAsync(
        TradeMarketOrder order,
        TradeSearchConstraints constraints,
        ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>> cache,
        CancellationToken cancellationToken) =>
        cache.GetOrAdd(
            order.SystemAddress,
            _ => sideProvider.GetSystemExportsAsync(
                ToLocation(order),
                constraints,
                cancellationToken));

    private Task<IReadOnlyList<TradeMarketOrder>> GetSystemImportsAsync(
        TradeMarketOrder order,
        TradeSearchConstraints constraints,
        ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>> cache,
        CancellationToken cancellationToken) =>
        cache.GetOrAdd(
            order.SystemAddress,
            _ => sideProvider.GetSystemImportsAsync(
                ToLocation(order),
                constraints,
                cancellationToken));

    private static TradeSystemLocation ToLocation(TradeMarketOrder order) =>
        new(
            order.SystemAddress,
            order.SystemName,
            order.SystemX,
            order.SystemY,
            order.SystemZ);

    private static TradeMarketOrder WithKnownStationMetadata(
        TradeMarketOrder order,
        TradeMarketOrder knownStation) =>
        order with
        {
            StationName = string.IsNullOrWhiteSpace(order.StationName)
                ? knownStation.StationName
                : order.StationName,
            StationType = string.IsNullOrWhiteSpace(order.StationType)
                ? knownStation.StationType
                : order.StationType,
            DistanceToArrivalLs = order.DistanceToArrivalLs
                ?? knownStation.DistanceToArrivalLs,
            MaxLandingPadSize = order.MaxLandingPadSize > 0
                ? order.MaxLandingPadSize
                : knownStation.MaxLandingPadSize,
            SystemAddress = order.SystemAddress != 0
                ? order.SystemAddress
                : knownStation.SystemAddress,
            SystemName = string.IsNullOrWhiteSpace(order.SystemName)
                ? knownStation.SystemName
                : order.SystemName,
            SystemX = order.SystemX != 0 ? order.SystemX : knownStation.SystemX,
            SystemY = order.SystemY != 0 ? order.SystemY : knownStation.SystemY,
            SystemZ = order.SystemZ != 0 ? order.SystemZ : knownStation.SystemZ
        };

    private static bool IsUsableReturnSource(
        TradeMarketOrder order,
        TradeSearchConstraints constraints,
        DateTimeOffset now) =>
        IsUsableStation(order, constraints, now)
        && order.BuyFromStationPrice > 0
        && order.Stock >= constraints.MinSupply;

    private static bool IsUsableReturnTarget(
        TradeMarketOrder order,
        TradeSearchConstraints constraints,
        DateTimeOffset now) =>
        IsUsableStation(order, constraints, now)
        && order.SellToStationPrice > 0
        && (order.HasInfiniteDemand || order.Demand >= constraints.MinDemand);

    private static bool IsUsableStation(
        TradeMarketOrder order,
        TradeSearchConstraints constraints,
        DateTimeOffset now)
    {
        if (!constraints.IncludeFleetCarriers && order.IsFleetCarrier)
        {
            return false;
        }

        if (order.MaxLandingPadSize < constraints.MinLandingPadSize)
        {
            return false;
        }

        if (constraints.MaxStationDistanceLs is { } maxDistance)
        {
            if (order.DistanceToArrivalLs is not { } distance || distance > maxDistance)
            {
                return false;
            }
        }

        if (order.UpdatedAt == DateTimeOffset.MinValue)
        {
            return false;
        }

        return Age(now, order.UpdatedAt) <= constraints.MaxDataAge;
    }

    private static TimeSpan Age(DateTimeOffset now, DateTimeOffset updatedAt)
    {
        TimeSpan age = now - updatedAt;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    private static bool IsBetterReturn(
        TradeRoundTripCandidate candidate,
        TradeRoundTripCandidate current) =>
        candidate.ProfitPerCycle > current.ProfitPerCycle
        || (candidate.ProfitPerCycle == current.ProfitPerCycle
            && candidate.CombinedProfitPerTon > current.CombinedProfitPerTon);

    private static IReadOnlyList<TradeRoundTripCandidate> MergeBest(
        IReadOnlyList<TradeRoundTripCandidate> current,
        TradeRoundTripCandidate incoming,
        int maxResults) =>
        current
            .Append(incoming)
            .GroupBy(
                candidate => (
                    candidate.Outbound.Source.MarketId,
                    candidate.Outbound.Target.MarketId,
                    Outbound: candidate.Outbound.Source.CommodityName,
                    Return: candidate.ReturnCommodity),
                RoundTripIdentityComparer.Instance)
            .Select(group => group
                .OrderByDescending(candidate => candidate.ProfitPerCycle)
                .First())
            .OrderByDescending(candidate => candidate.ProfitPerCycle)
            .ThenByDescending(candidate => candidate.CombinedProfitPerTon)
            .ThenBy(candidate => candidate.TradeLegDistanceLy)
            .Take(maxResults)
            .ToArray();

    private sealed record PairOutcome(
        TradeRoundTripCandidate? Candidate,
        Exception? Error);

    private sealed class RoundTripIdentityComparer :
        IEqualityComparer<(long SourceMarketId, long TargetMarketId, string Outbound, string Return)>
    {
        public static RoundTripIdentityComparer Instance { get; } = new();

        public bool Equals(
            (long SourceMarketId, long TargetMarketId, string Outbound, string Return) x,
            (long SourceMarketId, long TargetMarketId, string Outbound, string Return) y) =>
            x.SourceMarketId == y.SourceMarketId
            && x.TargetMarketId == y.TargetMarketId
            && x.Outbound.Equals(y.Outbound, StringComparison.OrdinalIgnoreCase)
            && x.Return.Equals(y.Return, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(
            (long SourceMarketId, long TargetMarketId, string Outbound, string Return) obj) =>
            HashCode.Combine(
                obj.SourceMarketId,
                obj.TargetMarketId,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Outbound),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Return));
    }
}
