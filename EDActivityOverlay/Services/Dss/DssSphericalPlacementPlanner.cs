using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Dss;

internal enum DssPlannerStrategy
{
    SphericalCalibrated,
    EmpiricalV30Fallback
}

/// <summary>
/// Planned spherical aim point combining unit-sphere impact coordinates
/// (theta, phi) and projected screen aim offsets (Nx, Ny, K).
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
    public static DssSphericalAimTarget Empty(
        int totalPlanCount) =>
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
/// Predictive spherical DSS placement planner.
///
/// Base batch:
/// - generated completely before the first shot;
/// - optimized directly for maximum whole-sphere union coverage using the
///   actual engineered cap footprint;
/// - covers both hemispheres;
/// - far/deep shots are launched first for flight-time concurrency;
/// - sequential advance remains fire-owned and never waits for impacts.
///
/// Correction tail:
/// - is hidden until Elite's native "Ударов" counter confirms every required
///   hit and native DSS coverage has settled below 100%;
/// - each correction waits for the preceding correction to appear in the
///   native hit counter and for coverage to settle again;
/// - whole-sphere incremental coverage chooses the global missing region;
/// - visible-side coverage CV may refine a nearby same-hemisphere correction,
///   but cannot replace a globally better rear-side correction.
///
/// Projection keeps the empirically established DSS geometry:
/// K=1 is the visible horizon and the rear antipode is halfway from horizon to
/// native MISS. v56 treats inner/outer rear trajectories as two empirical
/// trajectory families and distributes them across the batch globally instead
/// of selecting outer independently for every safe rear point.
/// </summary>
internal static class DssSphericalPlacementPlanner
{
    public const int MinimumTargetCount = 2;
    // Native Elite HUD research now includes N=21. The optimizer and
    // engineering resolver are generic in N, so keep a practical two-digit
    // ceiling rather than the old research-only N18 cap.
    public const int MaximumTargetCount = 32;
    public const int MaximumCorrectionShots = 8;

    private const double MinimumCoverageFeedbackConfidence = 0.45d;
    private const double MinimumCoverageFeedbackUncoveredScore = 0.24d;

    // CV observes only the projected visible hemisphere. It may refine the
    // model target only when it points into the same broad missing region.
    // Same-hemisphere CV may point at a different visible hole than the
    // theoretical optimum. Keep this broad enough to accept that correction,
    // while hemisphere gating still prevents any near-side observation from
    // replacing a rear-side global target.
    private const double MaximumCoverageOverrideAngularDistance = 1.35d;
    private const double MinimumCoverageOverrideGainRatio = 0.30d;
    private const double MinimumCoverageOverrideAbsoluteGain = 0.0005d;

    // Rear-trajectory assignment is an empirical projection problem, not an
    // S^2 union-coverage problem. The old per-point "outer whenever safe"
    // rule created a large discontinuity between the last inner K and the
    // first outer K. v56 balances the two trajectory families globally.
    private const double RearBranchScoreEpsilon = 1e-9d;

    private static readonly object CorrectionPlanCacheGate =
        new();

    private static readonly Dictionary<
        string,
        IReadOnlyList<DssCoverageCorrectionPoint>>
        CorrectionPlanCache =
            new(StringComparer.Ordinal);

    private static readonly object BasePlanCacheGate =
        new();

    private static readonly Dictionary<
        string,
        IReadOnlyList<SphericalPoint>>
        BasePlanCache =
            new(StringComparer.Ordinal);

    private static readonly object RearBranchPlanCacheGate =
        new();

    private static readonly Dictionary<
        string,
        IReadOnlyList<bool>>
        RearBranchPlanCache =
            new(StringComparer.Ordinal);

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
        // Production source priority:
        // 1. Native Elite HUD efficiency target read by CV.
        // 2. Journal BODY target if already known.
        // 3. Never build a spherical batch from SETTINGS fallback.
        //
        // This prevents the old N13-style failure where an arbitrary fallback
        // count was treated as the real body efficiency target.
        if (DssNativeEfficiencyTargetRuntime.TryGetFresh(
                out DssNativeEfficiencyTargetSnapshot nativeTarget))
        {
            requestedTarget =
                nativeTarget.Target;

            targetSource =
                "HUD_CV";
        }
        else if (targetSource.Equals(
                     "SETTINGS",
                     StringComparison.OrdinalIgnoreCase))
        {
            // SETTINGS is not a body-specific native target. Until HUD CV
            // locks, do not build a fictitious base batch.
            return
                DssSphericalAimTarget.Empty(
                    0);
        }

        if (ActiveStrategy
            == DssPlannerStrategy.EmpiricalV30Fallback)
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
            return
                DssSphericalAimTarget.Empty(
                    targetN);
        }

        // Predictive base batch: steps 1..N. It is deliberately independent of
        // impact count so the commander can fire the full efficient pattern
        // without waiting for long rear-side flight times.
        if (sequentialStep <= targetN)
        {
            IReadOnlyList<DssSphericalAimTarget> fullPlan =
                GenerateOrderedSphericalPlan(
                    targetN,
                    angularDiameterDegrees,
                    dssModule,
                    bodyRadiusMeters,
                    targetResolution.ActualCapAngularRadius);

            int index =
                sequentialStep - 1;

            if (index >= 0
                && index < fullPlan.Count)
            {
                return
                    fullPlan[index];
            }

            return
                DssSphericalAimTarget.Empty(
                    targetN);
        }

        int correctionIndex =
            sequentialStep - targetN;

        DssNativeScanProgressRuntime.ObserveTargetingStep(
            targetN,
            sequentialStep);

        if (correctionIndex < 1
            || correctionIndex
               > MaximumCorrectionShots)
        {
            return
                DssSphericalAimTarget.Empty(
                    targetN);
        }

        // Correction gating is authoritative native-HUD telemetry now.
        //
        // The old confirmedImpactCount is produced by the experimental visual
        // impact detector. Live v52 research showed it can reach N while
        // Elite's own "Ударов" counter is still N-1, which made a false
        // correction target appear before the final probe had actually landed.
        //
        // Require BOTH:
        //   1. native hit count >= every shot that must have landed;
        //   2. native DSS coverage < 100% and unchanged long enough to settle.
        //
        // 100% immediately suppresses corrections while SAAScanComplete is
        // still travelling through the Journal pipeline.
        int requiredNativeHits =
            targetN
            + correctionIndex
            - 1;

        if (!DssNativeScanProgressRuntime.CanOfferCorrection(
                requiredNativeHits,
                correctionIndex,
                out _))
        {
            return
                DssSphericalAimTarget.Empty(
                    targetN);
        }

        double capAlpha =
            targetResolution.ActualCapAngularRadius;

        if (!double.IsFinite(capAlpha)
            || capAlpha <= 0d)
        {
            IReadOnlyList<SphericalPoint> basePoints =
                GenerateOptimalSphericalPoints(
                    targetN);

            capAlpha =
                DssSphericalCapCoverage
                    .SolveCapAngularRadiusForCoverage(
                        basePoints,
                        0.90d);
        }

        return ResolveCorrectionTarget(
            correctionIndex,
            sequentialStep,
            targetN,
            angularDiameterDegrees,
            capAlpha,
            coverageObservation,
            usedCoverageCandidates);
    }

    /// <summary>
    /// Generates the complete ordered spherical plan for target count N.
    /// </summary>
    public static IReadOnlyList<DssSphericalAimTarget>
        GenerateOrderedSphericalPlan(
            int targetN,
            double angularDiameterDegrees,
            DssModuleSnapshot dssModule,
            double bodyRadiusMeters)
    {
        double capAngularRadius =
            DssSphericalCapCoverage
                .CalculateCapAngularRadius(
                    dssModule,
                    bodyRadiusMeters,
                    targetN);

        return
            GenerateOrderedSphericalPlan(
                targetN,
                angularDiameterDegrees,
                dssModule,
                bodyRadiusMeters,
                capAngularRadius);
    }

    internal static IReadOnlyList<DssSphericalAimTarget>
        GenerateOrderedSphericalPlan(
            int targetN,
            double angularDiameterDegrees,
            DssModuleSnapshot dssModule,
            double bodyRadiusMeters,
            double capAngularRadius)
    {
        _ = dssModule;
        _ = bodyRadiusMeters;

        int clampedN =
            Math.Clamp(
                targetN,
                MinimumTargetCount,
                MaximumTargetCount);

        IReadOnlyList<SphericalPoint> surfacePoints =
            GetCoverageOptimizedBasePoints(
                clampedN,
                capAngularRadius);

        IReadOnlyList<SphericalPoint> orderedPoints =
            OptimizeShotOrdering(
                surfacePoints);

        IReadOnlyList<bool> useOuterRearBranch =
            GetBalancedRearBranchAssignments(
                clampedN,
                capAngularRadius,
                orderedPoints,
                angularDiameterDegrees);

        var plan =
            new List<DssSphericalAimTarget>(
                orderedPoints.Count);

        for (int i = 0;
             i < orderedPoints.Count;
             i++)
        {
            SphericalPoint p =
                orderedPoints[i];

            (double nx, double ny, double k) =
                DssSphericalProjection
                    .ProjectSphericalToScreenAim(
                        p,
                        angularDiameterDegrees,
                        useOuterRearBranch[i]);

            DssAimZone zone =
                k > 1.0d
                    ? DssAimZone.FarSide
                    : (k >= 0.80d
                        ? DssAimZone.Limb
                        : DssAimZone.Disc);

            string role =
                p.Theta >= 2.35d
                    ? "BATCH_FAR_DEEP"
                    : (p.Theta
                       > Math.PI / 2d
                        ? "BATCH_FAR"
                        : (p.Theta < 0.15d
                            ? "BATCH_CENTER"
                            : "BATCH_NEAR"));

            plan.Add(
                new DssSphericalAimTarget(
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
    /// Generates unit-sphere impact coordinates for target count N.
    /// </summary>
    public static IReadOnlyList<SphericalPoint>
        GenerateOptimalSphericalPoints(
            int n)
    {
        n =
            Math.Clamp(
                n,
                MinimumTargetCount,
                MaximumTargetCount);

        return n switch
        {
            2 => new[]
            {
                new SphericalPoint(
                    Math.PI,
                    0),
                new SphericalPoint(
                    0,
                    0)
            },

            3 => new[]
            {
                new SphericalPoint(
                    2.35d,
                    Math.PI / 2d),
                new SphericalPoint(
                    2.35d,
                    -Math.PI / 2d),
                new SphericalPoint(
                    0,
                    0)
            },

            4 =>
                PolyhedralValidationCatalog
                    .GetTetrahedron(),

            5 => new[]
            {
                new SphericalPoint(
                    Math.PI * 0.95d,
                    0),
                new SphericalPoint(
                    1.85d,
                    0),
                new SphericalPoint(
                    1.85d,
                    2d * Math.PI / 3d),
                new SphericalPoint(
                    1.85d,
                    -2d * Math.PI / 3d),
                new SphericalPoint(
                    0,
                    0)
            },

            6 =>
                PolyhedralValidationCatalog
                    .GetOctahedron(),

            7 => new[]
            {
                new SphericalPoint(
                    Math.PI * 0.95d,
                    Math.PI / 4d),
                new SphericalPoint(
                    Math.PI * 0.95d,
                    -3d * Math.PI / 4d),
                new SphericalPoint(
                    1.45d,
                    0),
                new SphericalPoint(
                    1.45d,
                    Math.PI / 2d),
                new SphericalPoint(
                    1.45d,
                    Math.PI),
                new SphericalPoint(
                    1.45d,
                    -Math.PI / 2d),
                new SphericalPoint(
                    0,
                    0)
            },

            8 =>
                PolyhedralValidationCatalog
                    .GetCube(),

            12 =>
                PolyhedralValidationCatalog
                    .GetIcosahedron(),

            _ =>
                GenerateFibonacciZonalPoints(
                    n)
        };
    }

    /// <summary>
    /// Generates a balanced zonal Fibonacci distribution on S^2 for arbitrary
    /// N not covered by an exact polyhedral catalog entry.
    /// </summary>
    public static IReadOnlyList<SphericalPoint>
        GenerateFibonacciZonalPoints(
            int n)
    {
        var points =
            new List<SphericalPoint>(
                n);

        double goldenAngle =
            Math.PI
            * (3d - Math.Sqrt(5d));

        for (int i = 0;
             i < n;
             i++)
        {
            double z =
                1d
                - (2d * i + 1d)
                  / n;

            double theta =
                Math.Acos(
                    Math.Clamp(
                        z,
                        -1d,
                        1d));

            double phi =
                i * goldenAngle;

            points.Add(
                new SphericalPoint(
                    theta,
                    phi));
        }

        return points;
    }

    /// <summary>
    /// Strict far-to-near surface ordering.
    ///
    /// Theta is the spherical distance from the visible/front pole:
    /// 0 = exact front centre, pi/2 = horizon, pi = rear antipode.
    ///
    /// Earlier versions only grouped points into FAR_DEEP/FAR/NEAR and then
    /// sorted each group by azimuth. That allowed a shallower rear probe to be
    /// launched before a deeper one. v57 orders the complete base batch by
    /// descending Theta, so the longest/deepest trajectories are dispatched
    /// first and can fly while the commander fires the shorter probes.
    /// </summary>
    public static IReadOnlyList<SphericalPoint>
        OptimizeShotOrdering(
            IReadOnlyList<SphericalPoint> points)
    {
        if (points.Count <= 1)
        {
            return points;
        }

        return
            points
                .OrderByDescending(
                    p => p.Theta)
                .ThenBy(
                    p => p.Phi)
                .ToList();
    }

    /// <summary>
    /// Selects explicit rear trajectory branches for a whole ordered batch.
    ///
    /// The surface optimizer operates on S^2, where the two projected rear
    /// trajectories were assumed to represent the same nominal surface point.
    /// Live N21 runs show that using outer for almost every eligible rear point
    /// creates a real coverage hole around K~1.15..1.30. Branch selection is
    /// therefore solved as a separate screen-trajectory dispersion problem.
    ///
    /// Constraints:
    /// - front points and unsafe outer points always use inner;
    /// - when at least two safe dual-branch rear points exist, both families
    ///   must be represented;
    /// - the number of outer branches is kept close to half of safe candidates;
    /// - among assignments with that count, minimize the largest radial gap,
    ///   then the sum of squared radial gaps, then maximize minimum 2D aim
    ///   spacing.
    ///
    /// The radial score includes K=1 and safe native MISS as boundaries, which
    /// directly penalizes the v52 discontinuity without hard-coding N17 or any
    /// specific body.
    /// </summary>
    internal static IReadOnlyList<bool>
        SelectBalancedRearBranches(
            IReadOnlyList<SphericalPoint> orderedPoints,
            double angularDiameterDegrees)
    {
        var result =
            new bool[
                orderedPoints.Count];

        if (orderedPoints.Count == 0)
        {
            return result;
        }

        var dualIndices =
            new List<int>();

        for (int i = 0;
             i < orderedPoints.Count;
             i++)
        {
            SphericalPoint point =
                orderedPoints[i];

            if (point.Theta
                    <= Math.PI / 2d
                || !DssSphericalProjection
                    .ShouldUseOuterFarBranch(
                        point.Theta,
                        angularDiameterDegrees))
            {
                continue;
            }

            dualIndices.Add(i);
        }

        if (dualIndices.Count == 0)
        {
            return result;
        }

        // Preserve v52 behavior when there is only one meaningful dual-branch
        // point. With two or more, force representation of both trajectory
        // families and optimize the assignment globally.
        if (dualIndices.Count == 1)
        {
            result[
                dualIndices[0]] =
                true;

            return result;
        }

        int desiredOuterCount =
            Math.Clamp(
                (dualIndices.Count + 1) / 2,
                1,
                dualIndices.Count - 1);

        // Start all-inner, then greedily add exactly desiredOuterCount outer
        // assignments using the whole-set dispersion score.
        for (int outer = 0;
             outer < desiredOuterCount;
             outer++)
        {
            int bestIndex = -1;
            RearBranchDispersionScore bestScore =
                RearBranchDispersionScore.Worst;

            foreach (int candidateIndex
                     in dualIndices)
            {
                if (result[
                        candidateIndex])
                {
                    continue;
                }

                result[
                    candidateIndex] =
                    true;

                RearBranchDispersionScore score =
                    EvaluateRearBranchDispersion(
                        orderedPoints,
                        result,
                        angularDiameterDegrees);

                result[
                    candidateIndex] =
                    false;

                if (bestIndex < 0
                    || IsBetterRearBranchScore(
                        score,
                        bestScore))
                {
                    bestIndex =
                        candidateIndex;

                    bestScore =
                        score;
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            result[
                bestIndex] =
                true;
        }

        // Pairwise swap refinement preserves branch count while escaping
        // greedy-order artifacts. Rear count is <= 32, so this is cheap and is
        // cached by (N, cap radius, angular-diameter bucket) on the live path.
        bool improved = true;

        while (improved)
        {
            improved = false;

            RearBranchDispersionScore currentScore =
                EvaluateRearBranchDispersion(
                    orderedPoints,
                    result,
                    angularDiameterDegrees);

            int bestOuter = -1;
            int bestInner = -1;
            RearBranchDispersionScore bestSwapScore =
                currentScore;

            foreach (int outerIndex
                     in dualIndices)
            {
                if (!result[
                        outerIndex])
                {
                    continue;
                }

                foreach (int innerIndex
                         in dualIndices)
                {
                    if (result[
                            innerIndex])
                    {
                        continue;
                    }

                    result[
                        outerIndex] =
                        false;

                    result[
                        innerIndex] =
                        true;

                    RearBranchDispersionScore score =
                        EvaluateRearBranchDispersion(
                            orderedPoints,
                            result,
                            angularDiameterDegrees);

                    result[
                        innerIndex] =
                        false;

                    result[
                        outerIndex] =
                        true;

                    if (IsBetterRearBranchScore(
                            score,
                            bestSwapScore))
                    {
                        bestOuter =
                            outerIndex;

                        bestInner =
                            innerIndex;

                        bestSwapScore =
                            score;
                    }
                }
            }

            if (bestOuter >= 0
                && bestInner >= 0)
            {
                result[
                    bestOuter] =
                    false;

                result[
                    bestInner] =
                    true;

                improved =
                    true;
            }
        }

        return result;
    }

    private static IReadOnlyList<bool>
        GetBalancedRearBranchAssignments(
            int targetN,
            double capAngularRadius,
            IReadOnlyList<SphericalPoint> orderedPoints,
            double angularDiameterDegrees)
    {
        double diameterBucket =
            Math.Round(
                angularDiameterDegrees * 4d)
            / 4d;

        string key =
            targetN.ToString(
                CultureInfo.InvariantCulture)
            + "|"
            + Math.Round(
                    capAngularRadius,
                    6)
                .ToString(
                    "R",
                    CultureInfo.InvariantCulture)
            + "|"
            + diameterBucket.ToString(
                "0.00",
                CultureInfo.InvariantCulture);

        lock (RearBranchPlanCacheGate)
        {
            if (RearBranchPlanCache.TryGetValue(
                    key,
                    out IReadOnlyList<bool>? cached))
            {
                return cached;
            }
        }

        IReadOnlyList<bool> generated =
            SelectBalancedRearBranches(
                orderedPoints,
                diameterBucket);

        int rearCount = 0;
        int outerCount = 0;
        double minimumInnerK =
            double.PositiveInfinity;
        double maximumInnerK =
            double.NegativeInfinity;
        double minimumOuterK =
            double.PositiveInfinity;
        double maximumOuterK =
            double.NegativeInfinity;

        for (int i = 0;
             i < orderedPoints.Count;
             i++)
        {
            SphericalPoint point =
                orderedPoints[i];

            if (point.Theta
                <= Math.PI / 2d)
            {
                continue;
            }

            rearCount++;

            (_, _, double k) =
                DssSphericalProjection
                    .ProjectSphericalToScreenAim(
                        point,
                        diameterBucket,
                        generated[i]);

            if (generated[i])
            {
                outerCount++;

                minimumOuterK =
                    Math.Min(
                        minimumOuterK,
                        k);

                maximumOuterK =
                    Math.Max(
                        maximumOuterK,
                        k);
            }
            else
            {
                minimumInnerK =
                    Math.Min(
                        minimumInnerK,
                        k);

                maximumInnerK =
                    Math.Max(
                        maximumInnerK,
                        k);
            }
        }

        lock (RearBranchPlanCacheGate)
        {
            RearBranchPlanCache[
                key] =
                generated;
        }

        Logger.Logger.Info(
            $"DSS PLAN rear branches: N={targetN}; " +
            $"rear={rearCount}; outer={outerCount}; inner={rearCount - outerCount}; " +
            $"innerK={FormatRange(minimumInnerK, maximumInnerK)}; " +
            $"outerK={FormatRange(minimumOuterK, maximumOuterK)}; " +
            $"diam={diameterBucket:0.00}deg.");

        return generated;
    }

    private static RearBranchDispersionScore
        EvaluateRearBranchDispersion(
            IReadOnlyList<SphericalPoint> orderedPoints,
            IReadOnlyList<bool> useOuterRearBranch,
            double angularDiameterDegrees)
    {
        var radii =
            new List<double>();

        var projected =
            new List<(
                double X,
                double Y)>();

        for (int i = 0;
             i < orderedPoints.Count;
             i++)
        {
            SphericalPoint point =
                orderedPoints[i];

            if (point.Theta
                <= Math.PI / 2d)
            {
                continue;
            }

            (double x, double y, double k) =
                DssSphericalProjection
                    .ProjectSphericalToScreenAim(
                        point,
                        angularDiameterDegrees,
                        useOuterRearBranch[i]);

            radii.Add(k);

            projected.Add(
                (x, y));
        }

        if (radii.Count == 0)
        {
            return
                new RearBranchDispersionScore(
                    0d,
                    0d,
                    Math.PI);
        }

        radii.Sort();

        double safeK =
            DssSphericalProjection
                .EstimateSafeNormalizedRadius(
                    angularDiameterDegrees);

        double previous =
            1d;

        double maximumGap =
            0d;

        double squaredGapSum =
            0d;

        for (int i = 0;
             i < radii.Count;
             i++)
        {
            double gap =
                Math.Max(
                    0d,
                    radii[i]
                    - previous);

            maximumGap =
                Math.Max(
                    maximumGap,
                    gap);

            squaredGapSum +=
                gap * gap;

            previous =
                radii[i];
        }

        double tailGap =
            Math.Max(
                0d,
                safeK - previous);

        maximumGap =
            Math.Max(
                maximumGap,
                tailGap);

        squaredGapSum +=
            tailGap
            * tailGap;

        double minimumScreenSpacing =
            double.PositiveInfinity;

        for (int i = 0;
             i < projected.Count;
             i++)
        {
            for (int j = i + 1;
                 j < projected.Count;
                 j++)
            {
                double dx =
                    projected[i].X
                    - projected[j].X;

                double dy =
                    projected[i].Y
                    - projected[j].Y;

                double distance =
                    Math.Sqrt(
                        dx * dx
                        + dy * dy);

                minimumScreenSpacing =
                    Math.Min(
                        minimumScreenSpacing,
                        distance);
            }
        }

        if (!double.IsFinite(
                minimumScreenSpacing))
        {
            minimumScreenSpacing =
                Math.PI;
        }

        return
            new RearBranchDispersionScore(
                maximumGap,
                squaredGapSum,
                minimumScreenSpacing);
    }

    private static bool IsBetterRearBranchScore(
        RearBranchDispersionScore candidate,
        RearBranchDispersionScore current)
    {
        if (candidate.MaximumRadialGap
            < current.MaximumRadialGap
              - RearBranchScoreEpsilon)
        {
            return true;
        }

        if (candidate.MaximumRadialGap
            > current.MaximumRadialGap
              + RearBranchScoreEpsilon)
        {
            return false;
        }

        if (candidate.RadialGapSquaredSum
            < current.RadialGapSquaredSum
              - RearBranchScoreEpsilon)
        {
            return true;
        }

        if (candidate.RadialGapSquaredSum
            > current.RadialGapSquaredSum
              + RearBranchScoreEpsilon)
        {
            return false;
        }

        return
            candidate.MinimumScreenSpacing
            > current.MinimumScreenSpacing
              + RearBranchScoreEpsilon;
    }

    private static string FormatRange(
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(minimum)
            || !double.IsFinite(maximum))
        {
            return "-";
        }

        return
            minimum.ToString(
                "0.000",
                CultureInfo.InvariantCulture)
            + ".."
            + maximum.ToString(
                "0.000",
                CultureInfo.InvariantCulture);
    }

    private readonly record struct RearBranchDispersionScore(
        double MaximumRadialGap,
        double RadialGapSquaredSum,
        double MinimumScreenSpacing)
    {
        public static RearBranchDispersionScore Worst { get; } =
            new(
                double.PositiveInfinity,
                double.PositiveInfinity,
                double.NegativeInfinity);
    }



    internal static IReadOnlyList<SphericalPoint>
        GetCoverageOptimizedBasePoints(
            int targetN,
            double capAngularRadius)
    {
        int clampedN =
            Math.Clamp(
                targetN,
                MinimumTargetCount,
                MaximumTargetCount);

        if (!double.IsFinite(
                capAngularRadius)
            || capAngularRadius <= 0d)
        {
            return
                GenerateOptimalSphericalPoints(
                    clampedN);
        }

        string key =
            clampedN.ToString(
                CultureInfo.InvariantCulture)
            + "|"
            + Math.Round(
                    capAngularRadius,
                    6)
                .ToString(
                    "R",
                    CultureInfo.InvariantCulture);

        lock (BasePlanCacheGate)
        {
            if (BasePlanCache.TryGetValue(
                    key,
                    out IReadOnlyList<SphericalPoint>? cached))
            {
                return cached;
            }
        }

        IReadOnlyList<SphericalPoint> generated =
            DssSphericalCapCoverage
                .GenerateCoverageOptimizedLayout(
                    clampedN,
                    capAngularRadius);

        IReadOnlyList<SphericalPoint> legacy =
            GenerateOptimalSphericalPoints(
                clampedN);

        double optimizedCoverage =
            DssSphericalCapCoverage
                .EvaluateUnionCoverage(
                    generated,
                    capAngularRadius);

        double legacyCoverage =
            DssSphericalCapCoverage
                .EvaluateUnionCoverage(
                    legacy,
                    capAngularRadius);

        lock (BasePlanCacheGate)
        {
            BasePlanCache[
                key] =
                generated;
        }

        Logger.Logger.Info(
            $"DSS PLAN base optimized: N={clampedN}; " +
            $"alpha={capAngularRadius * 180d / Math.PI:0.00}deg; " +
            $"legacy={legacyCoverage:0.000}; " +
            $"optimized={optimizedCoverage:0.000}; " +
            $"gain={(optimizedCoverage - legacyCoverage) * 100d:+0.0;-0.0;0.0}pp.");

        return generated;
    }

    private static DssSphericalAimTarget ResolveCorrectionTarget(
        int correctionIndex,
        int sequentialStep,
        int targetN,
        double angularDiameterDegrees,
        double capAngularRadius,
        DssCoverageObservation? coverageObservation,
        long usedCoverageCandidates)
    {
        IReadOnlyList<SphericalPoint> basePoints =
            GetCoverageOptimizedBasePoints(
                targetN,
                capAngularRadius);

        IReadOnlyList<DssCoverageCorrectionPoint> corrections =
            GetCorrectionPlan(
                targetN,
                basePoints,
                capAngularRadius);

        int correctionPlanIndex =
            correctionIndex - 1;

        if (correctionPlanIndex < 0
            || correctionPlanIndex
               >= corrections.Count)
        {
            return
                DssSphericalAimTarget.Empty(
                    targetN);
        }

        DssCoverageCorrectionPoint modelTarget =
            corrections[
                correctionPlanIndex];

        DssCoverageObservation coverage =
            coverageObservation
            ?? DssCoverageObservation.Empty;

        if (TryResolveCoverageOverride(
                coverage,
                usedCoverageCandidates,
                angularDiameterDegrees,
                capAngularRadius,
                basePoints,
                corrections,
                correctionPlanIndex,
                modelTarget,
                out SphericalPoint coveragePoint,
                out double coverageGain))
        {
            return BuildAimTarget(
                sequentialStep,
                targetN,
                angularDiameterDegrees,
                coveragePoint,
                "CORRECTION_COVERAGE",
                coverage.SuggestedCandidateId,
                coverageGain);
        }

        string role =
            modelTarget.Point.Theta
                > Math.PI / 2d
                ? "CORRECTION_MODEL_REAR"
                : "CORRECTION_MODEL_NEAR";

        bool? correctionOuterBranch =
            modelTarget.Point.Theta
                > Math.PI / 2d
                ? ShouldUseOuterCorrectionBranch(
                    correctionIndex,
                    modelTarget.Point,
                    angularDiameterDegrees)
                : null;

        return BuildAimTarget(
            sequentialStep,
            targetN,
            angularDiameterDegrees,
            modelTarget.Point,
            role,
            0,
            modelTarget.IncrementalCoverage,
            correctionOuterBranch);
    }

    /// <summary>
    /// Correction tail alternates rear trajectory families, starting with
    /// inner. Both recent N21 runs showed the first automatic outer correction
    /// landing in an already well-covered region while manual inner-annulus
    /// shots produced the useful gain.
    /// </summary>
    internal static bool ShouldUseOuterCorrectionBranch(
        int correctionIndex,
        SphericalPoint point,
        double angularDiameterDegrees) =>
        correctionIndex > 0
        && correctionIndex % 2 == 0
        && DssSphericalProjection
            .ShouldUseOuterFarBranch(
                point.Theta,
                angularDiameterDegrees);

    private static bool TryResolveCoverageOverride(
        DssCoverageObservation coverage,
        long usedCoverageCandidates,
        double angularDiameterDegrees,
        double capAngularRadius,
        IReadOnlyList<SphericalPoint> basePoints,
        IReadOnlyList<DssCoverageCorrectionPoint> corrections,
        int correctionPlanIndex,
        DssCoverageCorrectionPoint modelTarget,
        out SphericalPoint coveragePoint,
        out double coverageGain)
    {
        coveragePoint =
            new SphericalPoint(
                0d,
                0d);

        coverageGain = 0d;

        if (!coverage.Available
            || coverage.Settling
            || coverage.Confidence
               < MinimumCoverageFeedbackConfidence
            || coverage.SuggestedCandidateId <= 0
            || coverage.SuggestedUncoveredScore
               < MinimumCoverageFeedbackUncoveredScore
            || DssProbeAimSolver.IsCoverageCandidateUsed(
                usedCoverageCandidates,
                coverage.SuggestedCandidateId))
        {
            return false;
        }

        SphericalPoint observed =
            DssSphericalProjection
                .ProjectScreenAimToSpherical(
                    coverage.SuggestedNormalizedX,
                    coverage.SuggestedNormalizedY,
                    angularDiameterDegrees);

        bool modelRear =
            modelTarget.Point.Theta
            > Math.PI / 2d;

        bool observedRear =
            observed.Theta
            > Math.PI / 2d;

        if (modelRear != observedRear)
        {
            return false;
        }

        double angularDistance =
            modelTarget.Point
                .AngularDistanceTo(
                    observed);

        if (angularDistance
            > MaximumCoverageOverrideAngularDistance)
        {
            return false;
        }

        var occupied =
            new List<SphericalPoint>(
                basePoints.Count
                + correctionPlanIndex);

        occupied.AddRange(
            basePoints);

        for (int i = 0;
             i < correctionPlanIndex;
             i++)
        {
            occupied.Add(
                corrections[i].Point);
        }

        double observedGain =
            DssSphericalCapCoverage
                .EvaluateIncrementalCoverage(
                    occupied,
                    observed,
                    capAngularRadius);

        double minimumGain =
            Math.Max(
                MinimumCoverageOverrideAbsoluteGain,
                modelTarget.IncrementalCoverage
                * MinimumCoverageOverrideGainRatio);

        if (observedGain
            < minimumGain)
        {
            return false;
        }

        coveragePoint =
            observed;

        coverageGain =
            observedGain;

        return true;
    }

    private static DssSphericalAimTarget BuildAimTarget(
        int sequentialStep,
        int targetN,
        double angularDiameterDegrees,
        SphericalPoint point,
        string role,
        int candidateId,
        double coverageScore,
        bool? useOuterRearBranch = null)
    {
        (double nx, double ny, double k) =
            useOuterRearBranch.HasValue
                ? DssSphericalProjection
                    .ProjectSphericalToScreenAim(
                        point,
                        angularDiameterDegrees,
                        useOuterRearBranch.Value)
                : DssSphericalProjection
                    .ProjectSphericalToScreenAim(
                        point,
                        angularDiameterDegrees);

        double safeK =
            DssSphericalProjection
                .EstimateSafeNormalizedRadius(
                    angularDiameterDegrees);

        if (!double.IsFinite(k)
            || k < 0d
            || k > safeK + 1e-6d)
        {
            return
                DssSphericalAimTarget.Empty(
                    targetN);
        }

        DssAimZone zone =
            k > 1.0d
                ? DssAimZone.FarSide
                : (k >= 0.80d
                    ? DssAimZone.Limb
                    : DssAimZone.Disc);

        return
            new DssSphericalAimTarget(
                true,
                sequentialStep,
                point,
                nx,
                ny,
                k,
                zone,
                role,
                targetN,
                candidateId,
                coverageScore);
    }

    private static IReadOnlyList<DssCoverageCorrectionPoint>
        GetCorrectionPlan(
            int targetN,
            IReadOnlyList<SphericalPoint> basePoints,
            double capAngularRadius)
    {
        string key =
            targetN.ToString(
                CultureInfo.InvariantCulture)
            + "|"
            + Math.Round(
                    capAngularRadius,
                    6)
                .ToString(
                    "R",
                    CultureInfo.InvariantCulture);

        lock (CorrectionPlanCacheGate)
        {
            if (CorrectionPlanCache.TryGetValue(
                    key,
                    out IReadOnlyList<DssCoverageCorrectionPoint>? cached))
            {
                return cached;
            }
        }

        IReadOnlyList<DssCoverageCorrectionPoint> generated =
            DssSphericalCapCoverage
                .GenerateGreedyCorrectionPlan(
                    basePoints,
                    capAngularRadius,
                    MaximumCorrectionShots);

        lock (CorrectionPlanCacheGate)
        {
            CorrectionPlanCache[key] =
                generated;
        }

        return generated;
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
        DssPredictiveAimTarget empirical =
            DssPredictiveBatchPlanner.Resolve(
                sequentialStep,
                requestedTarget,
                targetSource,
                angularDiameterDegrees,
                confirmedImpactCount,
                coverageObservation,
                usedCoverageCandidates);

        if (!empirical.Available)
        {
            return
                DssSphericalAimTarget.Empty(
                    empirical.PredictedBatchCount);
        }

        SphericalPoint p =
            DssSphericalProjection
                .ProjectScreenAimToSpherical(
                    empirical.NormalizedX,
                    empirical.NormalizedY,
                    angularDiameterDegrees);

        double k =
            Math.Sqrt(
                empirical.NormalizedX
                * empirical.NormalizedX
                + empirical.NormalizedY
                  * empirical.NormalizedY);

        return
            new DssSphericalAimTarget(
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

    public static int ResolveTargetCount(
        int requestedTarget,
        string targetSource)
    {
        if (targetSource.Equals(
                "BODY",
                StringComparison.OrdinalIgnoreCase)
            || targetSource.Equals(
                "HUD_CV",
                StringComparison.OrdinalIgnoreCase))
        {
            return
                Math.Clamp(
                    requestedTarget,
                    MinimumTargetCount,
                    MaximumTargetCount);
        }

        return
            DssPredictiveBatchPlanner
                .ResolvePredictedBatchCount(
                    requestedTarget,
                    targetSource);
    }
}
