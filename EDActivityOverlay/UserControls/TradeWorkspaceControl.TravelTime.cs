using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl
{
    private bool travelProfileInitialized;
    private double lastTravelMaxJumpRange;
    private double lastTravelUnladenMass;

    private void RefreshTravelProfileIfChanged(
        EDActivityOverlay.Models.GameStateSnapshot state)
    {
        bool changed =
            !travelProfileInitialized
            || Math.Abs(
                   lastTravelMaxJumpRange
                   - state.MaxJumpRangeLy)
               > 0.001
            || Math.Abs(
                   lastTravelUnladenMass
                   - state.UnladenMassTonnes)
               > 0.001;

        lastTravelMaxJumpRange =
            state.MaxJumpRangeLy;

        lastTravelUnladenMass =
            state.UnladenMassTonnes;

        travelProfileInitialized =
            true;

        if (!changed
            || currentCandidates.Count == 0)
        {
            return;
        }

        RefreshCurrentPage(
            selectFirstWhenEmpty:
                selectedCandidate is null);

        if (selectedCandidate is not null)
        {
            ShowSelectedCandidate(
                selectedCandidate);
        }
    }

    private TradeRouteTravelEstimate EstimateTravel(
        TradeRouteCandidate candidate)
    {
        if (TryGetRoundTrip(
                candidate,
                out TradeRoundTripCandidate roundTrip))
        {
            return
                travelTimeEstimator.EstimateRoundTrip(
                    roundTrip,
                    currentJournal);
        }

        return
            travelTimeEstimator.EstimateOneWay(
                candidate,
                currentJournal);
    }

    private string FormatEstimatedTravelTime(
        TradeRouteCandidate candidate)
    {
        TradeRouteTravelEstimate estimate =
            EstimateTravel(
                candidate);

        return
            estimate.Confidence
            == TradeTravelEstimateConfidence.Unavailable
                ? "—"
                : FormatTravelTime(
                    estimate.CycleTime);
    }

    private long EstimatedProfitPerHour(
        TradeRouteCandidate candidate)
    {
        TradeRouteTravelEstimate estimate =
            EstimateTravel(
                candidate);

        if (estimate.Confidence
            == TradeTravelEstimateConfidence.Unavailable)
        {
            return
                0;
        }

        return
            estimate.ProfitPerHour(
                candidate.ProfitPerTrip);
    }

    private double EstimatedTravelSeconds(
        TradeRouteCandidate candidate)
    {
        TradeRouteTravelEstimate estimate =
            EstimateTravel(
                candidate);

        return
            estimate.Confidence
            == TradeTravelEstimateConfidence.Unavailable
                ? double.MaxValue
                : estimate.CycleTime.TotalSeconds;
    }

    private string FormatTravelDetail(
        TradeRouteCandidate candidate)
    {
        TradeRouteTravelEstimate estimate =
            travelTimeEstimator.EstimateOneWay(
                candidate,
                currentJournal);

        if (estimate.Confidence
            == TradeTravelEstimateConfidence.Unavailable)
        {
            return
                Loc.Get(
                    "Loc_TRADE_TRAVEL_UNAVAILABLE");
        }

        long creditsPerHour =
            estimate.ProfitPerHour(
                candidate.ProfitPerTrip);

        return
            Loc.Format(
                "Loc_TRADE_TRAVEL_ONEWAY_DETAIL",
                FormatTravelTime(
                    estimate.OneWayTime),
                estimate.Outbound.EstimatedJumps,
                estimate.Outbound.LoadedJumpRangeLy,
                FormatTravelTime(
                    estimate.Outbound.SupercruiseTime),
                candidate.Target.DistanceToArrivalLs
                    ?? 0,
                FormatCreditsPerHour(
                    creditsPerHour),
                ConfidenceLabel(
                    estimate.Confidence));
    }

    private string FormatTravelDetail(
        TradeRoundTripCandidate candidate)
    {
        TradeRouteTravelEstimate estimate =
            travelTimeEstimator.EstimateRoundTrip(
                candidate,
                currentJournal);

        if (estimate.Confidence
            == TradeTravelEstimateConfidence.Unavailable
            || estimate.Return is null)
        {
            return
                Loc.Get(
                    "Loc_TRADE_TRAVEL_UNAVAILABLE");
        }

        long creditsPerHour =
            estimate.ProfitPerHour(
                candidate.ProfitPerCycle);

        return
            Loc.Format(
                "Loc_TRADE_TRAVEL_ROUND_DETAIL",
                FormatTravelTime(
                    estimate.Outbound.TotalTime),
                estimate.Outbound.EstimatedJumps,
                estimate.Outbound.LoadedJumpRangeLy,
                estimate.Outbound.CargoTons,
                FormatTravelTime(
                    estimate.Outbound.SupercruiseTime),
                candidate.Outbound.Target.DistanceToArrivalLs
                    ?? 0,
                FormatTravelTime(
                    estimate.Return.TotalTime),
                estimate.Return.EstimatedJumps,
                estimate.Return.LoadedJumpRangeLy,
                estimate.Return.CargoTons,
                FormatTravelTime(
                    estimate.Return.SupercruiseTime),
                candidate.Outbound.Source.DistanceToArrivalLs
                    ?? 0,
                FormatTravelTime(
                    estimate.CycleTime),
                FormatCreditsPerHour(
                    creditsPerHour),
                ConfidenceLabel(
                    estimate.Confidence));
    }

    private string FormatCompactTravel(
        TradeRouteCandidate candidate)
    {
        TradeRouteTravelEstimate estimate =
            travelTimeEstimator.EstimateOneWay(
                candidate,
                currentJournal);

        if (estimate.Confidence
            == TradeTravelEstimateConfidence.Unavailable)
        {
            return
                Loc.Get(
                    "Loc_TRADE_TRAVEL_COMPACT_UNAVAILABLE");
        }

        return
            Loc.Format(
                "Loc_TRADE_TRAVEL_COMPACT",
                FormatTravelTime(
                    estimate.OneWayTime),
                estimate.Outbound.EstimatedJumps,
                FormatCreditsPerHour(
                    estimate.ProfitPerHour(
                        candidate.ProfitPerTrip)));
    }

    private string FormatCompactTravel(
        TradeRoundTripCandidate candidate)
    {
        TradeRouteTravelEstimate estimate =
            travelTimeEstimator.EstimateRoundTrip(
                candidate,
                currentJournal);

        if (estimate.Confidence
            == TradeTravelEstimateConfidence.Unavailable)
        {
            return
                Loc.Get(
                    "Loc_TRADE_TRAVEL_COMPACT_UNAVAILABLE");
        }

        return
            Loc.Format(
                "Loc_TRADE_TRAVEL_ROUND_COMPACT",
                FormatTravelTime(
                    estimate.CycleTime),
                estimate.TotalEstimatedJumps,
                FormatCreditsPerHour(
                    estimate.ProfitPerHour(
                        candidate.ProfitPerCycle)));
    }

    private static string FormatTravelTime(
        TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return
                $"{(int)value.TotalHours}h {value.Minutes:00}m";
        }

        if (value.TotalMinutes >= 10)
        {
            return
                $"{(int)value.TotalMinutes}m";
        }

        return
            $"{(int)value.TotalMinutes}m {value.Seconds:00}s";
    }

    private static string FormatCreditsPerHour(
        long value)
    {
        if (value <= 0)
        {
            return
                "—";
        }

        if (value >= 1_000_000_000)
        {
            return
                Loc.Format(
                    "Loc_TRADE_CRH_BILLION",
                    value
                    / 1_000_000_000d);
        }

        if (value >= 1_000_000)
        {
            return
                Loc.Format(
                    "Loc_TRADE_CRH_MILLION",
                    value
                    / 1_000_000d);
        }

        if (value >= 1_000)
        {
            return
                Loc.Format(
                    "Loc_TRADE_CRH_THOUSAND",
                    value
                    / 1_000d);
        }

        return
            Loc.Format(
                "Loc_TRADE_CRH_RAW",
                value);
    }

    private static string ConfidenceLabel(
        TradeTravelEstimateConfidence confidence) =>
        confidence switch
        {
            TradeTravelEstimateConfidence.Medium =>
                "~",
            TradeTravelEstimateConfidence.Low =>
                Loc.Get(
                    "Loc_TRADE_TRAVEL_LOW_CONFIDENCE"),
            _ =>
                string.Empty
        };
}
