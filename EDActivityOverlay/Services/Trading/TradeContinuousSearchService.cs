using System.Collections.Concurrent;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Trading;

public sealed class TradeContinuousSearchService
{
    public const int FirstHopSeedLimit = 15;
    public const int FirstHopCommodityLimit = 16;
    public const int LookaheadCommodityLimit = 8;
    public const int PerCommodityResultLimit = 12;
    public const int LookaheadResultLimit = 24;
    public const int ApiConcurrency = 8;

    private const double NoContinuationFactor = 0.55d;
    private const double ImmediateBacktrackFactor = 0.75d;
    private const double OlderBacktrackFactor = 0.90d;

    private readonly ITradeDataProvider provider;
    private readonly ITradeSystemTradeSidesProvider sideProvider;
    private readonly TradeTravelTimeEstimator travelTimeEstimator = new();

    public TradeContinuousSearchService()
        : this(new ArdentMarketDataProvider())
    {
    }

    public TradeContinuousSearchService(ITradeDataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        this.provider = provider;
        sideProvider = provider as ITradeSystemTradeSidesProvider
            ?? throw new ArgumentException(
                "Continuous trade search requires exact system market-side access.",
                nameof(provider));
    }

    public async Task<IReadOnlyList<TradeContinuousPlan>> SearchAsync(
        TradeContinuousSearchRequest request,
        IProgress<TradeContinuousSearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.StartMarketId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.StartMarketId));

        request.Constraints.Validate();

        progress?.Report(new TradeContinuousSearchProgress
        {
            Stage = TradeContinuousSearchStage.ResolvingStart
        });

        TradeSystemLocation start =
            request.KnownStartLocation
            ?? await provider.ResolveSystemAsync(
                    request.StartSystem,
                    cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<TradeCommoditySummary> summaries;
        try
        {
            summaries =
                await provider.GetCommoditySummariesAsync(cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Commodity summaries only prioritize which exports we inspect.
            // The exact-market export endpoint remains authoritative.
            summaries = Array.Empty<TradeCommoditySummary>();
        }

        IReadOnlyDictionary<string, TradeCommoditySummary> summaryByCommodity =
            summaries
                .Where(item => !string.IsNullOrWhiteSpace(item.CommodityName))
                .GroupBy(
                    item => item.CommodityName,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        var exportsCache =
            new ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>>();

        var importsCache =
            new ConcurrentDictionary<string, Task<IReadOnlyList<TradeMarketOrder>>>(
                StringComparer.OrdinalIgnoreCase);

        using var apiGate =
            new SemaphoreSlim(ApiConcurrency, ApiConcurrency);

        progress?.Report(new TradeContinuousSearchProgress
        {
            Stage = TradeContinuousSearchStage.SearchingFirstHop
        });

        IReadOnlyList<TradeRouteCandidate> firstHop =
            await SearchExactMarketAsync(
                    start,
                    request.StartMarketId,
                    request.Constraints,
                    summaryByCommodity,
                    FirstHopCommodityLimit,
                    Math.Max(
                        FirstHopSeedLimit * 3,
                        request.Constraints.MaxResults),
                    exportsCache,
                    importsCache,
                    apiGate,
                    request.Ship,
                    cancellationToken)
                .ConfigureAwait(false);

        TradeRouteCandidate[] seeds =
            TradeCandidateRetention
                .SelectDiversified(
                    firstHop,
                    Math.Min(
                        FirstHopSeedLimit,
                        Math.Max(1, firstHop.Count)))
                .ToArray();

        if (seeds.Length == 0)
        {
            progress?.Report(new TradeContinuousSearchProgress
            {
                Stage = TradeContinuousSearchStage.Completed
            });

            return Array.Empty<TradeContinuousPlan>();
        }

        progress?.Report(new TradeContinuousSearchProgress
        {
            Stage = TradeContinuousSearchStage.EnrichingLookahead,
            FirstHopCandidates = firstHop.Count,
            TotalSeeds = seeds.Length
        });

        var plans =
            new List<TradeContinuousPlan>(seeds.Length);

        List<Task<EnrichmentResult>> pending =
            seeds
                .Select(first =>
                    EnrichFirstHopSafeAsync(
                        request,
                        first,
                        summaryByCommodity,
                        exportsCache,
                        importsCache,
                        apiGate,
                        cancellationToken))
                .ToList();

        int completed = 0;
        int failed = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<EnrichmentResult> finished =
                await Task.WhenAny(pending)
                    .ConfigureAwait(false);

            pending.Remove(finished);

            EnrichmentResult enrichment =
                await finished.ConfigureAwait(false);

            plans.Add(enrichment.Plan);

            if (enrichment.Failed)
            {
                failed++;
            }

            completed++;

            progress?.Report(new TradeContinuousSearchProgress
            {
                Stage = TradeContinuousSearchStage.EnrichingLookahead,
                FirstHopCandidates = firstHop.Count,
                CompletedSeeds = completed,
                TotalSeeds = seeds.Length,
                PlansAvailable = plans.Count,
                FailedSeeds = failed
            });
        }

        TradeContinuousPlan[] result =
            plans
                .GroupBy(
                    plan => CandidateKey(plan.First),
                    StringComparer.Ordinal)
                .Select(group =>
                    group
                        .OrderByDescending(item => item.RankingProfitPerHour)
                        .ThenByDescending(item => item.ConfidenceScore)
                        .ThenByDescending(item => item.TotalProfit)
                        .First())
                .OrderByDescending(plan => plan.RankingProfitPerHour)
                .ThenByDescending(plan => plan.ConfidenceScore)
                .ThenByDescending(plan => plan.TotalProfit)
                .Take(request.Constraints.MaxResults)
                .ToArray();

        progress?.Report(new TradeContinuousSearchProgress
        {
            Stage = TradeContinuousSearchStage.Completed,
            FirstHopCandidates = firstHop.Count,
            CompletedSeeds = seeds.Length,
            TotalSeeds = seeds.Length,
            PlansAvailable = result.Length,
            FailedSeeds = failed
        });

        return result;
    }

    private async Task<EnrichmentResult> EnrichFirstHopSafeAsync(
        TradeContinuousSearchRequest request,
        TradeRouteCandidate first,
        IReadOnlyDictionary<string, TradeCommoditySummary> summaries,
        ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>> exportsCache,
        ConcurrentDictionary<string, Task<IReadOnlyList<TradeMarketOrder>>> importsCache,
        SemaphoreSlim apiGate,
        CancellationToken cancellationToken)
    {
        try
        {
            return new EnrichmentResult(
                await EnrichFirstHopAsync(
                        request,
                        first,
                        summaries,
                        exportsCache,
                        importsCache,
                        apiGate,
                        cancellationToken)
                    .ConfigureAwait(false),
                Failed: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new EnrichmentResult(
                BuildPlan(
                    request,
                    first,
                    lookahead: null,
                    creditsAfterFirst:
                        CreditsAfterFirst(
                            request.Constraints.AvailableCredits,
                            first)),
                Failed: true);
        }
    }

    private async Task<TradeContinuousPlan> EnrichFirstHopAsync(
        TradeContinuousSearchRequest request,
        TradeRouteCandidate first,
        IReadOnlyDictionary<string, TradeCommoditySummary> summaries,
        ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>> exportsCache,
        ConcurrentDictionary<string, Task<IReadOnlyList<TradeMarketOrder>>> importsCache,
        SemaphoreSlim apiGate,
        CancellationToken cancellationToken)
    {
        long? creditsAfterFirst =
            CreditsAfterFirst(
                request.Constraints.AvailableCredits,
                first);

        TradeSystemLocation secondStart =
            ToLocation(first.Target);

        TradeSearchConstraints secondConstraints =
            request.Constraints with
            {
                OriginSystemName = secondStart.SystemName,
                OriginSystemAddress = secondStart.SystemAddress,
                SourceSearchRadiusLy = 0,
                AvailableCredits = creditsAfterFirst
            };

        IReadOnlyList<TradeRouteCandidate> secondCandidates =
            await SearchExactMarketAsync(
                    secondStart,
                    first.Target.MarketId,
                    secondConstraints,
                    summaries,
                    LookaheadCommodityLimit,
                    LookaheadResultLimit,
                    exportsCache,
                    importsCache,
                    apiGate,
                    liveState: null,
                    cancellationToken:
                        cancellationToken)
                .ConfigureAwait(false);

        if (secondCandidates.Count == 0)
        {
            return BuildPlan(
                request,
                first,
                lookahead: null,
                creditsAfterFirst:
                    creditsAfterFirst);
        }

        return secondCandidates
            .Select(second =>
                BuildPlan(
                    request,
                    first,
                    second,
                    creditsAfterFirst))
            .OrderByDescending(plan => plan.RankingProfitPerHour)
            .ThenByDescending(plan => plan.ConfidenceScore)
            .ThenByDescending(plan => plan.TotalProfit)
            .First();
    }

    private async Task<IReadOnlyList<TradeRouteCandidate>> SearchExactMarketAsync(
        TradeSystemLocation start,
        long marketId,
        TradeSearchConstraints constraints,
        IReadOnlyDictionary<string, TradeCommoditySummary> summaries,
        int commodityLimit,
        int maxResults,
        ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>> exportsCache,
        ConcurrentDictionary<string, Task<IReadOnlyList<TradeMarketOrder>>> importsCache,
        SemaphoreSlim apiGate,
        GameStateSnapshot? liveState,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TradeMarketOrder> systemExports =
            await GetSystemExportsCachedAsync(
                    start,
                    constraints,
                    exportsCache,
                    apiGate,
                    cancellationToken)
                .ConfigureAwait(false);

        IEnumerable<TradeMarketOrder> exact =
            systemExports
                .Where(order => order.MarketId == marketId);

        if (liveState is not null
            && liveState.MarketSnapshotId == marketId
            && liveState.MarketByCommodityId.Count > 0)
        {
            exact =
                exact.Select(order =>
                    ApplyLiveSourceSnapshot(
                        order,
                        liveState));
        }

        TradeMarketOrder[] exactExports =
            exact
                .Where(order =>
                    order.BuyFromStationPrice > 0
                    && order.Stock > 0)
                .GroupBy(
                    order => order.CommodityName,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderBy(order => order.BuyFromStationPrice)
                        .ThenByDescending(order => order.Stock)
                        .First())
                .OrderByDescending(order =>
                    ExportOpportunityScore(
                        order,
                        summaries,
                        constraints.CargoCapacity))
                .ThenByDescending(order => order.Stock)
                .ThenBy(
                    order => order.CommodityName,
                    StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, commodityLimit))
                .ToArray();

        if (exactExports.Length == 0)
            return Array.Empty<TradeRouteCandidate>();

        TradeSearchConstraints exactConstraints =
            constraints with
            {
                OriginSystemName = start.SystemName,
                OriginSystemAddress = start.SystemAddress,
                SourceSearchRadiusLy = 0,
                DiversifyCandidatePool = true
            };

        Task<IReadOnlyList<TradeRouteCandidate>>[] searches =
            exactExports
                .Select(source =>
                    SearchExportSafeAsync(
                        start,
                        source,
                        exactConstraints,
                        importsCache,
                        apiGate,
                        cancellationToken))
                .ToArray();

        IReadOnlyList<TradeRouteCandidate>[] byCommodity =
            await Task.WhenAll(searches)
                .ConfigureAwait(false);

        TradeRouteCandidate[] distinct =
            byCommodity
                .SelectMany(rows => rows)
                .GroupBy(
                    CandidateKey,
                    StringComparer.Ordinal)
                .Select(group =>
                    group
                        .OrderByDescending(item => item.ProfitPerTrip)
                        .First())
                .ToArray();

        if (distinct.Length == 0)
            return Array.Empty<TradeRouteCandidate>();

        return TradeCandidateRetention
            .SelectDiversified(
                distinct,
                Math.Min(
                    Math.Max(1, maxResults),
                    distinct.Length))
            .ToArray();
    }

    private async Task<IReadOnlyList<TradeRouteCandidate>> SearchExportSafeAsync(
        TradeSystemLocation start,
        TradeMarketOrder source,
        TradeSearchConstraints constraints,
        ConcurrentDictionary<string, Task<IReadOnlyList<TradeMarketOrder>>> importsCache,
        SemaphoreSlim apiGate,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SearchExportAsync(
                    start,
                    source,
                    constraints,
                    importsCache,
                    apiGate,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<TradeRouteCandidate>();
        }
    }

    private async Task<IReadOnlyList<TradeRouteCandidate>> SearchExportAsync(
        TradeSystemLocation start,
        TradeMarketOrder source,
        TradeSearchConstraints constraints,
        ConcurrentDictionary<string, Task<IReadOnlyList<TradeMarketOrder>>> importsCache,
        SemaphoreSlim apiGate,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TradeMarketOrder> imports =
            await GetNearbyImportsCachedAsync(
                    start,
                    source.CommodityName,
                    constraints,
                    importsCache,
                    apiGate,
                    cancellationToken)
                .ConfigureAwait(false);

        return TradeRouteEngine.BuildOneWayCandidates(
            start,
            new[] { source },
            imports,
            constraints,
            maxResults: PerCommodityResultLimit);
    }

    private Task<IReadOnlyList<TradeMarketOrder>> GetSystemExportsCachedAsync(
        TradeSystemLocation system,
        TradeSearchConstraints constraints,
        ConcurrentDictionary<long, Task<IReadOnlyList<TradeMarketOrder>>> cache,
        SemaphoreSlim apiGate,
        CancellationToken cancellationToken)
    {
        if (system.SystemAddress == 0)
        {
            return RunBoundedAsync(
                () =>
                    sideProvider.GetSystemExportsAsync(
                        system,
                        constraints,
                        cancellationToken),
                apiGate,
                cancellationToken);
        }

        return cache.GetOrAdd(
            system.SystemAddress,
            _ =>
                RunBoundedAsync(
                    () =>
                        sideProvider.GetSystemExportsAsync(
                            system,
                            constraints,
                            cancellationToken),
                    apiGate,
                    cancellationToken));
    }

    private Task<IReadOnlyList<TradeMarketOrder>> GetNearbyImportsCachedAsync(
        TradeSystemLocation system,
        string commodity,
        TradeSearchConstraints constraints,
        ConcurrentDictionary<string, Task<IReadOnlyList<TradeMarketOrder>>> cache,
        SemaphoreSlim apiGate,
        CancellationToken cancellationToken)
    {
        string key =
            $"{system.SystemAddress}:"
            + $"{commodity.ToLowerInvariant()}:"
            + $"{constraints.TargetSearchRadiusLy}:"
            + $"{constraints.ApiMaxDaysAgo}:"
            + $"{constraints.MinLandingPadSize}:"
            + $"{constraints.MaxStationDistanceLs}:"
            + $"{constraints.IncludeFleetCarriers}";

        return cache.GetOrAdd(
            key,
            _ =>
                RunBoundedAsync(
                    () =>
                        provider.GetNearbyImportsAsync(
                            system,
                            commodity,
                            constraints.TargetSearchRadiusLy,
                            constraints,
                            cancellationToken),
                    apiGate,
                    cancellationToken));
    }

    private static async Task<IReadOnlyList<TradeMarketOrder>> RunBoundedAsync(
        Func<Task<IReadOnlyList<TradeMarketOrder>>> action,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await action()
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private TradeContinuousPlan BuildPlan(
        TradeContinuousSearchRequest request,
        TradeRouteCandidate first,
        TradeRouteCandidate? lookahead,
        long? creditsAfterFirst)
    {
        TradeLegTravelEstimate firstTravel =
            travelTimeEstimator.EstimateLeg(
                first.SourceToTargetDistanceLy,
                first.TradableAmount,
                first.Target.DistanceToArrivalLs,
                request.Ship);

        TradeRouteConfidence firstConfidence =
            TradeRouteConfidenceCalculator.Evaluate(
                first,
                request.Constraints.CargoCapacity);

        TradeLegTravelEstimate? lookaheadTravel = null;
        TradeRouteConfidence? lookaheadConfidence = null;
        TimeSpan effectiveWorstAge = first.WorstDataAge;

        if (lookahead is not null)
        {
            lookaheadTravel =
                travelTimeEstimator.EstimateLeg(
                    lookahead.SourceToTargetDistanceLy,
                    lookahead.TradableAmount,
                    lookahead.Target.DistanceToArrivalLs,
                    request.Ship);

            TradeRouteCandidate atExecutionTime =
                lookahead with
                {
                    SourceAge =
                        lookahead.SourceAge
                        + firstTravel.TotalTime,
                    TargetAge =
                        lookahead.TargetAge
                        + firstTravel.TotalTime
                };

            lookaheadConfidence =
                TradeRouteConfidenceCalculator.Evaluate(
                    atExecutionTime,
                    request.Constraints.CargoCapacity);

            effectiveWorstAge =
                Max(
                    effectiveWorstAge,
                    atExecutionTime.WorstDataAge);
        }

        long totalProfit =
            checked(
                first.ProfitPerTrip
                + (lookahead?.ProfitPerTrip ?? 0));

        TimeSpan totalTime =
            firstTravel.TotalTime
            + (lookaheadTravel?.TotalTime ?? TimeSpan.Zero);

        long profitPerHour =
            totalTime.TotalSeconds <= 0
                ? 0
                : checked(
                    (long)Math.Round(
                        totalProfit
                        * 3600d
                        / totalTime.TotalSeconds));

        double firstBacktrackFactor =
            BacktrackFactor(
                first.Target.MarketId,
                request.RecentMarketIds);

        long[] secondRecent =
            new[] { request.StartMarketId }
                .Concat(request.RecentMarketIds)
                .ToArray();

        double secondBacktrackFactor =
            lookahead is null
                ? 1d
                : BacktrackFactor(
                    lookahead.Target.MarketId,
                    secondRecent);

        double continuationFactor =
            lookahead is null
                ? NoContinuationFactor
                : 1d;

        double planningFactor =
            firstBacktrackFactor
            * secondBacktrackFactor
            * continuationFactor;

        long rankingProfitPerHour =
            checked(
                (long)Math.Round(
                    profitPerHour
                    * planningFactor));

        int confidenceScore =
            lookaheadConfidence is null
                ? firstConfidence.Score
                : Math.Min(
                    firstConfidence.Score,
                    lookaheadConfidence.Score);

        TradeConfidenceLevel confidenceLevel =
            confidenceScore switch
            {
                >= 78 => TradeConfidenceLevel.High,
                >= 55 => TradeConfidenceLevel.Medium,
                _ => TradeConfidenceLevel.Low
            };

        return new TradeContinuousPlan
        {
            First = first,
            Lookahead = lookahead,
            FirstTravel = firstTravel,
            LookaheadTravel = lookaheadTravel,
            FirstConfidence = firstConfidence,
            LookaheadConfidence = lookaheadConfidence,
            ConfidenceScore = confidenceScore,
            ConfidenceLevel = confidenceLevel,
            TotalProfit = totalProfit,
            TotalTime = totalTime,
            ProfitPerHour = profitPerHour,
            RankingProfitPerHour = rankingProfitPerHour,
            PlanningFactor = planningFactor,
            FirstBacktracks = firstBacktrackFactor < 0.999d,
            LookaheadBacktracks = secondBacktrackFactor < 0.999d,
            EffectiveWorstDataAge = effectiveWorstAge,
            CreditsAfterFirst = creditsAfterFirst
        };
    }

    private static TradeMarketOrder ApplyLiveSourceSnapshot(
        TradeMarketOrder order,
        GameStateSnapshot state)
    {
        string commodityId =
            CommodityIdentity.Normalize(
                order.CommodityName);

        if (!state.MarketByCommodityId.TryGetValue(
                commodityId,
                out MarketItemSnapshot? live))
        {
            // Market.json is a complete current-market snapshot. If Ardent
            // says the station exports a commodity but the live market does
            // not contain it, do not keep the stale remote export.
            return order with
            {
                BuyFromStationPrice = 0,
                Stock = 0,
                UpdatedAt =
                    state.MarketUpdatedUtc
                    ?? order.UpdatedAt
            };
        }

        return order with
        {
            BuyFromStationPrice =
                live.BuyPrice > 0
                    ? live.BuyPrice
                    : order.BuyFromStationPrice,
            Stock =
                Math.Max(
                    0,
                    live.Supply),
            UpdatedAt =
                state.MarketUpdatedUtc
                ?? order.UpdatedAt
        };
    }

    private static double ExportOpportunityScore(
        TradeMarketOrder order,
        IReadOnlyDictionary<string, TradeCommoditySummary> summaries,
        int cargoCapacity)
    {
        int theoreticalSpread =
            summaries.TryGetValue(
                order.CommodityName,
                out TradeCommoditySummary? summary)
                ? Math.Max(
                    1,
                    summary.TheoreticalSpread)
                : 1;

        long volume =
            Math.Min(
                Math.Max(1, order.Stock),
                Math.Max(1, cargoCapacity));

        return theoreticalSpread
            * (double)volume;
    }

    private static long? CreditsAfterFirst(
        long? startingCredits,
        TradeRouteCandidate first)
    {
        if (startingCredits is not { } credits)
            return null;

        long buyCost =
            checked(
                (long)first.Source.BuyFromStationPrice
                * first.TradableAmount);

        long saleRevenue =
            checked(
                (long)first.Target.SellToStationPrice
                * first.TradableAmount);

        return Math.Max(
            0,
            checked(
                credits
                - buyCost
                + saleRevenue));
    }

    private static double BacktrackFactor(
        long targetMarketId,
        IReadOnlyList<long> recentMarketIds)
    {
        for (int index = 0;
             index < recentMarketIds.Count;
             index++)
        {
            if (recentMarketIds[index] != targetMarketId)
                continue;

            return index == 0
                ? ImmediateBacktrackFactor
                : OlderBacktrackFactor;
        }

        return 1d;
    }

    private static TradeSystemLocation ToLocation(
        TradeMarketOrder order) =>
        new(
            order.SystemAddress,
            order.SystemName,
            order.SystemX,
            order.SystemY,
            order.SystemZ);

    private static string CandidateKey(
        TradeRouteCandidate candidate) =>
        $"{candidate.Source.MarketId}:"
        + $"{candidate.Target.MarketId}:"
        + candidate.Source.CommodityName.ToLowerInvariant();

    private static TimeSpan Max(
        TimeSpan left,
        TimeSpan right) =>
        left >= right
            ? left
            : right;

    private sealed record EnrichmentResult(
        TradeContinuousPlan Plan,
        bool Failed);
}
