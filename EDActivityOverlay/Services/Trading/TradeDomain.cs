namespace EDActivityOverlay.Services.Trading;

public sealed record TradeSystemReference(string Name, long SystemAddress = 0);

public sealed record TradeSystemLocation(
    long SystemAddress,
    string SystemName,
    double X,
    double Y,
    double Z);

public sealed record TradeCommoditySummary(
    string CommodityName,
    int MinBuyPrice,
    int MaxSellPrice,
    long TotalStock,
    long TotalDemand)
{
    public int TheoreticalSpread => MaxSellPrice - MinBuyPrice;
}

public sealed record TradeMarketOrder
{
    public string CommodityName { get; init; } = string.Empty;
    public long MarketId { get; init; }
    public string StationName { get; init; } = string.Empty;
    public string StationType { get; init; } = string.Empty;
    public double? DistanceToArrivalLs { get; init; }
    public int MaxLandingPadSize { get; init; }
    public long SystemAddress { get; init; }
    public string SystemName { get; init; } = string.Empty;
    public double SystemX { get; init; }
    public double SystemY { get; init; }
    public double SystemZ { get; init; }

    // Ardent buyPrice: commander buys from station.
    public int BuyFromStationPrice { get; init; }

    // Ardent sellPrice: commander sells to station.
    public int SellToStationPrice { get; init; }

    public long Demand { get; init; }
    public long Stock { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public double? ReferenceDistanceLy { get; init; }

    public bool IsFleetCarrier =>
        StationType.Contains("Carrier", StringComparison.OrdinalIgnoreCase);

    // Ardent importer endpoints explicitly define zero as infinite demand.
    public bool HasInfiniteDemand => Demand == 0;
}

public sealed record TradeSearchConstraints
{
    public string OriginSystemName { get; init; } = string.Empty;
    public long OriginSystemAddress { get; init; }
    public int CargoCapacity { get; init; } = 1;
    public int SourceSearchRadiusLy { get; init; } = 40;
    public int TargetSearchRadiusLy { get; init; } = 80;
    public TimeSpan MaxDataAge { get; init; } = TimeSpan.FromDays(3);
    public int MinLandingPadSize { get; init; } = 1;
    public double? MaxStationDistanceLs { get; init; }
    public bool IncludeFleetCarriers { get; init; }
    public long MinSupply { get; init; } = 1;
    public long MinDemand { get; init; } = 1;
    public int MaxCommodityCandidates { get; init; } = 36;
    public int MaxResults { get; init; } = 100;
    public int MaxConcurrentCommoditySearches { get; init; } = 6;

    public int EnvelopeRadiusLy => checked(SourceSearchRadiusLy + TargetSearchRadiusLy);

    public int ApiMaxDaysAgo => Math.Clamp(
        (int)Math.Ceiling(Math.Max(1, MaxDataAge.TotalHours) / 24d),
        1,
        365);

    public TradeSystemReference Origin => new(OriginSystemName, OriginSystemAddress);

    public void Validate()
    {
        if (OriginSystemAddress == 0 && string.IsNullOrWhiteSpace(OriginSystemName))
        {
            throw new ArgumentException("Origin system name or address is required.");
        }

        if (CargoCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(CargoCapacity));
        if (SourceSearchRadiusLy is < 0 or > 500)
            throw new ArgumentOutOfRangeException(nameof(SourceSearchRadiusLy));
        if (TargetSearchRadiusLy is < 0 or > 500)
            throw new ArgumentOutOfRangeException(nameof(TargetSearchRadiusLy));
        if (EnvelopeRadiusLy > 500)
            throw new ArgumentOutOfRangeException(nameof(TargetSearchRadiusLy), "Source radius + target radius must be <= 500 ly in Trade-v1.");
        if (MaxDataAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxDataAge));
        if (MinLandingPadSize is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(MinLandingPadSize));
        if (MaxStationDistanceLs.HasValue && MaxStationDistanceLs.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxStationDistanceLs));
        if (MinSupply < 1)
            throw new ArgumentOutOfRangeException(nameof(MinSupply));
        if (MinDemand < 1)
            throw new ArgumentOutOfRangeException(nameof(MinDemand));
        if (MaxCommodityCandidates < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxCommodityCandidates));
        if (MaxResults < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxResults));
        if (MaxConcurrentCommoditySearches < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentCommoditySearches));
    }
}

public sealed record TradeRouteCandidate
{
    public required TradeMarketOrder Source { get; init; }
    public required TradeMarketOrder Target { get; init; }
    public int ProfitPerTon { get; init; }
    public int TradableAmount { get; init; }
    public long ProfitPerTrip { get; init; }
    public double OriginToSourceDistanceLy { get; init; }
    public double SourceToTargetDistanceLy { get; init; }
    public double TotalTravelDistanceLy => OriginToSourceDistanceLy + SourceToTargetDistanceLy;
    public TimeSpan SourceAge { get; init; }
    public TimeSpan TargetAge { get; init; }
    public TimeSpan WorstDataAge => SourceAge >= TargetAge ? SourceAge : TargetAge;
    public DateTimeOffset OldestUpdateUtc => Source.UpdatedAt <= Target.UpdatedAt ? Source.UpdatedAt : Target.UpdatedAt;
}

public sealed record TradeSearchResult(
    TradeSystemLocation Origin,
    IReadOnlyList<TradeRouteCandidate> Candidates,
    int CommodityReportsAvailable,
    int CommoditiesEvaluated);
