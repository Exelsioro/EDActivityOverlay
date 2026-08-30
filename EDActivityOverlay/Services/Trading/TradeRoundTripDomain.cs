namespace EDActivityOverlay.Services.Trading;

public enum TradeWorkspaceSearchMode
{
    OneWay = 0,
    RoundTrip = 1
}

public enum TradeRoundTripSearchStage
{
    DiscoveringOutbound = 0,
    EnrichingPairs = 1,
    Completed = 2
}

public sealed record TradeRoundTripCandidate
{
    public required TradeRouteCandidate Outbound { get; init; }
    public required TradeMarketOrder ReturnSource { get; init; }
    public required TradeMarketOrder ReturnTarget { get; init; }

    public int ReturnProfitPerTon { get; init; }
    public int ReturnTradableAmount { get; init; }
    public long ReturnProfitPerTrip { get; init; }
    public TimeSpan ReturnSourceAge { get; init; }
    public TimeSpan ReturnTargetAge { get; init; }

    public string ReturnCommodity =>
        ReturnSource.CommodityName;

    public long ProfitPerCycle =>
        checked(
            Outbound.ProfitPerTrip
            + ReturnProfitPerTrip);

    // One tonne carried each way earns the sum of the two per-ton margins.
    // Actual cycle profit still uses the independent tradable amount per leg.
    public int CombinedProfitPerTon =>
        checked(
            Outbound.ProfitPerTon
            + ReturnProfitPerTon);

    public double TradeLegDistanceLy =>
        Outbound.SourceToTargetDistanceLy;

    public double CycleDistanceLy =>
        TradeLegDistanceLy * 2d;

    public TimeSpan WorstDataAge =>
        Max(
            Outbound.WorstDataAge,
            ReturnSourceAge,
            ReturnTargetAge);

    public DateTimeOffset OldestUpdateUtc
    {
        get
        {
            DateTimeOffset oldest =
                Outbound.OldestUpdateUtc;

            if (ReturnSource.UpdatedAt < oldest)
            {
                oldest =
                    ReturnSource.UpdatedAt;
            }

            if (ReturnTarget.UpdatedAt < oldest)
            {
                oldest =
                    ReturnTarget.UpdatedAt;
            }

            return
                oldest;
        }
    }

    public TradeRouteCandidate ToDisplayCandidate() =>
        Outbound with
        {
            ProfitPerTon =
                CombinedProfitPerTon,
            ProfitPerTrip =
                ProfitPerCycle,
            TradableAmount =
                Math.Min(
                    Outbound.TradableAmount,
                    ReturnTradableAmount),
            SourceAge =
                WorstDataAge,
            TargetAge =
                WorstDataAge
        };

    private static TimeSpan Max(
        params TimeSpan[] values)
    {
        TimeSpan result =
            TimeSpan.Zero;

        foreach (TimeSpan value
                 in values)
        {
            if (value > result)
            {
                result =
                    value;
            }
        }

        return
            result;
    }
}

public sealed record TradeRoundTripSearchProgress
{
    public TradeRoundTripSearchStage Stage { get; init; }

    public int CompletedOutboundCommodities { get; init; }
    public int TotalOutboundCommodities { get; init; }
    public int PotentialOutboundRoutes { get; init; }

    public int CompletedPairs { get; init; }
    public int TotalPairs { get; init; }
    public int FailedPairs { get; init; }

    public IReadOnlyList<TradeRoundTripCandidate> BestCandidates { get; init; } =
        Array.Empty<TradeRoundTripCandidate>();

    public TimeSpan Elapsed { get; init; }
}
