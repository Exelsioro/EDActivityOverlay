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
    DssAimZone Zone,
    int CandidateId = 0,
    double CoverageScore = 0);

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
        DssHudGeometry geometry) =>
        Solve(
            state,
            readiness,
            geometry,
            sequentialStep: 1,
            scanComplete: false);

    public static DssProjectedAimPlan Solve(
        GameStateSnapshot state,
        DssAssistantReadinessSnapshot readiness,
        DssHudGeometry geometry,
        int sequentialStep,
        bool scanComplete) =>
        Solve(
            state,
            readiness,
            geometry,
            sequentialStep,
            scanComplete,
            confirmedImpactCount: int.MaxValue,
            coverageObservation: null,
            usedCoverageCandidates: 0);

    public static DssProjectedAimPlan Solve(
        GameStateSnapshot state,
        DssAssistantReadinessSnapshot readiness,
        DssHudGeometry geometry,
        int sequentialStep,
        bool scanComplete,
        int confirmedImpactCount,
        DssCoverageObservation? coverageObservation,
        long usedCoverageCandidates)
    {
        if (scanComplete
            || !readiness.IsReady
            || !geometry.BodyCenterFound
            || !geometry.HorizonMarkerFound
            || geometry.HorizonRadiusPixels <= 25
            || !IsWithinTargetingV1Calibration(
                readiness.AngularDiameterDegrees))
        {
            return DssProjectedAimPlan.Empty;
        }

        if (!TryResolvePredictiveTarget(
                state,
                sequentialStep,
                readiness.AngularDiameterDegrees,
                confirmedImpactCount,
                coverageObservation,
                usedCoverageCandidates,
                out DssPredictiveAimTarget target,
                out string source))
        {
            return DssProjectedAimPlan.Empty;
        }

        DssProjectedAimPoint point =
            new(
                sequentialStep,
                target.NormalizedX,
                target.NormalizedY,
                geometry.BodyCenterX
                    + target.NormalizedX
                      * geometry.HorizonRadiusPixels,
                geometry.BodyCenterY
                    + target.NormalizedY
                      * geometry.HorizonRadiusPixels,
                target.Zone,
                target.CandidateId,
                target.CoverageScore);

        return new DssProjectedAimPlan(
            target.PredictedBatchCount,
            $"TARGETING_V2_{target.Role}/{source}/N{target.PredictedBatchCount}",
            new[] { point });
    }

    internal static bool TryResolvePredictiveTarget(
        GameStateSnapshot state,
        int sequentialStep,
        double angularDiameterDegrees,
        int confirmedImpactCount,
        DssCoverageObservation? coverageObservation,
        long usedCoverageCandidates,
        out DssPredictiveAimTarget target,
        out string source)
    {
        (int requestedTarget, string resolvedSource) =
            ResolveEfficiencyTarget(
                state);

        source = resolvedSource;

        target =
            DssPredictiveBatchPlanner.Resolve(
                sequentialStep,
                requestedTarget,
                source,
                angularDiameterDegrees,
                confirmedImpactCount,
                coverageObservation,
                usedCoverageCandidates);

        return target.Available;
    }

    internal static bool IsCoverageCandidateUsed(
        long mask,
        int candidateId)
    {
        if (candidateId <= 0
            || candidateId >= 63)
        {
            return false;
        }

        return (mask
                & (1L << candidateId))
               != 0;
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
                        DssPredictiveBatchPlanner.MinimumBatchCount,
                        DssPredictiveBatchPlanner.MaximumBatchCount),
                    "BODY");
            }
        }

        int configured =
            SettingsService.Instance.Settings
                .DssEfficiencyTarget;

        return (
            Math.Clamp(
                configured,
                DssPredictiveBatchPlanner.MinimumBatchCount,
                DssPredictiveBatchPlanner.MaximumBatchCount),
            "SETTINGS");
    }
}
