namespace EDActivityOverlay.Services.Trading;

public sealed record CargoSaleLine
{
    public string CommodityId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int CargoAmount { get; init; }
    public int SellAmount { get; init; }
    public int SellPrice { get; init; }
    public long Revenue { get; init; }
    public long Demand { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CargoSaleCandidate
{
    public required TradeMarketOrder Target { get; init; }
    public required IReadOnlyList<CargoSaleLine> Lines { get; init; }
    public int TotalCargoUnits { get; init; }
    public int SellableUnits { get; init; }
    public long TotalRevenue { get; init; }
    public TimeSpan WorstDataAge { get; init; }
    public bool IsCurrentMarket { get; init; }

    public int UnsoldUnits =>
        Math.Max(
            0,
            TotalCargoUnits - SellableUnits);

    public double CoverageRatio =>
        TotalCargoUnits <= 0
            ? 0
            : SellableUnits / (double)TotalCargoUnits;

    public double DistanceLy =>
        Target.ReferenceDistanceLy ?? 0;

    public long AverageValuePerTon =>
        SellableUnits <= 0
            ? 0
            : TotalRevenue / SellableUnits;
}
