using System;
using System.Collections.Generic;
using System.Linq;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using EDActivityOverlay.Services.Journal;

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
            confirmedImpactCount: 0,
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
            || !IsSphericalTargetingOperational(
                readiness.AngularDiameterDegrees))
        {
            return DssProjectedAimPlan.Empty;
        }

        if (!TryResolveSphericalTarget(
                state,
                readiness,
                sequentialStep,
                confirmedImpactCount,
                coverageObservation,
                usedCoverageCandidates,
                out DssSphericalAimTarget target,
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
            target.TotalPlanCount,
            $"TARGETING_V3_{target.Role}/{source}/N{target.TotalPlanCount}",
            new[] { point });
    }

    /// <summary>
    /// Production overload: scanner engineering is supplied from the immutable
    /// DSS session context, avoiding Journal disk I/O on the HUD hot path.
    /// </summary>
    public static DssProjectedAimPlan Solve(
        GameStateSnapshot state,
        DssAssistantReadinessSnapshot readiness,
        DssHudGeometry geometry,
        int sequentialStep,
        bool scanComplete,
        DssModuleSnapshot dssModule,
        int confirmedImpactCount,
        DssCoverageObservation? coverageObservation,
        long usedCoverageCandidates)
    {
        if (scanComplete)
        {
            return
                DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    sequentialStep,
                    scanComplete: true,
                    geometry,
                    DssProjectedAimPlan.Empty);
        }

        bool geometryUsable =
            readiness.IsReady
            && geometry.BodyCenterFound
            && geometry.HorizonMarkerFound
            && geometry.HorizonRadiusPixels > 25
            && IsSphericalTargetingOperational(
                readiness.AngularDiameterDegrees);

        if (!geometryUsable)
        {
            return
                DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    sequentialStep,
                    scanComplete: false,
                    geometry,
                    DssProjectedAimPlan.Empty);
        }

        (int requestedTarget, string source) =
            ResolveEfficiencyTarget(
                state);

        // SETTINGS fallback is never authoritative for DSS geometry. A wrong
        // fallback N poisons both batch size and, more importantly, the
        // calibrated spherical-cap radius used by rear corrections.
        //
        // The supplied 58 Eridani DE 1 run visibly showed native N=8 while
        // shots.csv was planned as SETTINGS/N6. Do not allow that state again.
        if (DssNativeEfficiencyTargetRuntime.TryGetFresh(
                out DssNativeEfficiencyTargetSnapshot nativeTarget))
        {
            requestedTarget =
                nativeTarget.Target;

            source =
                "HUD_CV";
        }
        else if (source.Equals(
                     "SETTINGS",
                     StringComparison.OrdinalIgnoreCase))
        {
            return
                DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    sequentialStep,
                    scanComplete: false,
                    geometry,
                    DssProjectedAimPlan.Empty);
        }

        DssSphericalAimTarget target =
            DssSphericalPlacementPlanner.Resolve(
                sequentialStep,
                requestedTarget,
                source,
                readiness.AngularDiameterDegrees,
                dssModule,
                readiness.BodyRadiusMeters,
                confirmedImpactCount,
                coverageObservation,
                usedCoverageCandidates);

        DssProjectedAimPlan rawPlan =
            DssProjectedAimPlan.Empty;

        if (target.Available)
        {
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

            rawPlan =
                new DssProjectedAimPlan(
                    target.TotalPlanCount,
                    $"TARGETING_V3_{target.Role}/{source}/N{target.TotalPlanCount}",
                    new[] { point });
        }

        return
            DssActionableTargetLeaseRuntime.Resolve(
                state,
                sequentialStep,
                scanComplete: false,
                geometry,
                rawPlan);
    }
    internal static bool TryResolveSphericalTarget(
        GameStateSnapshot state,
        DssAssistantReadinessSnapshot readiness,
        int sequentialStep,
        int confirmedImpactCount,
        DssCoverageObservation? coverageObservation,
        long usedCoverageCandidates,
        out DssSphericalAimTarget target,
        out string source)
    {
        if (!IsSphericalTargetingOperational(
                readiness.AngularDiameterDegrees))
        {
            target =
                DssSphericalAimTarget.Empty(0);

            source =
                string.Empty;

            return false;
        }

        (int requestedTarget, string resolvedSource) =
            ResolveEfficiencyTarget(
                state);

        source = resolvedSource;

        DssModuleSnapshot dssModule = DssJournalContextReader.ReadLatestDssModule(
            JournalMonitorService.Instance.JournalDirectory);

        target =
            DssSphericalPlacementPlanner.Resolve(
                sequentialStep,
                requestedTarget,
                source,
                readiness.AngularDiameterDegrees,
                dssModule,
                readiness.BodyRadiusMeters,
                confirmedImpactCount,
                coverageObservation,
                usedCoverageCandidates);

        return target.Available;
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
        if (!IsWithinTargetingV1Calibration(
                angularDiameterDegrees))
        {
            target =
                DssPredictiveAimTarget.Empty(
                    0,
                    0);

            source =
                string.Empty;

            return false;
        }

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
    /// <summary>
    /// The spherical planner no longer aims near the native MISS boundary:
    /// its farthest base-plan endpoint is the rear-antipode circle halfway
    /// between horizon and MISS. Therefore the old 21..28 degree v1 radial
    /// sweep envelope must not hide targets that the readiness evaluator has
    /// already classified as READY.
    ///
    /// The legacy IsWithinTargetingV1Calibration gate remains below for the
    /// old predictive/empirical fallback only.
    /// </summary>
    internal static bool IsSphericalTargetingOperational(
        double angularDiameterDegrees) =>
        double.IsFinite(angularDiameterDegrees)
        && angularDiameterDegrees > 0d;
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
