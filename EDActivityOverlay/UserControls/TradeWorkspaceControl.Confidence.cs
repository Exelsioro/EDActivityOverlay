using System.Windows;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl
{
    private TradeRouteConfidence ConfidenceFor(
        TradeRouteCandidate candidate)
    {
        int desiredCargo =
            lastSearchConstraints
            is { CargoCapacity: > 0 } constraints
                ? constraints.CargoCapacity
                : Math.Max(
                    1,
                    candidate.TradableAmount);

        if (TryGetRoundTrip(
                candidate,
                out TradeRoundTripCandidate roundTrip))
        {
            return TradeRouteConfidenceCalculator.Evaluate(
                roundTrip,
                desiredCargo);
        }

        return TradeRouteConfidenceCalculator.Evaluate(
            candidate,
            desiredCargo);
    }

    private int ConfidenceScore(
        TradeRouteCandidate candidate) =>
        ConfidenceFor(
            candidate)
        .Score;

    private string ConfidenceBadge(
        TradeRouteConfidence confidence) =>
        Loc.Format(
            "Loc_TRADE_CONFIDENCE_BADGE",
            ConfidenceLevelName(
                confidence.Level),
            confidence.Score);

    private static string ConfidenceLevelName(
        TradeConfidenceLevel level) =>
        level switch
        {
            TradeConfidenceLevel.High =>
                Loc.Get(
                    "Loc_TRADE_CONFIDENCE_HIGH"),
            TradeConfidenceLevel.Medium =>
                Loc.Get(
                    "Loc_TRADE_CONFIDENCE_MEDIUM"),
            _ =>
                Loc.Get(
                    "Loc_TRADE_CONFIDENCE_LOW")
        };

    private void ShowConfidence(
        TradeRouteCandidate candidate)
    {
        int desiredCargo =
            lastSearchConstraints
            is { CargoCapacity: > 0 } constraints
                ? constraints.CargoCapacity
                : Math.Max(
                    1,
                    candidate.TradableAmount);

        TradeRouteConfidence confidence =
            TradeRouteConfidenceCalculator.Evaluate(
                candidate,
                desiredCargo);

        ShowConfidence(
            confidence);
    }

    private void ShowConfidence(
        TradeRoundTripCandidate candidate)
    {
        int desiredCargo =
            lastSearchConstraints
            is { CargoCapacity: > 0 } constraints
                ? constraints.CargoCapacity
                : Math.Max(
                    1,
                    Math.Max(
                        candidate.Outbound.TradableAmount,
                        candidate.ReturnTradableAmount));

        TradeRouteConfidence confidence =
            TradeRouteConfidenceCalculator.Evaluate(
                candidate,
                desiredCargo);

        ShowConfidence(
            confidence);
    }

    private void ShowConfidence(
        TradeRouteConfidence confidence)
    {
        SelectedConfidencePanel.Visibility =
            Visibility.Visible;

        SelectedConfidenceText.Text =
            FormatConfidenceDetail(
                confidence);
    }

    private void ClearConfidence()
    {
        SelectedConfidencePanel.Visibility =
            Visibility.Collapsed;

        SelectedConfidenceText.Text =
            string.Empty;
    }

    private static string FormatConfidenceDetail(
        TradeRouteConfidence confidence)
    {
        var lines =
            new List<string>
            {
                Loc.Format(
                    "Loc_TRADE_CONFIDENCE_SCORE",
                    ConfidenceLevelName(
                        confidence.Level),
                    confidence.Score)
            };

        int? returnScore =
            confidence.ReturnScore;

        bool roundTrip =
            returnScore.HasValue;

        if (returnScore is { } score)
        {
            lines.Add(
                Loc.Format(
                    "Loc_TRADE_CONFIDENCE_ROUND_SCORES",
                    confidence.OutboundScore,
                    score));
        }

        int reasonLimit =
            roundTrip
                ? 10
                : 8;

        IEnumerable<TradeConfidenceReason> reasons =
            confidence.Reasons
                .OrderBy(
                    ReasonPriority)
                .ThenBy(reason =>
                    reason.Leg)
                .ThenBy(reason =>
                    reason.Signal)
                .Take(
                    reasonLimit);

        foreach (TradeConfidenceReason reason
                 in reasons)
        {
            string symbol =
                reason.Severity switch
                {
                    TradeConfidenceReasonSeverity.Warning =>
                        "⚠",
                    TradeConfidenceReasonSeverity.Positive =>
                        "✓",
                    _ =>
                        "•"
                };

            string leg =
                roundTrip
                    ? reason.Leg
                      == TradeConfidenceLeg.Return
                        ? Loc.Get(
                            "Loc_TRADE_CONFIDENCE_RETURN")
                        : Loc.Get(
                            "Loc_TRADE_CONFIDENCE_OUTBOUND")
                    : string.Empty;

            string detail =
                FormatConfidenceReason(
                    reason);

            lines.Add(
                roundTrip
                    ? $"{symbol} {leg} · {detail}"
                    : $"{symbol} {detail}");
        }

        return string.Join(
            Environment.NewLine,
            lines);
    }

    private static int ReasonPriority(
        TradeConfidenceReason reason) =>
        reason.Severity switch
        {
            TradeConfidenceReasonSeverity.Warning =>
                0,
            TradeConfidenceReasonSeverity.Positive =>
                1,
            _ =>
                2
        };

    private static string FormatConfidenceReason(
        TradeConfidenceReason reason) =>
        reason.Signal switch
        {
            TradeConfidenceSignal.SourceFreshness =>
                Loc.Format(
                    "Loc_TRADE_CONFIDENCE_SOURCE_AGE",
                    reason.Value),
            TradeConfidenceSignal.TargetFreshness =>
                Loc.Format(
                    "Loc_TRADE_CONFIDENCE_TARGET_AGE",
                    reason.Value),
            TradeConfidenceSignal.SourceLiquidity =>
                Loc.Format(
                    "Loc_TRADE_CONFIDENCE_SOURCE_LIQUIDITY",
                    reason.Value),
            TradeConfidenceSignal.TargetLiquidity =>
                Loc.Format(
                    "Loc_TRADE_CONFIDENCE_TARGET_LIQUIDITY",
                    reason.Value),
            TradeConfidenceSignal.InfiniteTargetDemand =>
                Loc.Get(
                    "Loc_TRADE_CONFIDENCE_INFINITE_DEMAND"),
            TradeConfidenceSignal.MarketPartialFill =>
                Loc.Format(
                    "Loc_TRADE_CONFIDENCE_PARTIAL_FILL",
                    reason.Value),
            TradeConfidenceSignal.RelativeMargin =>
                Loc.Format(
                    "Loc_TRADE_CONFIDENCE_MARGIN",
                    reason.Value),
            TradeConfidenceSignal.SourceFleetCarrier =>
                Loc.Get(
                    "Loc_TRADE_CONFIDENCE_SOURCE_CARRIER"),
            TradeConfidenceSignal.TargetFleetCarrier =>
                Loc.Get(
                    "Loc_TRADE_CONFIDENCE_TARGET_CARRIER"),
            _ =>
                string.Empty
        };

    private void UpdateConfidenceSortAvailability()
    {
        bool cargoMode =
            IsCargoSaleMode;

        ConfidenceSortItem.Visibility =
            cargoMode
                ? Visibility.Collapsed
                : Visibility.Visible;

        if (!cargoMode
            || !string.Equals(
                SortTag(),
                "confidence",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool previousApplying =
            applyingJournal;

        applyingJournal =
            true;

        try
        {
            SelectTag(
                SortComboBox,
                "profit");
        }
        finally
        {
            applyingJournal =
                previousApplying;
        }
    }
}
