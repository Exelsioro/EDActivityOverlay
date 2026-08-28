using System;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssPredictiveAimTarget(
    bool Available,
    double NormalizedX,
    double NormalizedY,
    DssAimZone Zone,
    int CandidateId,
    double CoverageScore,
    string Role,
    int PredictedBatchCount,
    int WaveEnd)
{
    public static DssPredictiveAimTarget Empty(
        int predictedBatchCount,
        int waveEnd) =>
        new(
            false,
            0,
            0,
            DssAimZone.Disc,
            0,
            0,
            string.Empty,
            predictedBatchCount,
            waveEnd);
}

/// <summary>
/// Predictive DSS batch planner.
///
/// User-validated invariant:
///   r/Rh > 1.0 (past the visible horizon/limb) lands on the far hemisphere.
///
/// v30 therefore plans in two explicit classes instead of trying to fill only
/// the visible 2D disc:
///   - near-side / centre points (r <= 1),
///   - far-side points (r > 1), always below the calibrated MISS boundary.
///
/// If Elite's efficiency target is known from body data, the complete batch is
/// immediately available. If only the generic SETTINGS fallback is known, v30
/// exposes a six-shot first wave, waits for that wave's impacts/ScanComplete,
/// then releases the remaining predicted batch. That avoids both 14 s waits
/// between every shot and blindly dumping 12+ probes into a two-probe moon.
/// </summary>
internal static class DssPredictiveBatchPlanner
{
    internal const int MinimumBatchCount = 2;
    internal const int MaximumBatchCount = 18;
    internal const int UnknownTargetFirstWaveCount = 6;
    internal const int MaximumCorrectionShots = 8;

    internal static int ResolvePredictedBatchCount(
        int requestedTarget,
        string targetSource)
    {
        int clamped =
            Math.Clamp(
                requestedTarget,
                MinimumBatchCount,
                MaximumBatchCount);

        if (targetSource.Equals(
                "BODY",
                StringComparison.OrdinalIgnoreCase))
        {
            return clamped;
        }

        // Current settings UI historically capped the fallback at 12, while a
        // live v29 body visibly showed an Elite target of 13. Until native HUD
        // target digits are read directly, keep one conservative slot of headroom.
        return Math.Clamp(
            clamped + 1,
            MinimumBatchCount,
            MaximumBatchCount);
    }

    internal static int ResolveWaveEnd(
        int predictedBatchCount,
        string targetSource,
        int confirmedImpactCount)
    {
        if (targetSource.Equals(
                "BODY",
                StringComparison.OrdinalIgnoreCase))
        {
            return predictedBatchCount;
        }

        int firstWave =
            Math.Min(
                UnknownTargetFirstWaveCount,
                predictedBatchCount);

        if (confirmedImpactCount < firstWave)
        {
            return firstWave;
        }

        // Unknown native efficiency target: after the six-shot predictive wave,
        // release only one correction at a time. This prevents #8 from
        // appearing while #7 is still in flight on a native 7-probe body.
        return Math.Min(
            predictedBatchCount,
            confirmedImpactCount + 1);
    }

    internal static DssPredictiveAimTarget Resolve(
        int sequentialStep,
        int requestedTarget,
        string targetSource,
        double angularDiameterDegrees,
        int confirmedImpactCount,
        DssCoverageObservation? coverageObservation,
        long usedCoverageCandidates)
    {
        int predicted =
            ResolvePredictedBatchCount(
                requestedTarget,
                targetSource);

        int waveEnd =
            ResolveWaveEnd(
                predicted,
                targetSource,
                confirmedImpactCount);

        if (sequentialStep < 1)
        {
            return DssPredictiveAimTarget.Empty(
                predicted,
                waveEnd);
        }

        int firstWave =
            Math.Min(
                UnknownTargetFirstWaveCount,
                predicted);

        bool nativeTargetKnown =
            targetSource.Equals(
                "BODY",
                StringComparison.OrdinalIgnoreCase);

        if (sequentialStep <= predicted)
        {
            if (sequentialStep > waveEnd)
            {
                return DssPredictiveAimTarget.Empty(
                    predicted,
                    waveEnd);
            }

            if (!nativeTargetKnown
                && sequentialStep > firstWave)
            {
                return ResolveUnknownTargetCorrection(
                    sequentialStep - firstWave,
                    sequentialStep,
                    predicted,
                    waveEnd,
                    angularDiameterDegrees,
                    coverageObservation,
                    usedCoverageCandidates);
            }

            return ResolveBaseBatchPoint(
                sequentialStep,
                predicted,
                waveEnd,
                angularDiameterDegrees);
        }

        int correctionIndex =
            sequentialStep
            - predicted;

        if (correctionIndex < 1
            || correctionIndex > MaximumCorrectionShots
            || confirmedImpactCount < predicted)
        {
            return DssPredictiveAimTarget.Empty(
                predicted,
                waveEnd);
        }

        DssCoverageObservation coverage =
            coverageObservation
            ?? DssCoverageObservation.Empty;

        // Alternate far-side correction with visible-side coverage feedback.
        // Coverage CV is a correction sensor, not the spherical plan itself.
        if ((correctionIndex & 1) == 0
            && coverage.Available
            && coverage.SuggestedCandidateId > 0
            && coverage.SuggestedUncoveredScore >= 0.24d
            && !DssProbeAimSolver.IsCoverageCandidateUsed(
                usedCoverageCandidates,
                coverage.SuggestedCandidateId))
        {
            double radius =
                Math.Sqrt(
                    coverage.SuggestedNormalizedX
                    * coverage.SuggestedNormalizedX
                    + coverage.SuggestedNormalizedY
                      * coverage.SuggestedNormalizedY);

            return new DssPredictiveAimTarget(
                true,
                coverage.SuggestedNormalizedX,
                coverage.SuggestedNormalizedY,
                radius >= 0.60d
                    ? DssAimZone.Limb
                    : DssAimZone.Disc,
                coverage.SuggestedCandidateId,
                coverage.SuggestedUncoveredScore,
                "CORRECTION_COVERAGE",
                predicted,
                waveEnd);
        }

        return ResolveFarCorrection(
            correctionIndex,
            predicted,
            waveEnd,
            angularDiameterDegrees);
    }

    private static DssPredictiveAimTarget ResolveUnknownTargetCorrection(
        int correctionIndex,
        int sequentialStep,
        int predicted,
        int waveEnd,
        double angularDiameterDegrees,
        DssCoverageObservation? coverageObservation,
        long usedCoverageCandidates)
    {
        DssCoverageObservation coverage =
            coverageObservation
            ?? DssCoverageObservation.Empty;

        if (coverage.Settling)
        {
            return DssPredictiveAimTarget.Empty(
                predicted,
                waveEnd);
        }

        if (coverage.Available
            && coverage.SuggestedCandidateId > 0
            && coverage.SuggestedUncoveredScore >= 0.24d
            && !DssProbeAimSolver.IsCoverageCandidateUsed(
                usedCoverageCandidates,
                coverage.SuggestedCandidateId))
        {
            double x =
                coverage.SuggestedNormalizedX;

            double y =
                coverage.SuggestedNormalizedY;

            double radius =
                Math.Sqrt(
                    x * x
                    + y * y);

            // The v31 7/7 run is a clean calibration point for the correction
            // tail: the coarse observer pointed to (0,+0.68), while the
            // successful manual final shot landed near r=0.97 in the same
            // direction. Use coverage to choose the direction, but place a
            // non-central visible-side correction near 0.92 Rh so it reaches
            // farther into the uncovered cap instead of repeating v29's
            // 0.68-Rh ring.
            if (radius >= 0.18d)
            {
                const double correctionRadius = 0.92d;

                double scale =
                    correctionRadius
                    / radius;

                x *= scale;
                y *= scale;
                radius =
                    correctionRadius;
            }

            return new DssPredictiveAimTarget(
                true,
                x,
                y,
                radius >= 0.78d
                    ? DssAimZone.Limb
                    : DssAimZone.Disc,
                coverage.SuggestedCandidateId,
                coverage.SuggestedUncoveredScore,
                "CORRECTION_COVERAGE_NEAR",
                predicted,
                waveEnd);
        }

        // If the visible coverage classifier is unavailable, do not fabricate
        // another near-side ring point. Keep the spherical fallback balanced
        // by alternating conservative far-side corrections.
        return ResolveFarCorrection(
            correctionIndex,
            predicted,
            waveEnd,
            angularDiameterDegrees) with
        {
            Role = "CORRECTION_FAR_FALLBACK"
        };
    }

    private static DssPredictiveAimTarget ResolveBaseBatchPoint(
        int step,
        int predicted,
        int waveEnd,
        double angularDiameterDegrees)
    {
        double safe =
            DssProbeAimSolver
                .EstimateSafeNormalizedRadius(
                    angularDiameterDegrees);

        // Desired far radii are clamped below the empirically calibrated MISS
        // boundary. Any result > 1.0 is explicitly a far-hemisphere launch.
        double farOuter =
            ClampFarRadius(
                1.46d,
                safe);

        double farMiddle =
            ClampFarRadius(
                1.30d,
                safe);

        double farShallow =
            ClampFarRadius(
                1.12d,
                safe);

        return step switch
        {
            1 => Point(
                1,
                0,
                -safe,
                DssAimZone.FarSide,
                "BATCH_FAR_DEEP",
                predicted,
                waveEnd),

            2 => Point(
                2,
                0,
                -0.90d,
                DssAimZone.Limb,
                "BATCH_NEAR",
                predicted,
                waveEnd),

            // Centre is a reserved strategic point. Coverage ranking can no
            // longer push it out of the plan as happened in v29.
            3 => Point(
                3,
                0,
                0,
                DssAimZone.Disc,
                "BATCH_CENTER",
                predicted,
                waveEnd),

            4 => PointPolar(
                4,
                farOuter,
                90,
                DssAimZone.FarSide,
                "BATCH_FAR_DEEP",
                predicted,
                waveEnd),

            5 => PointPolar(
                5,
                0.78d,
                0,
                DssAimZone.Disc,
                "BATCH_NEAR",
                predicted,
                waveEnd),

            6 => PointPolar(
                6,
                farShallow,
                180,
                DssAimZone.FarSide,
                "BATCH_FAR_SHALLOW",
                predicted,
                waveEnd),

            7 => PointPolar(
                7,
                0.78d,
                180,
                DssAimZone.Disc,
                "BATCH_NEAR",
                predicted,
                waveEnd),

            8 => PointPolar(
                8,
                farShallow,
                0,
                DssAimZone.FarSide,
                "BATCH_FAR_SHALLOW",
                predicted,
                waveEnd),

            9 => PointPolar(
                9,
                0.78d,
                90,
                DssAimZone.Disc,
                "BATCH_NEAR",
                predicted,
                waveEnd),

            10 => PointPolar(
                10,
                farMiddle,
                -45,
                DssAimZone.FarSide,
                "BATCH_FAR_MID",
                predicted,
                waveEnd),

            11 => PointPolar(
                11,
                0.62d,
                135,
                DssAimZone.Disc,
                "BATCH_NEAR_INNER",
                predicted,
                waveEnd),

            12 => PointPolar(
                12,
                farMiddle,
                135,
                DssAimZone.FarSide,
                "BATCH_FAR_MID",
                predicted,
                waveEnd),

            13 => PointPolar(
                13,
                0.62d,
                45,
                DssAimZone.Disc,
                "BATCH_NEAR_INNER",
                predicted,
                waveEnd),

            14 => PointPolar(
                14,
                farMiddle,
                -135,
                DssAimZone.FarSide,
                "BATCH_FAR_MID",
                predicted,
                waveEnd),

            15 => PointPolar(
                15,
                0.62d,
                -135,
                DssAimZone.Disc,
                "BATCH_NEAR_INNER",
                predicted,
                waveEnd),

            16 => PointPolar(
                16,
                farMiddle,
                45,
                DssAimZone.FarSide,
                "BATCH_FAR_MID",
                predicted,
                waveEnd),

            17 => PointPolar(
                17,
                0.44d,
                90,
                DssAimZone.Disc,
                "BATCH_NEAR_INNER",
                predicted,
                waveEnd),

            18 => PointPolar(
                18,
                ClampFarRadius(1.18d, safe),
                90,
                DssAimZone.FarSide,
                "BATCH_FAR_SHALLOW",
                predicted,
                waveEnd),

            _ => DssPredictiveAimTarget.Empty(
                predicted,
                waveEnd)
        };
    }

    private static DssPredictiveAimTarget ResolveFarCorrection(
        int correctionIndex,
        int predicted,
        int waveEnd,
        double angularDiameterDegrees)
    {
        double safe =
            DssProbeAimSolver
                .EstimateSafeNormalizedRadius(
                    angularDiameterDegrees);

        double radius =
            ClampFarRadius(
                (correctionIndex & 2) == 0
                    ? 1.24d
                    : 1.44d,
                safe);

        double[] angles =
        {
            -30d,
            150d,
            30d,
            -150d,
            0d,
            180d,
            90d,
            -90d
        };

        double angle =
            angles[(correctionIndex - 1)
                   % angles.Length];

        return PointPolar(
            predicted + correctionIndex,
            radius,
            angle,
            DssAimZone.FarSide,
            "CORRECTION_FAR",
            predicted,
            waveEnd);
    }

    private static double ClampFarRadius(
        double desired,
        double safeBoundary)
    {
        double maximum =
            Math.Max(
                1.04d,
                safeBoundary - 0.08d);

        return Math.Clamp(
            desired,
            1.04d,
            maximum);
    }

    private static DssPredictiveAimTarget PointPolar(
        int step,
        double radius,
        double angleDegrees,
        DssAimZone zone,
        string role,
        int predicted,
        int waveEnd)
    {
        double radians =
            angleDegrees
            * Math.PI
            / 180d;

        return Point(
            step,
            Math.Cos(radians) * radius,
            Math.Sin(radians) * radius,
            zone,
            role,
            predicted,
            waveEnd);
    }

    private static DssPredictiveAimTarget Point(
        int step,
        double x,
        double y,
        DssAimZone zone,
        string role,
        int predicted,
        int waveEnd) =>
        new(
            true,
            x,
            y,
            zone,
            0,
            0,
            role,
            predicted,
            waveEnd);
}
