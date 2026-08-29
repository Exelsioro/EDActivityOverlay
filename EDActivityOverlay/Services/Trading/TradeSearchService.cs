using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EDActivityOverlay.Services.Trading;

public sealed class TradeSearchService
{
    private readonly ITradeDataProvider provider;

    public TradeSearchService()
        : this(
            new ArdentMarketDataProvider())
    {
    }

    public TradeSearchService(
        ITradeDataProvider provider)
    {
        this.provider =
            provider
            ?? throw new ArgumentNullException(
                nameof(provider));
    }

    public async Task<TradeSearchResult> SearchAsync(
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        TradeSearchProgress? completed =
            null;

        await foreach (TradeSearchProgress progress
                       in SearchProgressAsync(
                           constraints,
                           cancellationToken))
        {
            if (progress.Stage
                == TradeSearchStage.Completed)
            {
                completed =
                    progress;
            }
        }

        if (completed?.Origin is null)
        {
            throw new InvalidOperationException(
                "Trade search completed without resolving the origin system.");
        }

        return
            new TradeSearchResult(
                completed.Origin,
                completed.BestCandidates,
                completed.CommodityReportsAvailable,
                completed.TotalCommodities);
    }

    public async IAsyncEnumerable<TradeSearchProgress> SearchProgressAsync(
        TradeSearchConstraints constraints,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            constraints);

        constraints.Validate();

        var stopwatch =
            Stopwatch.StartNew();

        yield return
            new TradeSearchProgress
            {
                Stage =
                    TradeSearchStage.ResolvingOrigin,
                Elapsed =
                    stopwatch.Elapsed
            };

        TradeSystemLocation origin =
            await provider.ResolveSystemAsync(
                    constraints.Origin,
                    cancellationToken)
                .ConfigureAwait(
                    false);

        yield return
            new TradeSearchProgress
            {
                Stage =
                    TradeSearchStage.LoadingCommodityReports,
                Origin =
                    origin,
                Elapsed =
                    stopwatch.Elapsed
            };

        Task<IReadOnlyList<TradeCommoditySummary>> reportsTask =
            provider.GetCommoditySummariesAsync(
                cancellationToken);

        Task<IReadOnlyDictionary<string, IReadOnlyList<TradeMarketOrder>>?>
            originMarketTask =
                TryPreloadOriginMarketAsync(
                    origin,
                    constraints,
                    cancellationToken);

        await Task.WhenAll(
                reportsTask,
                originMarketTask)
            .ConfigureAwait(
                false);

        IReadOnlyList<TradeCommoditySummary> reports =
            await reportsTask.ConfigureAwait(
                false);

        IReadOnlyDictionary<string, IReadOnlyList<TradeMarketOrder>>?
            originOrdersByCommodity =
                await originMarketTask.ConfigureAwait(
                    false);

        TradeCommoditySummary[] shortlisted =
            reports
                .Where(
                    report =>
                        !string.IsNullOrWhiteSpace(
                            report.CommodityName)
                        && report.TotalStock > 0
                        && report.TheoreticalSpread > 0)
                .OrderByDescending(
                    report =>
                        report.TheoreticalSpread)
                .ThenByDescending(
                    report =>
                        report.TotalStock)
                .ThenBy(
                    report =>
                        report.CommodityName,
                    StringComparer.OrdinalIgnoreCase)
                .Take(
                    constraints.MaxCommodityCandidates)
                .ToArray();

        if (shortlisted.Length == 0)
        {
            yield return
                new TradeSearchProgress
                {
                    Stage =
                        TradeSearchStage.Completed,
                    Origin =
                        origin,
                    CommodityReportsAvailable =
                        reports.Count,
                    TotalCommodities =
                        0,
                    CompletedCommodities =
                        0,
                    BestCandidates =
                        Array.Empty<TradeRouteCandidate>(),
                    Elapsed =
                        stopwatch.Elapsed
                };

            yield break;
        }

        yield return
            new TradeSearchProgress
            {
                Stage =
                    TradeSearchStage.Searching,
                Origin =
                    origin,
                CommodityReportsAvailable =
                    reports.Count,
                TotalCommodities =
                    shortlisted.Length,
                CompletedCommodities =
                    0,
                BestCandidates =
                    Array.Empty<TradeRouteCandidate>(),
                Elapsed =
                    stopwatch.Elapsed
            };

        using var gate =
            new SemaphoreSlim(
                constraints.MaxConcurrentCommoditySearches,
                constraints.MaxConcurrentCommoditySearches);

        var pending =
            shortlisted
                .Select(
                    report =>
                        SearchCommodityOutcomeBoundedAsync(
                            origin,
                            report.CommodityName,
                            constraints,
                            originOrdersByCommodity,
                            gate,
                            cancellationToken))
                .ToList();

        IReadOnlyList<TradeRouteCandidate> best =
            Array.Empty<TradeRouteCandidate>();

        int completedCount =
            0;

        int failedCount =
            0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<CommoditySearchOutcome> finished =
                await Task.WhenAny(
                        pending)
                    .ConfigureAwait(
                        false);

            pending.Remove(
                finished);

            CommoditySearchOutcome outcome =
                await finished.ConfigureAwait(
                    false);

            completedCount++;

            if (outcome.Error is not null)
            {
                failedCount++;
            }
            else if (outcome.Candidates.Count > 0)
            {
                best =
                    MergeTopCandidates(
                        best,
                        outcome.Candidates,
                        constraints.MaxResults);
            }

            yield return
                new TradeSearchProgress
                {
                    Stage =
                        TradeSearchStage.Searching,
                    Origin =
                        origin,
                    CommodityReportsAvailable =
                        reports.Count,
                    TotalCommodities =
                        shortlisted.Length,
                    CompletedCommodities =
                        completedCount,
                    FailedCommodities =
                        failedCount,
                    CompletedCommodity =
                        outcome.CommodityName,
                    LastError =
                        outcome.Error?.Message
                        ?? string.Empty,
                    NewCandidateCount =
                        outcome.Candidates.Count,
                    BestCandidates =
                        best,
                    Elapsed =
                        stopwatch.Elapsed
                };
        }

        yield return
            new TradeSearchProgress
            {
                Stage =
                    TradeSearchStage.Completed,
                Origin =
                    origin,
                CommodityReportsAvailable =
                    reports.Count,
                TotalCommodities =
                    shortlisted.Length,
                CompletedCommodities =
                    completedCount,
                FailedCommodities =
                    failedCount,
                BestCandidates =
                    best,
                Elapsed =
                    stopwatch.Elapsed
            };
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<TradeMarketOrder>>?>
        TryPreloadOriginMarketAsync(
            TradeSystemLocation origin,
            TradeSearchConstraints constraints,
            CancellationToken cancellationToken)
    {
        if (provider
            is not ITradeOriginMarketProvider bulkProvider)
        {
            return
                null;
        }

        try
        {
            IReadOnlyList<TradeMarketOrder> orders =
                await bulkProvider.GetSystemOrdersAsync(
                        origin,
                        constraints,
                        cancellationToken)
                    .ConfigureAwait(
                        false);

            return
                orders
                    .Where(
                        order =>
                            !string.IsNullOrWhiteSpace(
                                order.CommodityName))
                    .GroupBy(
                        order =>
                            order.CommodityName,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group =>
                            group.Key,
                        group =>
                            (IReadOnlyList<TradeMarketOrder>)group
                                .GroupBy(
                                    order =>
                                        order.MarketId)
                                .Select(
                                    market =>
                                        market.First())
                                .ToArray(),
                        StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"Ardent origin-market preload failed; falling back to per-commodity origin queries: {ex.Message}");

            return
                null;
        }
    }

    private async Task<CommoditySearchOutcome> SearchCommodityOutcomeBoundedAsync(
        TradeSystemLocation origin,
        string commodityName,
        TradeSearchConstraints constraints,
        IReadOnlyDictionary<string, IReadOnlyList<TradeMarketOrder>>?
            originOrdersByCommodity,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(
                cancellationToken)
            .ConfigureAwait(
                false);

        try
        {
            try
            {
                IReadOnlyList<TradeRouteCandidate> candidates =
                    await SearchCommodityAsync(
                            origin,
                            commodityName,
                            constraints,
                            originOrdersByCommodity,
                            cancellationToken)
                        .ConfigureAwait(
                            false);

                return
                    new CommoditySearchOutcome(
                        commodityName,
                        candidates,
                        null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return
                    new CommoditySearchOutcome(
                        commodityName,
                        Array.Empty<TradeRouteCandidate>(),
                        ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<TradeRouteCandidate>> SearchCommodityAsync(
        TradeSystemLocation origin,
        string commodityName,
        TradeSearchConstraints constraints,
        IReadOnlyDictionary<string, IReadOnlyList<TradeMarketOrder>>?
            originOrdersByCommodity,
        CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<TradeMarketOrder>> localTask;

        if (originOrdersByCommodity is not null)
        {
            localTask =
                Task.FromResult(
                    originOrdersByCommodity.TryGetValue(
                        commodityName,
                        out IReadOnlyList<TradeMarketOrder>? preloadedLocal)
                        ? preloadedLocal
                        : (IReadOnlyList<TradeMarketOrder>)
                          Array.Empty<TradeMarketOrder>());
        }
        else
        {
            localTask =
                provider.GetSystemCommodityOrdersAsync(
                    origin,
                    commodityName,
                    constraints,
                    cancellationToken);
        }

        Task<IReadOnlyList<TradeMarketOrder>> exportsTask =
            provider.GetNearbyExportsAsync(
                origin,
                commodityName,
                constraints.SourceSearchRadiusLy,
                constraints,
                cancellationToken);

        Task<IReadOnlyList<TradeMarketOrder>> importsTask =
            provider.GetNearbyImportsAsync(
                origin,
                commodityName,
                constraints.EnvelopeRadiusLy,
                constraints,
                cancellationToken);

        await Task.WhenAll(
                localTask,
                exportsTask,
                importsTask)
            .ConfigureAwait(
                false);

        IReadOnlyList<TradeMarketOrder> local =
            await localTask.ConfigureAwait(
                false);

        IEnumerable<TradeMarketOrder> sources =
            local
                .Concat(
                    await exportsTask.ConfigureAwait(
                        false))
                .GroupBy(
                    order =>
                        order.MarketId)
                .Select(
                    group =>
                        group.First());

        IEnumerable<TradeMarketOrder> targets =
            local
                .Concat(
                    await importsTask.ConfigureAwait(
                        false))
                .GroupBy(
                    order =>
                        order.MarketId)
                .Select(
                    group =>
                        group.First());

        return
            TradeRouteEngine.BuildOneWayCandidates(
                origin,
                sources,
                targets,
                constraints,
                maxResults:
                    constraints.MaxResults);
    }

    private static IReadOnlyList<TradeRouteCandidate> MergeTopCandidates(
        IReadOnlyList<TradeRouteCandidate> current,
        IReadOnlyList<TradeRouteCandidate> incoming,
        int maxResults) =>
        current
            .Concat(
                incoming)
            .GroupBy(
                candidate =>
                    (
                        candidate.Source.MarketId,
                        candidate.Target.MarketId,
                        Commodity:
                            candidate.Source.CommodityName),
                CandidateIdentityComparer.Instance)
            .Select(
                group =>
                    group
                        .OrderByDescending(
                            candidate =>
                                candidate.ProfitPerTrip)
                        .First())
            .OrderByDescending(
                candidate =>
                    candidate.ProfitPerTrip)
            .ThenByDescending(
                candidate =>
                    candidate.ProfitPerTon)
            .ThenBy(
                candidate =>
                    candidate.TotalTravelDistanceLy)
            .Take(
                maxResults)
            .ToArray();

    private sealed record CommoditySearchOutcome(
        string CommodityName,
        IReadOnlyList<TradeRouteCandidate> Candidates,
        Exception? Error);

    private sealed class CandidateIdentityComparer :
        IEqualityComparer<(long SourceMarketId, long TargetMarketId, string Commodity)>
    {
        public static CandidateIdentityComparer Instance { get; } =
            new();

        public bool Equals(
            (long SourceMarketId, long TargetMarketId, string Commodity) x,
            (long SourceMarketId, long TargetMarketId, string Commodity) y) =>
            x.SourceMarketId == y.SourceMarketId
            && x.TargetMarketId == y.TargetMarketId
            && x.Commodity.Equals(
                y.Commodity,
                StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(
            (long SourceMarketId, long TargetMarketId, string Commodity) obj) =>
            HashCode.Combine(
                obj.SourceMarketId,
                obj.TargetMarketId,
                StringComparer.OrdinalIgnoreCase.GetHashCode(
                    obj.Commodity));
    }
}
