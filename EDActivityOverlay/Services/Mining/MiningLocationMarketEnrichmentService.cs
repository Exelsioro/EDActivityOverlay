using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.Services.Mining;

public interface IMiningLocationMarketEnricher
{
    Task<IReadOnlyList<MiningLocationCandidate>> EnrichAsync(
        MiningLocationQuery query,
        IReadOnlyList<MiningLocationCandidate> candidates,
        CancellationToken cancellationToken);
}

public sealed class MiningLocationMarketEnrichmentService : IMiningLocationMarketEnricher
{
    internal const int CandidateLimit = 12;
    internal const int CommodityLimitPerCandidate = 3;
    internal const int SellSearchRadiusLy = 80;

    private static readonly TimeSpan MaxDataAge = TimeSpan.FromDays(3);
    private const int MaxConcurrentCandidates = 4;

    private readonly ITradeDataProvider provider;

    public MiningLocationMarketEnrichmentService()
        : this(new ArdentMarketDataProvider())
    {
    }

    internal MiningLocationMarketEnrichmentService(ITradeDataProvider provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<IReadOnlyList<MiningLocationCandidate>> EnrichAsync(
        MiningLocationQuery query,
        IReadOnlyList<MiningLocationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return candidates;
        }

        int enrichCount = Math.Min(CandidateLimit, candidates.Count);
        MiningLocationCandidate[] head = candidates.Take(enrichCount).ToArray();

        using var gate = new SemaphoreSlim(MaxConcurrentCandidates, MaxConcurrentCandidates);
        Task<MiningLocationCandidate>[] tasks = head
            .Select(async candidate =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await EnrichCandidateAsync(
                        query,
                        candidate,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Logger.Warning(
                        $"Mining destination market enrichment failed for "
                        + $"{candidate.SystemName} / {candidate.RingName}: {ex.Message}");
                    return candidate;
                }
                finally
                {
                    gate.Release();
                }
            })
            .ToArray();

        MiningLocationCandidate[] enrichedHead =
            await Task.WhenAll(tasks).ConfigureAwait(false);

        int highestSellPrice = enrichedHead
            .Where(candidate => candidate.HasDestinationMarket)
            .Select(candidate => candidate.BestSellPrice)
            .DefaultIfEmpty(0)
            .Max();

        for (int index = 0; index < enrichedHead.Length; index++)
        {
            MiningLocationCandidate candidate = enrichedHead[index];
            int priceScore =
                highestSellPrice > 0 && candidate.BestSellPrice > 0
                    ? (int)Math.Round(
                        3d * candidate.BestSellPrice / highestSellPrice,
                        MidpointRounding.AwayFromZero)
                    : 0;

            int sellTravelScore = candidate.HasDestinationMarket
                ? candidate.BestSellDistanceLy switch
                {
                    <= 20 => 2,
                    <= 50 => 1,
                    _ => 0
                }
                : 0;

            int destinationMarketScore = Math.Min(5, priceScore + sellTravelScore);

            enrichedHead[index] = candidate with
            {
                Score = Math.Clamp(
                    candidate.Score - candidate.MarketScore + destinationMarketScore,
                    0,
                    100),
                MarketScore = destinationMarketScore
            };
        }

        return enrichedHead
            .Concat(candidates.Skip(enrichCount))
            .ToArray();
    }

    private async Task<MiningLocationCandidate> EnrichCandidateAsync(
        MiningLocationQuery query,
        MiningLocationCandidate candidate,
        CancellationToken cancellationToken)
    {
        string[] selected = query.CommodityIds
            .Select(id => MiningTargetCatalog.Find(id)?.CommodityId ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] commodities = candidate.HotspotCounts
            .Where(pair => selected.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(CommodityLimitPerCandidate)
            .Select(pair => pair.Key)
            .ToArray();

        if (commodities.Length == 0)
        {
            return candidate;
        }

        TradeSystemLocation origin = await provider.ResolveSystemAsync(
            new TradeSystemReference(candidate.SystemName),
            cancellationToken).ConfigureAwait(false);

        var constraints = new TradeSearchConstraints
        {
            OriginSystemName = origin.SystemName,
            OriginSystemAddress = origin.SystemAddress,
            CargoCapacity = 1,
            SourceSearchRadiusLy = 0,
            TargetSearchRadiusLy = SellSearchRadiusLy,
            MaxDataAge = MaxDataAge,
            MinLandingPadSize = 3,
            IncludeFleetCarriers = false,
            MinSupply = 1,
            MinDemand = 1
        };

        MarketMatch? best = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (string commodityId in commodities)
        {
            MiningTargetOption? option = MiningTargetCatalog.Find(commodityId);
            if (option is null)
            {
                continue;
            }

            IReadOnlyList<TradeMarketOrder> orders =
                await provider.GetNearbyImportsAsync(
                    origin,
                    option.EnglishName,
                    SellSearchRadiusLy,
                    constraints,
                    cancellationToken).ConfigureAwait(false);

            TradeMarketOrder? order = orders
                .Where(item => IsUsableBuyer(item, now))
                .OrderByDescending(item => item.SellToStationPrice)
                .ThenBy(item => BuyerDistanceLy(origin, item))
                .ThenBy(item => item.DistanceToArrivalLs ?? double.MaxValue)
                .ThenByDescending(item => item.UpdatedAt)
                .FirstOrDefault();

            if (order is null)
            {
                continue;
            }

            var match = new MarketMatch(
                option.CommodityId,
                order,
                BuyerDistanceLy(origin, order));

            if (best is null
                || match.Order.SellToStationPrice > best.Order.SellToStationPrice
                || (match.Order.SellToStationPrice == best.Order.SellToStationPrice
                    && match.DistanceLy < best.DistanceLy))
            {
                best = match;
            }
        }

        if (best is null)
        {
            return candidate;
        }

        return candidate with
        {
            BestSellCommodityId = best.CommodityId,
            BestSellPrice = best.Order.SellToStationPrice,
            BestSellSystemName = best.Order.SystemName,
            BestSellStationName = best.Order.StationName,
            BestSellDemand = best.Order.Demand,
            BestSellDistanceLy = best.DistanceLy,
            BestSellDistanceToArrivalLs = best.Order.DistanceToArrivalLs,
            BestSellMaxLandingPadSize = best.Order.MaxLandingPadSize,
            BestSellUpdatedAt = best.Order.UpdatedAt
        };
    }

    private static bool IsUsableBuyer(
        TradeMarketOrder order,
        DateTimeOffset now)
    {
        if (order.SellToStationPrice <= 0
            || order.IsFleetCarrier
            || order.MaxLandingPadSize < 3
            || order.UpdatedAt == DateTimeOffset.MinValue)
        {
            return false;
        }

        if (now - order.UpdatedAt > MaxDataAge)
        {
            return false;
        }

        return order.HasInfiniteDemand || order.Demand >= 1;
    }

    private static double BuyerDistanceLy(
        TradeSystemLocation origin,
        TradeMarketOrder order)
    {
        if (order.ReferenceDistanceLy is { } referenceDistance
            && referenceDistance >= 0)
        {
            return referenceDistance;
        }

        double dx = order.SystemX - origin.X;
        double dy = order.SystemY - origin.Y;
        double dz = order.SystemZ - origin.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private sealed record MarketMatch(
        string CommodityId,
        TradeMarketOrder Order,
        double DistanceLy);
}
