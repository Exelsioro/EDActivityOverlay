namespace EDActivityOverlay.Services.Trading;

public sealed record TradeCommoditySourceCandidate(
    TradeMarketOrder Market,
    string CommodityName,
    int RequestedQuantity,
    int AvailableQuantity,
    int PurchasableQuantity,
    long TotalCost,
    TimeSpan Age)
{
    public bool FullCoverage => PurchasableQuantity >= RequestedQuantity;
    public double DistanceLy => Market.ReferenceDistanceLy ?? 0;
}

public sealed class TradeCommodityLookupService
{
    private readonly ITradeDataProvider provider;
    private IReadOnlyList<string>? commodityNamesCache;

    public TradeCommodityLookupService()
        : this(new ArdentMarketDataProvider())
    {
    }

    public TradeCommodityLookupService(ITradeDataProvider provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<IReadOnlyList<string>> GetCommodityNamesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TradeCommoditySummary> rows =
            await provider.GetCommoditySummariesAsync(cancellationToken)
                .ConfigureAwait(false);

        string[] names = rows
            .Select(row => row.CommodityName?.Trim() ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        commodityNamesCache = names;
        return names;
    }

    public async Task<IReadOnlyList<TradeCommoditySourceCandidate>> SearchAsync(
        TradeSearchConstraints constraints,
        string commodityName,
        int requestedQuantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        constraints.Validate();

        string commodity = commodityName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(commodity))
        {
            throw new ArgumentException("Commodity name is required.", nameof(commodityName));
        }

        if (requestedQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
        }

        IReadOnlyList<string> knownNames = commodityNamesCache
            ?? await GetCommodityNamesAsync(cancellationToken).ConfigureAwait(false);
        string normalizedCommodity = CommodityIdentity.Normalize(commodity);
        commodity = knownNames.FirstOrDefault(name =>
                CommodityIdentity.Normalize(name).Equals(
                    normalizedCommodity,
                    StringComparison.OrdinalIgnoreCase))
            ?? commodity;

        TradeSystemLocation origin =
            await provider.ResolveSystemAsync(
                    constraints.Origin,
                    cancellationToken)
                .ConfigureAwait(false);

        TradeSearchConstraints lookupConstraints = constraints with
        {
            CargoCapacity = requestedQuantity,
            MinSupply = 1,
            MinDemand = 1
        };

        IReadOnlyList<TradeMarketOrder> rows =
            await provider.GetNearbyExportsAsync(
                    origin,
                    commodity,
                    constraints.SourceSearchRadiusLy,
                    lookupConstraints,
                    cancellationToken)
                .ConfigureAwait(false);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        return rows
            .Where(order => IsUsable(order, constraints, now))
            .GroupBy(order => order.MarketId > 0
                ? $"id:{order.MarketId}"
                : $"name:{order.SystemName}|{order.StationName}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(order => order.BuyFromStationPrice)
                .ThenByDescending(order => order.Stock)
                .First())
            .Select(order =>
            {
                int available = checked((int)Math.Min(
                    Math.Max(0L, order.Stock),
                    int.MaxValue));
                int affordable = constraints.AvailableCredits is { } credits
                    ? checked((int)Math.Min(credits / Math.Max(1, order.BuyFromStationPrice), int.MaxValue))
                    : int.MaxValue;
                int quantity = Math.Min(requestedQuantity, Math.Min(available, affordable));
                return new TradeCommoditySourceCandidate(
                    order,
                    commodity,
                    requestedQuantity,
                    available,
                    quantity,
                    checked((long)quantity * order.BuyFromStationPrice),
                    Age(now, order.UpdatedAt));
            })
            .OrderByDescending(candidate => candidate.FullCoverage)
            .ThenBy(candidate => candidate.Market.BuyFromStationPrice)
            .ThenBy(candidate => candidate.DistanceLy)
            .ThenBy(candidate => candidate.Market.DistanceToArrivalLs ?? double.MaxValue)
            .ThenBy(candidate => candidate.Age)
            .Take(constraints.MaxResults)
            .ToArray();
    }

    private static bool IsUsable(
        TradeMarketOrder order,
        TradeSearchConstraints constraints,
        DateTimeOffset now)
    {
        if (order.BuyFromStationPrice <= 0 || order.Stock <= 0)
            return false;
        if (!constraints.IncludeFleetCarriers && order.IsFleetCarrier)
            return false;
        if (order.MaxLandingPadSize < constraints.MinLandingPadSize)
            return false;
        if (constraints.MaxStationDistanceLs is { } maxArrival
            && (order.DistanceToArrivalLs is not { } arrival || arrival > maxArrival))
            return false;
        if (order.UpdatedAt == DateTimeOffset.MinValue)
            return false;
        return Age(now, order.UpdatedAt) <= constraints.MaxDataAge;
    }

    private static TimeSpan Age(DateTimeOffset now, DateTimeOffset updatedAt)
    {
        TimeSpan age = now - updatedAt;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }
}
