namespace EDActivityOverlay.Services.Trading;

public sealed record TradeHistoryLegRecord
{
    public int LegNumber { get; init; }
    public long FromMarketId { get; init; }
    public long ToMarketId { get; init; }
    public string FromSystem { get; init; } = string.Empty;
    public string FromStation { get; init; } = string.Empty;
    public string ToSystem { get; init; } = string.Empty;
    public string ToStation { get; init; } = string.Empty;
    public string CommodityId { get; init; } = string.Empty;
    public string Commodity { get; init; } = string.Empty;
    public int PlannedQuantity { get; init; }
    public int PurchasedQuantity { get; init; }
    public int SoldQuantity { get; init; }
    public int PlannedBuyPrice { get; init; }
    public int PlannedSellPrice { get; init; }
    public int AverageBuyPrice { get; init; }
    public int AverageSellPrice { get; init; }
    public long PurchaseCost { get; init; }
    public long SaleRevenue { get; init; }
    public long ActualProfit { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }

    public TimeSpan Duration =>
        CompletedAtUtc > StartedAtUtc
            ? CompletedAtUtc - StartedAtUtc
            : TimeSpan.Zero;
}

public sealed record TradeHistoryRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string RouteKind { get; init; } = "oneway";
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    public int RerouteCount { get; init; }
    public long InitialPlannedProfit { get; init; }
    public long FinalPlannedProfit { get; init; }
    public long ActualProfit { get; init; }
    public long PurchaseCost { get; init; }
    public long SaleRevenue { get; init; }
    public IReadOnlyList<TradeHistoryLegRecord> Legs { get; init; } =
        Array.Empty<TradeHistoryLegRecord>();

    public TimeSpan Duration =>
        CompletedAtUtc > StartedAtUtc
            ? CompletedAtUtc - StartedAtUtc
            : TimeSpan.Zero;

    public long ActualProfitPerHour =>
        Duration.TotalSeconds <= 0
            ? 0
            : checked(
                (long)Math.Round(
                    ActualProfit
                    * 3600d
                    / Duration.TotalSeconds));

    public double VariancePercent =>
        InitialPlannedProfit == 0
            ? 0
            : (ActualProfit - InitialPlannedProfit)
              * 100d
              / Math.Abs((double)InitialPlannedProfit);

    public string CommoditySummary =>
        string.Join(
            " / ",
            Legs
                .Select(leg => leg.Commodity)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));

    public string RouteSummary
    {
        get
        {
            if (Legs.Count == 0)
            {
                return string.Empty;
            }

            var points =
                new List<string>
                {
                    Legs[0].FromSystem
                };

            points.AddRange(
                Legs.Select(leg => leg.ToSystem));

            return string.Join(
                " → ",
                points.Where(value =>
                    !string.IsNullOrWhiteSpace(value)));
        }
    }
}

public sealed record TradeHistorySummary
{
    public int Trades { get; init; }
    public long Profit { get; init; }
    public TimeSpan Duration { get; init; }
    public long ProfitPerHour { get; init; }
    public long BestTradeProfit { get; init; }
    public long TotalCargoSold { get; init; }
}

public sealed record TradeHistorySnapshot
{
    public required TradeHistorySummary Session { get; init; }
    public required TradeHistorySummary AllTime { get; init; }
    public required IReadOnlyList<TradeHistoryRecord> Recent { get; init; }
}
