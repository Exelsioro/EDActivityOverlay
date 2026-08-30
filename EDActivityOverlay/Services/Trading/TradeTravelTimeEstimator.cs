using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Trading;

public enum TradeTravelEstimateConfidence
{
    Unavailable = 0,
    Low = 1,
    Medium = 2
}

public sealed record TradeLegTravelEstimate
{
    public int CargoTons { get; init; }
    public double LoadedJumpRangeLy { get; init; }
    public int EstimatedJumps { get; init; }

    public TimeSpan JumpTime { get; init; }
    public TimeSpan SupercruiseTime { get; init; }
    public TimeSpan FixedOperationsTime { get; init; }

    public double? StationDistanceLs { get; init; }

    public TimeSpan TotalTime =>
        JumpTime
        + SupercruiseTime
        + FixedOperationsTime;
}

public sealed record TradeRouteTravelEstimate
{
    public required TradeLegTravelEstimate Outbound { get; init; }
    public TradeLegTravelEstimate? Return { get; init; }

    public TradeTravelEstimateConfidence Confidence { get; init; }
    public string ConfidenceReason { get; init; } = string.Empty;

    public TimeSpan OneWayTime =>
        Outbound.TotalTime;

    public TimeSpan CycleTime =>
        Return is null
            ? Outbound.TotalTime
            : Outbound.TotalTime
              + Return.TotalTime;

    public int TotalEstimatedJumps =>
        Outbound.EstimatedJumps
        + (Return?.EstimatedJumps ?? 0);

    public long ProfitPerHour(
        long profit) =>
        CycleTime.TotalSeconds <= 0
            ? 0
            : checked(
                (long)Math.Round(
                    profit
                    * 3600d
                    / CycleTime.TotalSeconds));
}

public sealed class TradeTravelTimeEstimator
{
    // Empirical supercruise curve. Values >= 1000 ls are based on the
    // Fuel Rats travel-time table. Below 1000 ls Elite never reaches the same
    // cruise profile, so a smooth short-distance approximation is used.
    private static readonly (double DistanceLs, double Seconds)[] SupercruiseCurve =
    [
        (1_000, 60),
        (5_000, 140),
        (10_000, 170),
        (25_000, 230),
        (50_000, 300),
        (100_000, 385),
        (150_000, 450),
        (200_000, 510),
        (300_000, 612),
        (400_000, 705),
        (500_000, 790),
        (600_000, 860),
        (700_000, 950),
        (800_000, 1_022),
        (900_000, 1_085),
        (1_000_000, 1_165),
        (1_200_000, 1_300),
        (1_500_000, 1_494),
        (2_000_000, 1_800),
        (2_500_000, 2_090),
        (3_000_000, 2_370)
    ];

    // These are deliberately modest fixed overheads rather than a fake
    // precision model. FSD class/engineering affect jump count through the
    // Journal-provided MaxJumpRange; a hyperspace transition itself is mostly
    // a fixed client/game overhead.
    public const double JumpOverheadSeconds = 45;
    public const double DepartureOperationsSeconds = 35;
    public const double ArrivalDockMarketSeconds = 55;

    public TradeRouteTravelEstimate EstimateOneWay(
        TradeRouteCandidate candidate,
        GameStateSnapshot ship)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ship);

        TradeLegTravelEstimate outbound =
            EstimateLeg(
                candidate.SourceToTargetDistanceLy,
                candidate.TradableAmount,
                candidate.Target.DistanceToArrivalLs,
                ship);

        return
            new TradeRouteTravelEstimate
            {
                Outbound =
                    outbound,
                Confidence =
                    Confidence(
                        ship,
                        candidate.Target.DistanceToArrivalLs,
                        returnStationDistanceLs:
                            null),
                ConfidenceReason =
                    ConfidenceReason(
                        ship,
                        candidate.Target.DistanceToArrivalLs,
                        returnStationDistanceLs:
                            null)
            };
    }

    public TradeRouteTravelEstimate EstimateRoundTrip(
        TradeRoundTripCandidate candidate,
        GameStateSnapshot ship)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ship);

        TradeLegTravelEstimate outbound =
            EstimateLeg(
                candidate.TradeLegDistanceLy,
                candidate.Outbound.TradableAmount,
                candidate.Outbound.Target.DistanceToArrivalLs,
                ship);

        TradeLegTravelEstimate returnLeg =
            EstimateLeg(
                candidate.TradeLegDistanceLy,
                candidate.ReturnTradableAmount,
                candidate.Outbound.Source.DistanceToArrivalLs,
                ship);

        return
            new TradeRouteTravelEstimate
            {
                Outbound =
                    outbound,
                Return =
                    returnLeg,
                Confidence =
                    Confidence(
                        ship,
                        candidate.Outbound.Target.DistanceToArrivalLs,
                        candidate.Outbound.Source.DistanceToArrivalLs),
                ConfidenceReason =
                    ConfidenceReason(
                        ship,
                        candidate.Outbound.Target.DistanceToArrivalLs,
                        candidate.Outbound.Source.DistanceToArrivalLs)
            };
    }

    public TradeLegTravelEstimate EstimateLeg(
        double systemDistanceLy,
        int cargoTons,
        double? stationDistanceLs,
        GameStateSnapshot ship)
    {
        ArgumentNullException.ThrowIfNull(ship);

        double loadedRange =
            EstimateLoadedJumpRangeLy(
                ship,
                cargoTons);

        int jumps =
            EstimateJumpCount(
                systemDistanceLy,
                loadedRange);

        TimeSpan jumpTime =
            TimeSpan.FromSeconds(
                jumps
                * JumpOverheadSeconds);

        double effectiveStationDistance =
            stationDistanceLs
            is > 0
                ? stationDistanceLs.Value
                : 1_000;

        TimeSpan supercruise =
            EstimateSupercruiseTime(
                effectiveStationDistance);

        return
            new TradeLegTravelEstimate
            {
                CargoTons =
                    Math.Max(
                        0,
                        cargoTons),
                LoadedJumpRangeLy =
                    loadedRange,
                EstimatedJumps =
                    jumps,
                JumpTime =
                    jumpTime,
                SupercruiseTime =
                    supercruise,
                FixedOperationsTime =
                    TimeSpan.FromSeconds(
                        DepartureOperationsSeconds
                        + ArrivalDockMarketSeconds),
                StationDistanceLs =
                    stationDistanceLs
            };
    }

    public double EstimateLoadedJumpRangeLy(
        GameStateSnapshot ship,
        int cargoTons)
    {
        ArgumentNullException.ThrowIfNull(ship);

        if (ship.MaxJumpRangeLy <= 0)
        {
            return
                0;
        }

        if (ship.UnladenMassTonnes <= 0)
        {
            // The drive/loadout is still respected because MaxJumpRange comes
            // from Elite's Loadout event. Cargo correction is unavailable
            // until a Loadout carrying UnladenMass has been observed.
            return
                ship.MaxJumpRangeLy;
        }

        double dryMass =
            ship.UnladenMassTonnes;

        double loadedMass =
            dryMass
            + Math.Max(
                0,
                cargoTons);

        // Elite's Loadout.MaxJumpRange is already based on the installed FSD,
        // its engineering and the fitted ship. The Journal defines it for zero
        // cargo and just enough fuel for one jump. With FSD parameters fixed,
        // jump range scales approximately inversely with ship mass, so adding
        // planned cargo can be modelled without maintaining a separate FSD
        // catalogue. Fuel-mass variation is intentionally omitted and is part
        // of the estimate uncertainty.
        return
            ship.MaxJumpRangeLy
            * dryMass
            / loadedMass;
    }

    public static int EstimateJumpCount(
        double systemDistanceLy,
        double loadedJumpRangeLy)
    {
        if (systemDistanceLy <= 0)
        {
            return
                0;
        }

        if (loadedJumpRangeLy <= 0)
        {
            return
                0;
        }

        return
            Math.Max(
                1,
                (int)Math.Ceiling(
                    systemDistanceLy
                    / loadedJumpRangeLy));
    }

    public static TimeSpan EstimateSupercruiseTime(
        double distanceLs)
    {
        if (distanceLs <= 0)
        {
            return
                TimeSpan.FromSeconds(
                    35);
        }

        if (distanceLs < 1_000)
        {
            // Smoothly approaches the measured 60 s point at 1000 ls while
            // retaining a realistic minimum approach/acceleration cost.
            double seconds =
                35
                + 25
                * Math.Sqrt(
                    distanceLs
                    / 1_000d);

            return
                TimeSpan.FromSeconds(
                    seconds);
        }

        for (int index = 1;
             index < SupercruiseCurve.Length;
             index++)
        {
            (double leftDistance,
             double leftSeconds) =
                SupercruiseCurve[index - 1];

            (double rightDistance,
             double rightSeconds) =
                SupercruiseCurve[index];

            if (distanceLs > rightDistance)
            {
                continue;
            }

            double fraction =
                (distanceLs - leftDistance)
                / (rightDistance - leftDistance);

            double seconds =
                leftSeconds
                + (rightSeconds - leftSeconds)
                * fraction;

            return
                TimeSpan.FromSeconds(
                    seconds);
        }

        // At multi-million-ls distances supercruise is close to the 2001c cap.
        // 15 minutes is the empirical acceleration/deceleration overhead used
        // by the same reference table for very long cruises.
        return
            TimeSpan.FromSeconds(
                900
                + distanceLs
                  / 2_001d);
    }

    private static TradeTravelEstimateConfidence Confidence(
        GameStateSnapshot ship,
        double? outboundStationDistanceLs,
        double? returnStationDistanceLs)
    {
        if (ship.MaxJumpRangeLy <= 0)
        {
            return
                TradeTravelEstimateConfidence.Unavailable;
        }

        bool hasMass =
            ship.UnladenMassTonnes > 0;

        bool hasOutboundStation =
            outboundStationDistanceLs is > 0;

        bool hasReturnStation =
            returnStationDistanceLs is null
            || returnStationDistanceLs is > 0;

        return
            hasMass
            && hasOutboundStation
            && hasReturnStation
                ? TradeTravelEstimateConfidence.Medium
                : TradeTravelEstimateConfidence.Low;
    }

    private static string ConfidenceReason(
        GameStateSnapshot ship,
        double? outboundStationDistanceLs,
        double? returnStationDistanceLs)
    {
        if (ship.MaxJumpRangeLy <= 0)
        {
            return
                "MaxJumpRange is unavailable";
        }

        var missing =
            new List<string>();

        if (ship.UnladenMassTonnes <= 0)
        {
            missing.Add(
                "UnladenMass");
        }

        if (outboundStationDistanceLs is not > 0)
        {
            missing.Add(
                "target station distance");
        }

        if (returnStationDistanceLs is not null
            && returnStationDistanceLs is not > 0)
        {
            missing.Add(
                "return station distance");
        }

        return
            missing.Count == 0
                ? "ship-specific jump range + cargo mass + station distance"
                : "fallback: "
                  + string.Join(
                      ", ",
                      missing);
    }
}
