namespace EDActivityOverlay.Services.Trading;

public enum TradeConfidenceLevel
{
    Low = 0,
    Medium = 1,
    High = 2
}

public enum TradeConfidenceReasonSeverity
{
    Positive = 0,
    Neutral = 1,
    Warning = 2
}

public enum TradeConfidenceLeg
{
    Outbound = 0,
    Return = 1
}

public enum TradeConfidenceSignal
{
    SourceFreshness = 0,
    TargetFreshness = 1,
    SourceLiquidity = 2,
    TargetLiquidity = 3,
    InfiniteTargetDemand = 4,
    MarketPartialFill = 5,
    RelativeMargin = 6,
    SourceFleetCarrier = 7,
    TargetFleetCarrier = 8
}

public sealed record TradeConfidenceReason(
    TradeConfidenceSignal Signal,
    TradeConfidenceReasonSeverity Severity,
    TradeConfidenceLeg Leg,
    double Value = 0);

public sealed record TradeRouteConfidence
{
    public int Score { get; init; }

    public TradeConfidenceLevel Level { get; init; }

    public int OutboundScore { get; init; }

    public int? ReturnScore { get; init; }

    public IReadOnlyList<TradeConfidenceReason> Reasons { get; init; } =
        Array.Empty<TradeConfidenceReason>();
}

public static class TradeRouteConfidenceCalculator
{
    public static TradeRouteConfidence Evaluate(
        TradeRouteCandidate candidate,
        int desiredCargo)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);

        LegAssessment outbound =
            EvaluateLeg(
                candidate.Source,
                candidate.Target,
                candidate.ProfitPerTon,
                candidate.TradableAmount,
                candidate.SourceAge,
                candidate.TargetAge,
                desiredCargo,
                TradeConfidenceLeg.Outbound);

        return new TradeRouteConfidence
        {
            Score =
                outbound.Score,
            Level =
                LevelFor(
                    outbound.Score),
            OutboundScore =
                outbound.Score,
            Reasons =
                outbound.Reasons
        };
    }

    public static TradeRouteConfidence Evaluate(
        TradeRoundTripCandidate candidate,
        int desiredCargo)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);

        LegAssessment outbound =
            EvaluateLeg(
                candidate.Outbound.Source,
                candidate.Outbound.Target,
                candidate.Outbound.ProfitPerTon,
                candidate.Outbound.TradableAmount,
                candidate.Outbound.SourceAge,
                candidate.Outbound.TargetAge,
                desiredCargo,
                TradeConfidenceLeg.Outbound);

        LegAssessment returnLeg =
            EvaluateLeg(
                candidate.ReturnSource,
                candidate.ReturnTarget,
                candidate.ReturnProfitPerTon,
                candidate.ReturnTradableAmount,
                candidate.ReturnSourceAge,
                candidate.ReturnTargetAge,
                desiredCargo,
                TradeConfidenceLeg.Return);

        // A repeatable A <-> B cycle is only as reliable as its weaker leg.
        int score =
            Math.Min(
                outbound.Score,
                returnLeg.Score);

        return new TradeRouteConfidence
        {
            Score =
                score,
            Level =
                LevelFor(
                    score),
            OutboundScore =
                outbound.Score,
            ReturnScore =
                returnLeg.Score,
            Reasons =
                outbound.Reasons
                    .Concat(
                        returnLeg.Reasons)
                    .ToArray()
        };
    }

    private static LegAssessment EvaluateLeg(
        TradeMarketOrder source,
        TradeMarketOrder target,
        int profitPerTon,
        int tradableAmount,
        TimeSpan sourceAge,
        TimeSpan targetAge,
        int desiredCargo,
        TradeConfidenceLeg leg)
    {
        int actualAmount =
            Math.Max(
                1,
                tradableAmount);

        int requested =
            Math.Max(
                actualAmount,
                desiredCargo);

        double sourceAgeHours =
            Math.Max(
                0,
                sourceAge.TotalHours);

        double targetAgeHours =
            Math.Max(
                0,
                targetAge.TotalHours);

        double sourceLiquidity =
            Math.Max(
                0,
                source.Stock)
            / (double)actualAmount;

        double targetLiquidity =
            target.HasInfiniteDemand
                ? double.PositiveInfinity
                : Math.Max(
                    0,
                    target.Demand)
                  / (double)actualAmount;

        long finiteDemand =
            target.HasInfiniteDemand
                ? long.MaxValue
                : Math.Max(
                    0,
                    target.Demand);

        long marketDepth =
            Math.Min(
                Math.Max(
                    0,
                    source.Stock),
                finiteDemand);

        double fillRatio =
            requested <= 0
                ? 1
                : Math.Min(
                    1,
                    marketDepth
                    / (double)requested);

        double marginRatio =
            profitPerTon
            / (double)Math.Max(
                1,
                source.BuyFromStationPrice);

        double freshnessScore =
            (AgeScore(
                 sourceAgeHours)
             + AgeScore(
                 targetAgeHours))
            / 2d;

        double sourceLiquidityScore =
            LiquidityScore(
                sourceLiquidity);

        double targetLiquidityScore =
            target.HasInfiniteDemand
                ? 100
                : LiquidityScore(
                    targetLiquidity);

        double marginScore =
            MarginScore(
                marginRatio);

        double fillScore =
            FillScore(
                fillRatio);

        double raw =
            freshnessScore
            * 0.35
            + sourceLiquidityScore
              * 0.20
            + targetLiquidityScore
              * 0.20
            + marginScore
              * 0.15
            + fillScore
              * 0.10;

        if (source.IsFleetCarrier)
        {
            raw -=
                6;
        }

        if (target.IsFleetCarrier)
        {
            raw -=
                6;
        }

        int score =
            Math.Clamp(
                checked(
                    (int)Math.Round(
                        raw,
                        MidpointRounding.AwayFromZero)),
                0,
                100);

        var reasons =
            new List<TradeConfidenceReason>
            {
                new(
                    TradeConfidenceSignal.SourceFreshness,
                    FreshnessSeverity(
                        sourceAgeHours),
                    leg,
                    sourceAgeHours),
                new(
                    TradeConfidenceSignal.TargetFreshness,
                    FreshnessSeverity(
                        targetAgeHours),
                    leg,
                    targetAgeHours),
                new(
                    TradeConfidenceSignal.SourceLiquidity,
                    LiquiditySeverity(
                        sourceLiquidity),
                    leg,
                    sourceLiquidity)
            };

        if (target.HasInfiniteDemand)
        {
            reasons.Add(
                new TradeConfidenceReason(
                    TradeConfidenceSignal.InfiniteTargetDemand,
                    TradeConfidenceReasonSeverity.Positive,
                    leg));
        }
        else
        {
            reasons.Add(
                new TradeConfidenceReason(
                    TradeConfidenceSignal.TargetLiquidity,
                    LiquiditySeverity(
                        targetLiquidity),
                    leg,
                    targetLiquidity));
        }

        if (fillRatio < 0.999)
        {
            reasons.Add(
                new TradeConfidenceReason(
                    TradeConfidenceSignal.MarketPartialFill,
                    TradeConfidenceReasonSeverity.Warning,
                    leg,
                    fillRatio * 100d));
        }

        reasons.Add(
            new TradeConfidenceReason(
                TradeConfidenceSignal.RelativeMargin,
                MarginSeverity(
                    marginRatio),
                leg,
                marginRatio * 100d));

        if (source.IsFleetCarrier)
        {
            reasons.Add(
                new TradeConfidenceReason(
                    TradeConfidenceSignal.SourceFleetCarrier,
                    TradeConfidenceReasonSeverity.Warning,
                    leg));
        }

        if (target.IsFleetCarrier)
        {
            reasons.Add(
                new TradeConfidenceReason(
                    TradeConfidenceSignal.TargetFleetCarrier,
                    TradeConfidenceReasonSeverity.Warning,
                    leg));
        }

        return new LegAssessment(
            score,
            reasons);
    }

    private static TradeConfidenceLevel LevelFor(
        int score) =>
        score switch
        {
            >= 78 =>
                TradeConfidenceLevel.High,
            >= 55 =>
                TradeConfidenceLevel.Medium,
            _ =>
                TradeConfidenceLevel.Low
        };

    private static double AgeScore(
        double ageHours)
    {
        // 24-hour half-life:
        //   0h -> 100, 24h -> 50, 48h -> 25, 72h -> 12.5.
        return Math.Clamp(
            100d
            * Math.Pow(
                0.5d,
                Math.Max(
                    0,
                    ageHours)
                / 24d),
            0,
            100);
    }

    private static double LiquidityScore(
        double ratio) =>
        ratio switch
        {
            >= 10 =>
                100,
            >= 5 =>
                90,
            >= 3 =>
                80,
            >= 2 =>
                70,
            >= 1.5 =>
                55,
            >= 1.2 =>
                45,
            >= 1 =>
                35,
            > 0 =>
                ratio * 35,
            _ =>
                0
        };

    private static double MarginScore(
        double ratio) =>
        ratio switch
        {
            >= 0.50 =>
                100,
            >= 0.30 =>
                90,
            >= 0.20 =>
                80,
            >= 0.10 =>
                65,
            >= 0.05 =>
                45,
            >= 0.02 =>
                25,
            > 0 =>
                10,
            _ =>
                0
        };

    private static double FillScore(
        double ratio) =>
        ratio switch
        {
            >= 1 =>
                100,
            >= 0.75 =>
                70,
            >= 0.50 =>
                45,
            >= 0.25 =>
                25,
            > 0 =>
                10,
            _ =>
                0
        };

    private static TradeConfidenceReasonSeverity FreshnessSeverity(
        double ageHours) =>
        ageHours switch
        {
            <= 4 =>
                TradeConfidenceReasonSeverity.Positive,
            >= 24 =>
                TradeConfidenceReasonSeverity.Warning,
            _ =>
                TradeConfidenceReasonSeverity.Neutral
        };

    private static TradeConfidenceReasonSeverity LiquiditySeverity(
        double ratio) =>
        ratio switch
        {
            >= 3 =>
                TradeConfidenceReasonSeverity.Positive,
            < 1.5 =>
                TradeConfidenceReasonSeverity.Warning,
            _ =>
                TradeConfidenceReasonSeverity.Neutral
        };

    private static TradeConfidenceReasonSeverity MarginSeverity(
        double ratio) =>
        ratio switch
        {
            >= 0.20 =>
                TradeConfidenceReasonSeverity.Positive,
            < 0.05 =>
                TradeConfidenceReasonSeverity.Warning,
            _ =>
                TradeConfidenceReasonSeverity.Neutral
        };

    private sealed record LegAssessment(
        int Score,
        IReadOnlyList<TradeConfidenceReason> Reasons);
}
