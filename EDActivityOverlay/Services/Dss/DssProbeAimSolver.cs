using System;
using System.Collections.Generic;
using System.Linq;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssProjectedAimPoint(
    int Sequence,
    double NormalizedX,
    double NormalizedY,
    double ScreenX,
    double ScreenY,
    DssAimZone Zone);

internal sealed record DssProjectedAimPlan(
    int EfficiencyTarget,
    string Source,
    IReadOnlyList<DssProjectedAimPoint> Points)
{
    public static DssProjectedAimPlan Empty { get; } =
        new(
            0,
            string.Empty,
            Array.Empty<DssProjectedAimPoint>());

    public bool IsAvailable =>
        Points.Count > 0;
}

/// <summary>
/// DSS Targeting v1.
///
/// Uses the empirically measured native-MISS boundary from the clean v23
/// radial sweeps and projects one conservative actionable aim point:
///
///     Ksafe(theta) = Kboundary(theta) - safetyMargin
///     Pscreen = C + direction * Ksafe(theta) * Rh
///
/// This is still not the coverage planner. Its only purpose is to validate the
/// end-to-end live geometry -> screen target -> real DSS trajectory chain.
/// </summary>
internal static class DssProbeAimSolver
{
    // Targeting v1 is deliberately limited to the angular-diameter range
    // covered by the clean v23 radial sweeps. Do not extrapolate this first
    // empirical model outside the measured envelope.
    internal const double TargetingV1MinimumAngularDiameterDegrees = 21d;
    internal const double TargetingV1MaximumAngularDiameterDegrees = 28d;
    internal const double TargetingV1SafetyMarginNormalized = 0.05d;

    // Least-squares fit to the clean pre-shot v23 MISS-boundary sweeps:
    //   21.39 deg -> 1.7402 Rh
    //   22.48 deg -> 1.7414 Rh
    //   23.21 deg -> 1.7326 Rh
    //   24.22 deg -> 1.7225 Rh
    // Kboundary(theta) ~= intercept + slope * theta.
    private const double TargetingV1BoundaryIntercept = 1.88392783d;
    private const double TargetingV1BoundarySlope = -0.00656091d;
    public static DssProjectedAimPlan Solve(
        GameStateSnapshot state,
        DssAssistantReadinessSnapshot readiness,
        DssHudGeometry geometry)
    {
        if (!readiness.IsReady
            || !geometry.BodyCenterFound
            || !geometry.HorizonMarkerFound
            || geometry.HorizonRadiusPixels <= 25
            || !IsWithinTargetingV1Calibration(
                readiness.AngularDiameterDegrees))
        {
            return DssProjectedAimPlan.Empty;
        }

        (int target, string source) =
            ResolveEfficiencyTarget(
                state);

        DssProbePattern pattern =
            DssProbePatternCatalog.Get(
                target);

        // v1 deliberately emits ONE actionable point. Coverage planning comes
        // later; for now we only need to prove the live geometry -> aim-point
        // chain against Elite's native MISS indicator and real probe shots.
        // Reuse the first non-centre catalog direction so the orientation is
        // deterministic and already compatible with the future planner.
        DssAimPoint? directionPoint =
            pattern.Points
                .OrderBy(
                    point => point.Sequence)
                .FirstOrDefault(
                    point =>
                        Math.Sqrt(
                            point.X * point.X
                            + point.Y * point.Y) > 0.05d);

        double directionX = 0d;
        double directionY = -1d;

        if (directionPoint is not null)
        {
            double length =
                Math.Sqrt(
                    directionPoint.X * directionPoint.X
                    + directionPoint.Y * directionPoint.Y);

            if (length > 0.001d)
            {
                directionX =
                    directionPoint.X / length;
                directionY =
                    directionPoint.Y / length;
            }
        }

        double safeRadius =
            EstimateSafeNormalizedRadius(
                readiness.AngularDiameterDegrees);

        double normalizedX =
            directionX * safeRadius;

        double normalizedY =
            directionY * safeRadius;

        DssProjectedAimPoint point =
            new(
                1,
                normalizedX,
                normalizedY,
                geometry.BodyCenterX
                    + normalizedX
                      * geometry.HorizonRadiusPixels,
                geometry.BodyCenterY
                    + normalizedY
                      * geometry.HorizonRadiusPixels,
                DssAimZone.FarSide);

        return new DssProjectedAimPlan(
            pattern.EfficiencyTarget,
            $"TARGETING_V1/{source}",
            new[] { point });
    }

    internal static bool IsWithinTargetingV1Calibration(
        double angularDiameterDegrees) =>
        double.IsFinite(angularDiameterDegrees)
        && angularDiameterDegrees
            >= TargetingV1MinimumAngularDiameterDegrees
        && angularDiameterDegrees
            <= TargetingV1MaximumAngularDiameterDegrees;

    internal static double EstimateBoundaryNormalizedRadius(
        double angularDiameterDegrees)
    {
        double clamped =
            Math.Clamp(
                angularDiameterDegrees,
                TargetingV1MinimumAngularDiameterDegrees,
                TargetingV1MaximumAngularDiameterDegrees);

        return TargetingV1BoundaryIntercept
               + TargetingV1BoundarySlope
                 * clamped;
    }

    internal static double EstimateSafeNormalizedRadius(
        double angularDiameterDegrees) =>
        EstimateBoundaryNormalizedRadius(
            angularDiameterDegrees)
        - TargetingV1SafetyMarginNormalized;

    private static (int Target, string Source)
        ResolveEfficiencyTarget(
            GameStateSnapshot state)
    {
        if (state.DestinationBodyId >= 0)
        {
            ExplorationBodySnapshot? body =
                state.ExplorationBodies
                    .FirstOrDefault(
                        item =>
                            item.BodyId
                            == state.DestinationBodyId);

            if (body?.EfficiencyTarget > 0)
            {
                return (
                    Math.Clamp(
                        body.EfficiencyTarget,
                        DssProbePatternCatalog.MinimumTarget,
                        DssProbePatternCatalog.MaximumTarget),
                    "BODY");
            }
        }

        int configured =
            SettingsService.Instance.Settings
                .DssEfficiencyTarget;

        return (
            Math.Clamp(
                configured,
                DssProbePatternCatalog.MinimumTarget,
                DssProbePatternCatalog.MaximumTarget),
            "SETTINGS");
    }
}
