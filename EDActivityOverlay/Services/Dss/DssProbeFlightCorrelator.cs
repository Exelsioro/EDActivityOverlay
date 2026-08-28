using System;
using System.Collections.Generic;
using System.Linq;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssProbeImpactCorrelation(
    int ImpactSequence,
    DateTimeOffset ImpactUtc,
    long ImpactFrameSequence,
    double CounterChangeRatio,
    int MatchedLaunchSequence,
    DateTimeOffset LaunchUtc,
    double FlightMilliseconds,
    string CorrelationMethod,
    int CandidateCount,
    bool LaunchGeometryValid,
    double AimNormalizedX,
    double AimNormalizedY,
    double AimNormalizedRadius,
    double AimAngleDegrees,
    double AngularDiameterDegrees,
    int NearestPatternPoint,
    double NearestErrorPixels);

internal sealed record DssProbeUnresolvedLaunch(
    int LaunchSequence,
    DateTimeOffset LaunchUtc,
    DateTimeOffset ExpiredUtc,
    double AgeMilliseconds,
    string Reason,
    bool GeometryValid,
    double AimNormalizedRadius);

/// <summary>
/// Correlates visual DSS impact-counter transitions with prior fire inputs.
///
/// It intentionally keeps ambiguity visible. If only one pending launch can
/// explain an impact, the correlation is HIGH confidence. If several launches
/// are in flight, the oldest pending launch is used provisionally and the row
/// is marked FIFO_AMBIGUOUS.
///
/// Controlled calibration should therefore still use one probe at a time when
/// a precise ballistic data point is required.
/// </summary>
internal sealed class DssProbeFlightCorrelator
{
    private static readonly TimeSpan MinimumFlightTime =
        TimeSpan.FromMilliseconds(350);

    private static readonly TimeSpan MaximumFlightTime =
        TimeSpan.FromSeconds(45);

    private readonly object sync =
        new();

    private readonly List<DssProbeLaunchRecord>
        pending =
            new();

    private int impactSequence;

    public void Reset()
    {
        lock (sync)
        {
            pending.Clear();
            impactSequence = 0;
        }
    }

    public bool HasPendingLaunches
    {
        get
        {
            lock (sync)
            {
                return pending.Count > 0;
            }
        }
    }

    public bool RegisterLaunch(
        DssProbeLaunchRecord launch)
    {
        // Elite already exposes a native trajectory verdict at the reticle.
        // If it says MISS/Промах at launch, do not keep that fire input in the
        // future-hit candidate queue.
        if (launch.HudMissVisible)
        {
            return false;
        }

        lock (sync)
        {
            pending.Add(
                launch);

            return true;
        }
    }

    public DssProbeImpactCorrelation
        RegisterImpact(
            DateTimeOffset impactUtc,
            long frameSequence,
            double changeRatio)
    {
        lock (sync)
        {
            DssProbeLaunchRecord[] candidates =
                pending
                    .Where(
                        launch =>
                        {
                            TimeSpan age =
                                impactUtc
                                - launch.InputUtc;

                            return age
                                   >= MinimumFlightTime
                                   && age
                                      <= MaximumFlightTime;
                        })
                    .OrderBy(
                        launch =>
                            launch.InputUtc)
                    .ToArray();

            int nextImpact =
                ++impactSequence;

            if (candidates.Length == 0)
            {
                return new DssProbeImpactCorrelation(
                    nextImpact,
                    impactUtc,
                    frameSequence,
                    changeRatio,
                    0,
                    DateTimeOffset.MinValue,
                    -1,
                    "UNMATCHED",
                    0,
                    false,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0);
            }

            DssProbeLaunchRecord matched =
                candidates[0];

            pending.Remove(
                matched);

            double flightMilliseconds =
                (impactUtc
                 - matched.InputUtc)
                .TotalMilliseconds;

            string method =
                candidates.Length == 1
                    ? "SINGLE_PENDING"
                    : "FIFO_AMBIGUOUS";

            return new DssProbeImpactCorrelation(
                nextImpact,
                impactUtc,
                frameSequence,
                changeRatio,
                matched.LaunchSequence,
                matched.InputUtc,
                flightMilliseconds,
                method,
                candidates.Length,
                matched.GeometryValid,
                matched.AimNormalizedX,
                matched.AimNormalizedY,
                matched.AimNormalizedRadius,
                matched.AimAngleDegrees,
                matched.AngularDiameterDegrees,
                matched.NearestPatternPoint,
                matched.NearestErrorPixels);
        }
    }

    public IReadOnlyList<DssProbeUnresolvedLaunch>
        Expire(
            DateTimeOffset nowUtc)
    {
        lock (sync)
        {
            DssProbeLaunchRecord[] expired =
                pending
                    .Where(
                        launch =>
                            nowUtc
                            - launch.InputUtc
                            > MaximumFlightTime)
                    .OrderBy(
                        launch =>
                            launch.InputUtc)
                    .ToArray();

            if (expired.Length == 0)
            {
                return Array.Empty<
                    DssProbeUnresolvedLaunch>();
            }

            foreach (DssProbeLaunchRecord launch
                     in expired)
            {
                pending.Remove(
                    launch);
            }

            return expired
                .Select(
                    launch =>
                        new DssProbeUnresolvedLaunch(
                            launch.LaunchSequence,
                            launch.InputUtc,
                            nowUtc,
                            (nowUtc
                             - launch.InputUtc)
                                .TotalMilliseconds,
                            "NO_IMPACT_WITHIN_45S",
                            launch.GeometryValid,
                            launch.AimNormalizedRadius))
                .ToArray();
        }
    }
}
