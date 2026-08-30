using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeV9dConfidenceTests
{
    private static readonly DateTimeOffset Now =
        new(
            2026,
            8,
            30,
            12,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public void FreshDeepLiquidRouteIsHighConfidence()
    {
        TradeRouteCandidate candidate =
            Candidate(
                sourceStock:
                    2_000,
                targetDemand:
                    3_000,
                buy:
                    10_000,
                sell:
                    15_000,
                sourceAgeHours:
                    0.5,
                targetAgeHours:
                    1);

        TradeRouteConfidence confidence =
            TradeRouteConfidenceCalculator.Evaluate(
                candidate,
                desiredCargo:
                    100);

        Assert.Equal(
            TradeConfidenceLevel.High,
            confidence.Level);

        Assert.True(
            confidence.Score >= 85);

        Assert.DoesNotContain(
            confidence.Reasons,
            reason =>
                reason.Severity
                == TradeConfidenceReasonSeverity.Warning);
    }

    [Fact]
    public void StaleFragileThinMarginCarrierRouteIsLowConfidence()
    {
        TradeRouteCandidate candidate =
            Candidate(
                sourceStock:
                    105,
                targetDemand:
                    105,
                buy:
                    100_000,
                sell:
                    102_000,
                sourceAgeHours:
                    48,
                targetAgeHours:
                    60,
                sourceCarrier:
                    true,
                targetCarrier:
                    true);

        TradeRouteConfidence confidence =
            TradeRouteConfidenceCalculator.Evaluate(
                candidate,
                desiredCargo:
                    100);

        Assert.Equal(
            TradeConfidenceLevel.Low,
            confidence.Level);

        Assert.True(
            confidence.Score < 55);

        Assert.Contains(
            confidence.Reasons,
            reason =>
                reason.Signal
                == TradeConfidenceSignal.SourceFleetCarrier);

        Assert.Contains(
            confidence.Reasons,
            reason =>
                reason.Signal
                == TradeConfidenceSignal.RelativeMargin
                && reason.Severity
                   == TradeConfidenceReasonSeverity.Warning);
    }

    [Fact]
    public void InfiniteDemandIsPositiveSignal()
    {
        TradeRouteCandidate finite =
            Candidate(
                sourceStock:
                    500,
                targetDemand:
                    100,
                buy:
                    10_000,
                sell:
                    15_000,
                sourceAgeHours:
                    2,
                targetAgeHours:
                    2);

        TradeRouteCandidate infinite =
            finite with
            {
                Target =
                    finite.Target with
                    {
                        Demand =
                            0
                    }
            };

        TradeRouteConfidence finiteConfidence =
            TradeRouteConfidenceCalculator.Evaluate(
                finite,
                desiredCargo:
                    100);

        TradeRouteConfidence infiniteConfidence =
            TradeRouteConfidenceCalculator.Evaluate(
                infinite,
                desiredCargo:
                    100);

        Assert.True(
            infiniteConfidence.Score
            > finiteConfidence.Score);

        Assert.Contains(
            infiniteConfidence.Reasons,
            reason =>
                reason.Signal
                == TradeConfidenceSignal.InfiniteTargetDemand
                && reason.Severity
                   == TradeConfidenceReasonSeverity.Positive);
    }

    [Fact]
    public void PartialMarketDepthIsExplicitWarning()
    {
        TradeRouteCandidate candidate =
            Candidate(
                sourceStock:
                    100,
                targetDemand:
                    100,
                buy:
                    10_000,
                sell:
                    15_000,
                sourceAgeHours:
                    1,
                targetAgeHours:
                    1);

        TradeRouteConfidence confidence =
            TradeRouteConfidenceCalculator.Evaluate(
                candidate,
                desiredCargo:
                    200);

        TradeConfidenceReason warning =
            Assert.Single(
                confidence.Reasons,
                reason =>
                    reason.Signal
                    == TradeConfidenceSignal.MarketPartialFill);

        Assert.Equal(
            TradeConfidenceReasonSeverity.Warning,
            warning.Severity);

        Assert.Equal(
            50,
            warning.Value,
            3);
    }

    [Fact]
    public void RoundTripUsesWeakerLegConfidence()
    {
        TradeRouteCandidate outbound =
            Candidate(
                sourceStock:
                    2_000,
                targetDemand:
                    2_000,
                buy:
                    10_000,
                sell:
                    15_000,
                sourceAgeHours:
                    1,
                targetAgeHours:
                    1);

        var roundTrip =
            new TradeRoundTripCandidate
            {
                Outbound =
                    outbound,
                ReturnSource =
                    Order(
                        20,
                        "Return Source",
                        buy:
                            100_000,
                        sell:
                            0,
                        stock:
                            105,
                        demand:
                            0,
                        ageHours:
                            48),
                ReturnTarget =
                    Order(
                        10,
                        "Return Target",
                        buy:
                            0,
                        sell:
                            102_000,
                        stock:
                            0,
                        demand:
                            105,
                        ageHours:
                            48),
                ReturnProfitPerTon =
                    2_000,
                ReturnTradableAmount =
                    100,
                ReturnProfitPerTrip =
                    200_000,
                ReturnSourceAge =
                    TimeSpan.FromHours(
                        48),
                ReturnTargetAge =
                    TimeSpan.FromHours(
                        48)
            };

        TradeRouteConfidence confidence =
            TradeRouteConfidenceCalculator.Evaluate(
                roundTrip,
                desiredCargo:
                    100);

        Assert.NotNull(
            confidence.ReturnScore);

        Assert.Equal(
            Math.Min(
                confidence.OutboundScore,
                confidence.ReturnScore!.Value),
            confidence.Score);
    }

    [Fact]
    public void UnifiedWorkspaceExposesConfidenceSortAndDetail()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml.cs");

        Assert.Contains(
            "x:Name=\"ConfidenceSortItem\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"SelectedConfidencePanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Text=\"{Binding Confidence}\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"confidence\" =>",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DiversifiedRetentionIncludesConfidenceRanking()
    {
        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "Services",
                "Trading",
                "TradeCandidateRetention.cs");

        Assert.Contains(
            "TradeRouteConfidenceCalculator.Evaluate",
            code,
            StringComparison.Ordinal);
    }

    private static TradeRouteCandidate Candidate(
        long sourceStock,
        long targetDemand,
        int buy,
        int sell,
        double sourceAgeHours,
        double targetAgeHours,
        bool sourceCarrier = false,
        bool targetCarrier = false) =>
        new()
        {
            Source =
                Order(
                    10,
                    "Source",
                    buy,
                    0,
                    sourceStock,
                    0,
                    sourceAgeHours,
                    sourceCarrier),
            Target =
                Order(
                    20,
                    "Target",
                    0,
                    sell,
                    0,
                    targetDemand,
                    targetAgeHours,
                    targetCarrier),
            ProfitPerTon =
                sell - buy,
            TradableAmount =
                100,
            ProfitPerTrip =
                (long)(sell - buy)
                * 100,
            OriginToSourceDistanceLy =
                10,
            SourceToTargetDistanceLy =
                20,
            SourceAge =
                TimeSpan.FromHours(
                    sourceAgeHours),
            TargetAge =
                TimeSpan.FromHours(
                    targetAgeHours)
        };

    private static TradeMarketOrder Order(
        long marketId,
        string station,
        int buy,
        int sell,
        long stock,
        long demand,
        double ageHours,
        bool carrier = false) =>
        new()
        {
            CommodityName =
                "gold",
            MarketId =
                marketId,
            StationName =
                station,
            StationType =
                carrier
                    ? "Fleet Carrier"
                    : "Coriolis",
            DistanceToArrivalLs =
                500,
            MaxLandingPadSize =
                3,
            SystemAddress =
                marketId,
            SystemName =
                $"{station} System",
            SystemX =
                marketId,
            SystemY =
                0,
            SystemZ =
                0,
            BuyFromStationPrice =
                buy,
            SellToStationPrice =
                sell,
            Stock =
                stock,
            Demand =
                demand,
            UpdatedAt =
                Now
                - TimeSpan.FromHours(
                    ageHours)
        };

    private static string ReadProjectFile(
        params string[] relative)
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
        {
            string candidate =
                directory.FullName;

            foreach (string part
                     in relative)
            {
                candidate =
                    Path.Combine(
                        candidate,
                        part);
            }

            if (File.Exists(
                    candidate))
            {
                return File.ReadAllText(
                    candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
