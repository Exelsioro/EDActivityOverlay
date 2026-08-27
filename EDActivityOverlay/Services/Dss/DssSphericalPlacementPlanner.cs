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

        int officialTargetN =
            ResolveTargetCount(
                requestedTarget,
                targetSource);

        DssEngineeringTargetResolution targetResolution =
            DssEngineeringTargetResolver.Resolve(
                officialTargetN,
                targetSource,
                dssModule);

        int targetN =
            targetResolution.TargetCount;

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
        // CoverageObserver sees only the projected near hemisphere. It cannot
        // measure missing area on the rear hemisphere and therefore must never
        // arbitrate every correction shot.
        //
        // v47 live validation demonstrated the failure mode very clearly:
        // after the N13 base batch, corrections 14..18 all became r=0.68
        // near-side holes, while the scan completed only after the commander
        // manually sent the last three launches behind the horizon.
        //
        // Correction ownership is therefore explicit:
        //   1,3,5,7 -> rear hemisphere (geometry-driven, stable for the step)
        //   2,4,6,8 -> near hemisphere (coverage CV when trustworthy)
        //
        // This also removes the old "far point flashes, then snaps near"
        // behaviour. That happened because CoverageObserver temporarily returns
        // Empty while settling after an impact; the old planner showed the far
        // fallback during that gap and replaced it with a near CV target later.
        bool rearHemisphereSlot =
            (correctionIndex & 1) == 1;

        if (rearHemisphereSlot)
        {
            int rearOrdinal =
                (correctionIndex + 1) / 2;

            return ResolveRearHemisphereCorrection(
                rearOrdinal,
                sequentialStep,
                targetN,
                angularDiameterDegrees);
        }

        DssCoverageObservation coverage =
            coverageObservation
            ?? DssCoverageObservation.Empty;

        if (coverage.Available
            && !coverage.Settling
            && coverage.Confidence >= 0.45d
            && coverage.SuggestedCandidateId > 0
            && coverage.SuggestedUncoveredScore >= 0.24d
            && !DssProbeAimSolver.IsCoverageCandidateUsed(
                usedCoverageCandidates,
                coverage.SuggestedCandidateId))
        {
            SphericalPoint p =
                DssSphericalProjection.ProjectScreenAimToSpherical(
                    coverage.SuggestedNormalizedX,
                    coverage.SuggestedNormalizedY,
                    angularDiameterDegrees);

            (double nx, double ny, double k) =
                DssSphericalProjection.ProjectSphericalToScreenAim(
                    p,
                    angularDiameterDegrees);

            DssAimZone zone =
                k > 1.0d
                    ? DssAimZone.FarSide
                    : (k >= 0.80d
                        ? DssAimZone.Limb
                        : DssAimZone.Disc);

            return new DssSphericalAimTarget(
                true,
                sequentialStep,
                p,
                nx,
                ny,
                k,
                zone,
                "CORRECTION_COVERAGE_NEAR",
                targetN,
                coverage.SuggestedCandidateId,
                coverage.SuggestedUncoveredScore);
        }

        // During CV settle/unavailability keep this correction on the front
        // hemisphere instead of temporarily showing a rear target that will be
        // revoked one frame later. The deterministic fallback chooses a large
        // geometric hole in the visible hemisphere.
        int nearOrdinal =
            correctionIndex / 2;

        return ResolveNearHemisphereFallback(
            nearOrdinal,
            sequentialStep,
            targetN,
            angularDiameterDegrees);
    }

    private static DssSphericalAimTarget ResolveRearHemisphereCorrection(
        int rearOrdinal,
        int sequentialStep,
        int targetN,
        double angularDiameterDegrees)
    {
        IReadOnlyList<SphericalPoint> basePoints =
            GenerateOptimalSphericalPoints(
                targetN);

        var occupied =
            new List<SphericalPoint>(
                basePoints);

        SphericalPoint selected =
            new(
                2.50d,
                -Math.PI / 4d);

        for (int ordinal = 1;
             ordinal <= rearOrdinal;
             ordinal++)
        {
            selected =
                SelectLargestSphericalGap(
                    occupied,
                    BuildRearCorrectionCandidates());

            occupied.Add(
                selected);
        }

        (double nx, double ny, double k) =
            DssSphericalProjection.ProjectSphericalToScreenAim(
                selected,
                angularDiameterDegrees);

        return new DssSphericalAimTarget(
            true,
            sequentialStep,
            selected,
            nx,
            ny,
            k,
            DssAimZone.FarSide,
            "CORRECTION_FAR_BALANCE",
            targetN);
    }

    private static DssSphericalAimTarget ResolveNearHemisphereFallback(
        int nearOrdinal,
        int sequentialStep,
        int targetN,
        double angularDiameterDegrees)
    {
        IReadOnlyList<SphericalPoint> basePoints =
            GenerateOptimalSphericalPoints(
                targetN);

        var occupied =
            new List<SphericalPoint>(
                basePoints);

        SphericalPoint selected =
            new(
                Math.PI / 3d,
                0d);

        for (int ordinal = 1;
             ordinal <= nearOrdinal;
             ordinal++)
        {
            selected =
                SelectLargestSphericalGap(
                    occupied,
                    BuildNearCorrectionCandidates());

            occupied.Add(
                selected);
        }

        (double nx, double ny, double k) =
            DssSphericalProjection.ProjectSphericalToScreenAim(
                selected,
                angularDiameterDegrees);

        DssAimZone zone =
            k >= 0.80d
                ? DssAimZone.Limb
                : DssAimZone.Disc;

        return new DssSphericalAimTarget(
            true,
            sequentialStep,
            selected,
            nx,
            ny,
            k,
            zone,
            "CORRECTION_NEAR_FALLBACK",
            targetN);
    }

    private static IReadOnlyList<SphericalPoint>
        BuildRearCorrectionCandidates()
    {
        var result =
            new List<SphericalPoint>();

        // Three rear latitudes. The deepest ring still projects inside the
        // rear-antipode circle defined by the supplied DSS firing-pattern
        // guide, so no correction needs the ambiguous outer antipode->MISS
        // annulus.
        foreach (double theta
                 in new[]
                 {
                     2.18d, // ~125 deg
                     2.50d, // ~143 deg
                     2.82d  // ~162 deg
                 })
        {
            double phase =
                theta < 2.3d
                    ? 0d
                    : theta < 2.7d
                        ? Math.PI / 8d
                        : Math.PI / 4d;

            for (int i = 0;
                 i < 8;
                 i++)
            {
                result.Add(
                    new SphericalPoint(
                        theta,
                        phase
                        + i * Math.PI / 4d));
            }
        }

        // Exact antipode is a useful candidate when the base plan leaves the
        // rear pole uncovered.
        result.Add(
            new SphericalPoint(
                Math.PI,
                0d));

        return result;
    }

    private static IReadOnlyList<SphericalPoint>
        BuildNearCorrectionCandidates()
    {
        var result =
            new List<SphericalPoint>();

        foreach (double theta
                 in new[]
                 {
                     0.45d,
                     0.85d,
                     1.25d
                 })
        {
            double phase =
                theta < 0.7d
                    ? Math.PI / 8d
                    : 0d;

            for (int i = 0;
                 i < 8;
                 i++)
            {
                result.Add(
                    new SphericalPoint(
                        theta,
                        phase
                        + i * Math.PI / 4d));
            }
        }

        result.Add(
            new SphericalPoint(
                0d,
                0d));

        return result;
    }

    private static SphericalPoint SelectLargestSphericalGap(
        IReadOnlyList<SphericalPoint> occupied,
        IReadOnlyList<SphericalPoint> candidates)
    {
        if (candidates.Count == 0)
        {
            return new SphericalPoint(
                Math.PI,
                0d);
        }

        SphericalPoint best =
            candidates[0];

        double bestMinimumDistance =
            double.NegativeInfinity;

        foreach (SphericalPoint candidate
                 in candidates)
        {
            double minimumDistance =
                Math.PI;

            foreach (SphericalPoint existing
                     in occupied)
            {
                double dot =
                    candidate.X * existing.X
                    + candidate.Y * existing.Y
                    + candidate.Z * existing.Z;

                double distance =
                    Math.Acos(
                        Math.Clamp(
                            dot,
                            -1d,
                            1d));

                minimumDistance =
                    Math.Min(
                        minimumDistance,
                        distance);
            }

            if (minimumDistance
                > bestMinimumDistance)
            {
                bestMinimumDistance =
                    minimumDistance;

                best =
                    candidate;
            }
        }

        return best;
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
