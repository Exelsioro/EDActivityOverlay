using System;
using System.Collections.Generic;

namespace EDActivityOverlay.Services.Trading;

public enum TradeSearchStage
{
    ResolvingOrigin = 0,
    LoadingCommodityReports = 1,
    Searching = 2,
    Completed = 3
}

public sealed record TradeSearchProgress
{
    public TradeSearchStage Stage { get; init; }

    public TradeSystemLocation? Origin { get; init; }

    public int CommodityReportsAvailable { get; init; }

    public int TotalCommodities { get; init; }

    public int CompletedCommodities { get; init; }

    public int FailedCommodities { get; init; }

    public string CompletedCommodity { get; init; } =
        string.Empty;

    public string LastError { get; init; } =
        string.Empty;

    public int NewCandidateCount { get; init; }

    public IReadOnlyList<TradeRouteCandidate> BestCandidates { get; init; } =
        Array.Empty<TradeRouteCandidate>();

    public TimeSpan Elapsed { get; init; }

    public double Fraction =>
        TotalCommodities > 0
            ? Math.Clamp(
                (double)CompletedCommodities
                / TotalCommodities,
                0,
                1)
            : Stage == TradeSearchStage.Completed
                ? 1
                : 0;
}
