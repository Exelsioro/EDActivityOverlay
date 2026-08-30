using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Trading;

public enum TradeContinuousSearchStage
{
    ResolvingStart = 0,
    SearchingFirstHop = 1,
    EnrichingLookahead = 2,
    Completed = 3
}

public sealed record TradeContinuousSearchProgress
{
    public TradeContinuousSearchStage Stage { get; init; }
    public int FirstHopCandidates { get; init; }
    public int CompletedSeeds { get; init; }
    public int TotalSeeds { get; init; }
    public int PlansAvailable { get; init; }
    public int FailedSeeds { get; init; }
}

public sealed record TradeContinuousSearchRequest
{
    public required TradeSystemReference StartSystem { get; init; }
    public TradeSystemLocation? KnownStartLocation { get; init; }
    public long StartMarketId { get; init; }
    public required TradeSearchConstraints Constraints { get; init; }
    public required GameStateSnapshot Ship { get; init; }
    public IReadOnlyList<long> RecentMarketIds { get; init; } =
        Array.Empty<long>();
}

public sealed record TradeContinuousPlan
{
    public required TradeRouteCandidate First { get; init; }
    public TradeRouteCandidate? Lookahead { get; init; }

    public required TradeLegTravelEstimate FirstTravel { get; init; }
    public TradeLegTravelEstimate? LookaheadTravel { get; init; }

    public required TradeRouteConfidence FirstConfidence { get; init; }
    public TradeRouteConfidence? LookaheadConfidence { get; init; }

    public int ConfidenceScore { get; init; }
    public TradeConfidenceLevel ConfidenceLevel { get; init; }

    public long TotalProfit { get; init; }
    public TimeSpan TotalTime { get; init; }
    public long ProfitPerHour { get; init; }

    // Planner-only score. Actual economics remain untouched.
    public long RankingProfitPerHour { get; init; }
    public double PlanningFactor { get; init; } = 1d;

    public bool FirstBacktracks { get; init; }
    public bool LookaheadBacktracks { get; init; }

    public TimeSpan EffectiveWorstDataAge { get; init; }
    public long? CreditsAfterFirst { get; init; }

    public bool HasLookahead => Lookahead is not null;
    public int LegCount => HasLookahead ? 2 : 1;
}
