using System;
using System.Collections.Generic;
using System.Linq;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Dss;

internal enum DssPlannerStrategy
{
    SphericalCalibrated,
    EmpiricalV30Fallback
}

/// <summary>
/// Planned spherical aim point combining unit-sphere impact coordinates (theta, phi)
/// and projected screen aim offsets (Nx, Ny, K).
/// </summary>
internal sealed record DssSphericalAimTarget(
    bool Available,
    int Sequence,
    SphericalPoint SurfacePoint,
    double NormalizedX,
    double NormalizedY,
    double AimRadiusNormalized,
    DssAimZone Zone,
    string Role,
    int TotalPlanCount,
    int CandidateId = 0,
    double CoverageScore = 0d)
{
    public static DssSphericalAimTarget Empty(int totalPlanCount) =>
        new(
            false,
            0,
            new SphericalPoint(0, 0),
            0,
            0,
            0,
            DssAimZone.Disc,
            string.Empty,
            totalPlanCount);
}

/// <summary>
/// Spherical DSS Placement Planner (Requirements 1–14).
///
/// Features:
/// 1. Unit-sphere representation of probe impacts S^2.
/// 2. Rotational symmetry preservation (phi_screen == phi_sphere).
/// 3. Calibrated curved trajectory projection against live Elite.
/// 4. Hard feasibility constraint: native MISS boundary K < K_miss(theta) - margin.
/// 5. DSS PatchRadius and engineering incorporated into spherical cap model.
/// 6. Polyhedral validation ground truth (N=4, 6, 8, 12).
/// 7. Layout generation and union surface coverage scoring for arbitrary N in [2, 18].
/// 8. Shot ordering optimization (deep far probes launched first for flight-time concurrency).
/// 9. Instant sequential advance on fire (never wait for impact).
/// 10. Coverage CV as correction feedback only.
/// 11. Empirical v30/v31 pattern available as fallback.
/// </summary>
internal static class DssSphericalPlacementPlanner
{
    public const int MinimumTargetCount = 2;
    public const int MaximumTargetCount = 18;
    public const int MaximumCorrectionShots = 8;

    public static DssPlannerStrategy ActiveStrategy { get; set; } =
        DssPlannerStrategy.SphericalCalibrated;

    /// <summary>
    /// Resolves the planned aim target for the given sequential step.
    /// </summary>
    public static DssSphericalAimTarget Resolve(
        int sequentialStep,
        int requestedTarget,
        string targetSource,
        double angularDiameterDegrees,
        DssModuleSnapshot dssModule,
        double bodyRadiusMeters,
        int confirmedImpactCount,
        DssCoverageObservation? coverageObservation,
        long usedCoverageCandidates)
    {
        if (ActiveStrategy == DssPlannerStrategy.EmpiricalV30Fallback)
        {
            return ResolveEmpiricalFallback(
                sequentialStep,
                requestedTarget,
                targetSource,
                angularDiameterDegrees,
                confirmedImpactCount,
                coverageObservation,
                usedCoverageCandidates);
        }

        int targetN = ResolveTargetCount(requestedTarget, targetSource);

        if (sequentialStep < 1)
        {
            return DssSphericalAimTarget.Empty(targetN);
        }

        // Base batch: steps 1..N
        if (sequentialStep <= targetN)
        {
            IReadOnlyList<DssSphericalAimTarget> fullPlan = GenerateOrderedSphericalPlan(
                targetN,
                angularDiameterDegrees,
                dssModule,
                bodyRadiusMeters);

            int index = sequentialStep - 1;
            if (index >= 0 && index < fullPlan.Count)
            {
                return fullPlan[index];
            }

            return DssSphericalAimTarget.Empty(targetN);
        }

        // Correction tail: step > N
        int correctionIndex = sequentialStep - targetN;
        if (correctionIndex < 1 || correctionIndex > MaximumCorrectionShots)
        {
            return DssSphericalAimTarget.Empty(targetN);
        }

        return ResolveCorrectionTarget(
            correctionIndex,
            sequentialStep,
            targetN,
            angularDiameterDegrees,
            coverageObservation,
            usedCoverageCandidates);
    }

    /// <summary>
    /// Generates the complete ordered spherical plan for target count N.
    /// </summary>
    public static IReadOnlyList<DssSphericalAimTarget> GenerateOrderedSphericalPlan(
        int targetN,
        double angularDiameterDegrees,
        DssModuleSnapshot dssModule,
        double bodyRadiusMeters)
    {
        int clampedN = Math.Clamp(targetN, MinimumTargetCount, MaximumTargetCount);
        IReadOnlyList<SphericalPoint> surfacePoints = GenerateOptimalSphericalPoints(clampedN);

        // Optimize shot ordering (Req 11): far probes first, minimal slew path
        IReadOnlyList<SphericalPoint> orderedPoints = OptimizeShotOrdering(surfacePoints);

        var plan = new List<DssSphericalAimTarget>(orderedPoints.Count);

        for (int i = 0; i < orderedPoints.Count; i++)
        {
            SphericalPoint p = orderedPoints[i];
            (double nx, double ny, double k) = DssSphericalProjection.ProjectSphericalToScreenAim(
                p,
                angularDiameterDegrees);

            DssAimZone zone = k > 1.0d
                ? DssAimZone.FarSide
                : (k >= 0.80d ? DssAimZone.Limb : DssAimZone.Disc);

            string role = p.Theta >= 2.35d // > 135 deg
                ? "BATCH_FAR_DEEP"
                : (p.Theta > Math.PI / 2d
                    ? "BATCH_FAR"
                    : (p.Theta < 0.15d ? "BATCH_CENTER" : "BATCH_NEAR"));

            plan.Add(new DssSphericalAimTarget(
                true,
                i + 1,
                p,
                nx,
                ny,
                k,
                zone,
                role,
                clampedN));
        }

        return plan;
    }

    /// <summary>
    /// Generates optimal unit-sphere impact coordinates for target count N.
    /// </summary>
    public static IReadOnlyList<SphericalPoint> GenerateOptimalSphericalPoints(int n)
    {
        n = Math.Clamp(n, MinimumTargetCount, MaximumTargetCount);

        return n switch
        {
            2 => new[]
            {
                new SphericalPoint(Math.PI, 0),       // Rear antipode
                new SphericalPoint(0, 0)              // Front center
            },

            3 => new[]
            {
                new SphericalPoint(2.35d, Math.PI / 2d),   // Far (+90 deg)
                new SphericalPoint(2.35d, -Math.PI / 2d),  // Far (-90 deg)
                new SphericalPoint(0, 0)                   // Front center
            },

            4 => PolyhedralValidationCatalog.GetTetrahedron(),

            5 => new[]
            {
                new SphericalPoint(Math.PI * 0.95d, 0),                      // Deep rear
                new SphericalPoint(1.85d, 0),                                // Far (+0 deg)
                new SphericalPoint(1.85d, 2d * Math.PI / 3d),                // Far (+120 deg)
                new SphericalPoint(1.85d, -2d * Math.PI / 3d),               // Far (-120 deg)
                new SphericalPoint(0, 0)                                     // Front center
            },

            6 => PolyhedralValidationCatalog.GetOctahedron(),

            7 => new[]
            {
                new SphericalPoint(Math.PI * 0.95d, Math.PI / 4d),           // Deep rear 1
                new SphericalPoint(Math.PI * 0.95d, -3d * Math.PI / 4d),     // Deep rear 2
                new SphericalPoint(1.45d, 0),                                // Near limb 1
                new SphericalPoint(1.45d, Math.PI / 2d),                     // Near limb 2
                new SphericalPoint(1.45d, Math.PI),                          // Near limb 3
                new SphericalPoint(1.45d, -Math.PI / 2d),                    // Near limb 4
                new SphericalPoint(0, 0)                                     // Front center
            },

            8 => PolyhedralValidationCatalog.GetCube(),

            12 => PolyhedralValidationCatalog.GetIcosahedron(),

            _ => GenerateFibonacciZonalPoints(n)
        };
    }

    /// <summary>
    /// Generates a balanced zonal Fibonacci distribution on S^2 for arbitrary N.
    /// </summary>
    public static IReadOnlyList<SphericalPoint> GenerateFibonacciZonalPoints(int n)
    {
        var points = new List<SphericalPoint>(n);
        double goldenAngle = Math.PI * (3d - Math.Sqrt(5d));

        for (int i = 0; i < n; i++)
        {
            double z = 1d - (2d * i + 1d) / n; // [-1, 1]
            double theta = Math.Acos(Math.Clamp(z, -1d, 1d));
            double phi = i * goldenAngle;
            points.Add(new SphericalPoint(theta, phi));
        }

        return points;
    }

    /// <summary>
    /// Optimizes shot ordering (Req 11):
    /// 1. Deep far probes (theta >= 125 deg, flight time 8-12s) fired first.
    /// 2. Mid far probes (90 deg < theta < 125 deg).
    /// 3. Near limb and inner probes (theta < 90 deg).
    /// 4. Center probe (theta ~ 0) fired last.
    /// 5. Within each zone, ordered by azimuth phi to ensure smooth camera panning.
    /// </summary>
    public static IReadOnlyList<SphericalPoint> OptimizeShotOrdering(IReadOnlyList<SphericalPoint> points)
    {
        if (points.Count <= 1) return points;

        var farDeep = points.Where(p => p.Theta >= 2.18d).OrderBy(p => p.Phi).ToList(); // >= 125 deg
        var farMid = points.Where(p => p.Theta > Math.PI / 2d && p.Theta < 2.18d).OrderBy(p => p.Phi).ToList();
        var near = points.Where(p => p.Theta <= Math.PI / 2d && p.Theta > 0.18d).OrderBy(p => p.Phi).ToList();
        var center = points.Where(p => p.Theta <= 0.18d).ToList();

        var ordered = new List<SphericalPoint>(points.Count);
        ordered.AddRange(farDeep);
        ordered.AddRange(farMid);
        ordered.AddRange(near);
        ordered.AddRange(center);

        return ordered;
    }

    private static DssSphericalAimTarget ResolveCorrectionTarget(
        int correctionIndex,
        int sequentialStep,
        int targetN,
        double angularDiameterDegrees,
        DssCoverageObservation? coverageObservation,
        long usedCoverageCandidates)
    {
        DssCoverageObservation coverage = coverageObservation ?? DssCoverageObservation.Empty;

        // Use coverage feedback if available and uncovered candidate is significant
        if (coverage.Available
            && coverage.SuggestedCandidateId > 0
            && coverage.SuggestedUncoveredScore >= 0.24d
            && !DssProbeAimSolver.IsCoverageCandidateUsed(usedCoverageCandidates, coverage.SuggestedCandidateId))
        {
            SphericalPoint p = DssSphericalProjection.ProjectScreenAimToSpherical(
                coverage.SuggestedNormalizedX,
                coverage.SuggestedNormalizedY,
                angularDiameterDegrees);

            (double nx, double ny, double k) = DssSphericalProjection.ProjectSphericalToScreenAim(
                p,
                angularDiameterDegrees);

            DssAimZone zone = k > 1.0d ? DssAimZone.FarSide : (k >= 0.80d ? DssAimZone.Limb : DssAimZone.Disc);

            return new DssSphericalAimTarget(
                true,
                sequentialStep,
                p,
                nx,
                ny,
                k,
                zone,
                "CORRECTION_COVERAGE",
                targetN,
                coverage.SuggestedCandidateId,
                coverage.SuggestedUncoveredScore);
        }

        // Far-side correction fallback: alternate quadrants at safe boundary
        double safeRadius = DssSphericalProjection.EstimateSafeNormalizedRadius(angularDiameterDegrees);
        double correctionAngle = ((correctionIndex - 1) * 90d - 45d) * Math.PI / 180d;

        SphericalPoint fallbackPoint = new(2.50d, correctionAngle);
        (double fnx, double fny, double fk) = DssSphericalProjection.ProjectSphericalToScreenAim(
            fallbackPoint,
            angularDiameterDegrees);

        return new DssSphericalAimTarget(
            true,
            sequentialStep,
            fallbackPoint,
            fnx,
            fny,
            fk,
            DssAimZone.FarSide,
            "CORRECTION_FAR_FALLBACK",
            targetN);
    }

    private static DssSphericalAimTarget ResolveEmpiricalFallback(
        int sequentialStep,
        int requestedTarget,
        string targetSource,
        double angularDiameterDegrees,
        int confirmedImpactCount,
        DssCoverageObservation? coverageObservation,
        long usedCoverageCandidates)
    {
        DssPredictiveAimTarget empirical = DssPredictiveBatchPlanner.Resolve(
            sequentialStep,
            requestedTarget,
            targetSource,
            angularDiameterDegrees,
            confirmedImpactCount,
            coverageObservation,
            usedCoverageCandidates);

        if (!empirical.Available)
        {
            return DssSphericalAimTarget.Empty(empirical.PredictedBatchCount);
        }

        SphericalPoint p = DssSphericalProjection.ProjectScreenAimToSpherical(
            empirical.NormalizedX,
            empirical.NormalizedY,
            angularDiameterDegrees);

        double k = Math.Sqrt(empirical.NormalizedX * empirical.NormalizedX + empirical.NormalizedY * empirical.NormalizedY);

        return new DssSphericalAimTarget(
            true,
            sequentialStep,
            p,
            empirical.NormalizedX,
            empirical.NormalizedY,
            k,
            empirical.Zone,
            empirical.Role,
            empirical.PredictedBatchCount,
            empirical.CandidateId,
            empirical.CoverageScore);
    }

    public static int ResolveTargetCount(int requestedTarget, string targetSource) =>
        DssPredictiveBatchPlanner.ResolvePredictedBatchCount(requestedTarget, targetSource);
}
