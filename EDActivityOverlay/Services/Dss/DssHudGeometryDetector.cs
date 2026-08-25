using System;
using System.Collections.Generic;
using System.Linq;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssHudGeometry(
    int ReticleX,
    int ReticleY,
    bool BodyCenterFound,
    double BodyCenterX,
    double BodyCenterY,
    double BodyCenterConfidence,
    bool HorizonMarkerFound,
    bool HorizonMarkerObserved,
    double HorizonMarkerX,
    double HorizonMarkerY,
    double HorizonMarkerConfidence,
    double HorizonObservationAgeMilliseconds,
    double HorizonRadiusPixels,
    double HorizonAimErrorPixels,
    double AimOffsetDegrees)
{
    public static DssHudGeometry Empty(int width, int height) =>
        new(
            width / 2,
            height / 2,
            false,
            0,
            0,
            0,
            false,
            false,
            0,
            0,
            0,
            -1,
            0,
            0,
            0);
}

internal sealed record DssDetectionHint(
    double PredictedCenterX,
    double PredictedCenterY,
    double CenterSearchRadiusPixels,
    double? ExpectedHorizonRadiusPixels);

/// <summary>
/// DSS geometry detector v8.
///
/// Previous versions searched the entire frame for a white blob and then tried
/// to prove that it was the body-centre marker. This is fragile on the Milky
/// Way because there are many white blobs.
///
/// v8 reverses the problem:
///
/// 1. The DSS reticle is fixed at the screen centre.
/// 2. Frontier draws a thin radial guide from the reticle toward the body
///    centre.
/// 3. Search rays that start at the known reticle.
/// 4. Only accept a body-centre marker if a strong continuous guide reaches it.
///
/// A random star may look like the marker, but it does not normally have a
/// continuous reticle-anchored guide path.
/// </summary>
internal sealed class DssHudGeometryDetector
{
    private const int GuideMinimumLuma = 50;
    private const int GuideMaximumSpread = 120;

    private const int MarkerMinimumLuma = 120;
    private const int MarkerMaximumSpread = 120;
    private const double MarkerCoreMinimumMeanLuma = 125d;

    // A real Frontier body-centre marker is a filled neutral-white disk.
    // Probe tails and bright limbs can satisfy the old 1-D cross tests, but
    // they do not fill a small 2-D disk in all four quadrants.
    private const int MarkerCoreShapeMinimumLuma = 110;
    private const int MarkerCoreShapeMaximumSpread = 80;

    private const int HorizonMinimumLuma = 135;
    private const int HorizonMaximumSpread = 60;

    private const int GlobalRayShortlist = 12;

    public DssHudGeometry DetectGlobal(
        DssCapturedFrame frame,
        double verticalFovDegrees,
        double? expectedHorizonRadiusPixels = null) =>
        DetectCore(
            frame,
            verticalFovDegrees,
            null,
            expectedHorizonRadiusPixels,
            requireExpectedHorizonValidation:
                expectedHorizonRadiusPixels is > 25);

    public DssHudGeometry DetectLocal(
        DssCapturedFrame frame,
        double verticalFovDegrees,
        DssDetectionHint hint) =>
        DetectCore(
            frame,
            verticalFovDegrees,
            hint,
            hint.ExpectedHorizonRadiusPixels,
            requireExpectedHorizonValidation: false);

    public DssHudGeometry Detect(
        DssCapturedFrame frame,
        double verticalFovDegrees) =>
        DetectGlobal(frame, verticalFovDegrees);

    internal static double GetFocalPixels(
        int frameHeight,
        double verticalFovDegrees)
    {
        double clamped =
            Math.Clamp(verticalFovDegrees, 20d, 120d);

        return (frameHeight / 2d)
               / Math.Tan(clamped * Math.PI / 360d);
    }

    private DssHudGeometry DetectCore(
        DssCapturedFrame frame,
        double verticalFovDegrees,
        DssDetectionHint? hint,
        double? expectedHorizonRadiusPixels,
        bool requireExpectedHorizonValidation)
    {
        int reticleX = frame.Width / 2;
        int reticleY = frame.Height / 2;

        // v9 deliberately removes the v8 "near reticle bright marker"
        // fallback. In the uploaded v8 session that fallback repeatedly
        // locked onto static central HUD text/graphics while the real body
        // centre was absent. The only accepted centre path is now:
        //
        // fixed reticle -> continuous Frontier radial guide -> marker.
        BodyCenterCandidate? center =
            FindBodyCenterFromGuideRay(
                frame,
                reticleX,
                reticleY,
                hint);

        HorizonCandidate? horizon = null;

        if (center is not null)
        {
            horizon =
                FindHorizonMarker(
                    frame,
                    center.X,
                    center.Y,
                    reticleX,
                    reticleY,
                    expectedHorizonRadiusPixels);
        }

        bool hasTrustedRadius =
            expectedHorizonRadiusPixels is > 25;

        // C -> H mutual validation.
        //
        // During normal LOCAL tracking Frontier is allowed to blink the
        // horizon dash, so an already-close centre may survive without H.
        //
        // During GLOBAL reacquisition, however, a Milky-Way ray is not enough:
        // with a trusted Rh the centre candidate must predict a real short
        // perpendicular horizon dash in the expected place.
        if (center is not null
            && hasTrustedRadius)
        {
            bool safeObservedHorizon =
                horizon is not null
                && IsSafeInitialHorizonPoint(
                    frame,
                    horizon.X,
                    horizon.Y);

            bool suspiciousLocalJump =
                hint is not null
                && Distance(
                    center.X,
                    center.Y,
                    hint.PredictedCenterX,
                    hint.PredictedCenterY) > 72;

            // Frontier intentionally blinks the horizon triplet. Requiring
            // H on every GLOBAL reacquisition makes a trusted circle vanish
            // even when the native centre marker + radial guide are still
            // clear. Keep H as the preferred validator, but permit an
            // exceptionally strong centre-only candidate. The tracker still
            // requires three mutually consistent reacquire observations.
            bool strongCenterOnly =
                center.Confidence >= 0.94;

            if ((requireExpectedHorizonValidation
                 || suspiciousLocalJump)
                && !safeObservedHorizon
                && !strongCenterOnly)
            {
                center = null;
                horizon = null;
            }
        }

        // H + trusted Rh -> C reconstruction.
        //
        // This is the important v10 fallback for a partly visible body:
        // the body-centre marker itself may be clipped/off-screen while the
        // radial guide and Frontier horizon dash are still clearly visible.
        //
        // In that state H is between the reticle and the body centre, so:
        //
        // C = H + normalize(H - A) * Rh
        //
        // where A is the fixed DSS reticle.
        if (center is null
            && hasTrustedRadius)
        {
            HorizonRecoveryCandidate? recovery =
                FindCenterFromTrustedHorizon(
                    frame,
                    reticleX,
                    reticleY,
                    expectedHorizonRadiusPixels!.Value,
                    hint);

            if (recovery is not null)
            {
                return BuildRecoveredGeometry(
                    frame,
                    verticalFovDegrees,
                    recovery,
                    expectedHorizonRadiusPixels.Value);
            }
        }

        if (center is null)
        {
            return DssHudGeometry.Empty(
                frame.Width,
                frame.Height);
        }

        double dx =
            center.X - reticleX;

        double dy =
            center.Y - reticleY;

        double focalPixels =
            GetFocalPixels(
                frame.Height,
                verticalFovDegrees);

        double aimOffsetDegrees =
            Math.Atan2(
                Math.Sqrt(dx * dx + dy * dy),
                focalPixels)
            * 180d / Math.PI;

        double horizonRadius =
            horizon is null
                ? 0
                : Distance(
                    center.X,
                    center.Y,
                    horizon.X,
                    horizon.Y);

        double horizonError =
            horizon is null
                ? 0
                : Distance(
                    center.X,
                    center.Y,
                    reticleX,
                    reticleY)
                  - horizonRadius;

        return new DssHudGeometry(
            reticleX,
            reticleY,
            true,
            center.X,
            center.Y,
            center.Confidence,
            horizon is not null,
            horizon is not null,
            horizon?.X ?? 0,
            horizon?.Y ?? 0,
            horizon?.Confidence ?? 0,
            horizon is null ? -1 : 0,
            horizonRadius,
            horizonError,
            aimOffsetDegrees);
    }

    private static BodyCenterCandidate?
        FindNearReticleMarker(
            DssCapturedFrame frame,
            int reticleX,
            int reticleY)
    {
        double scale =
            frame.Height / 1080d;

        int searchRadius =
            Math.Max(
                38,
                (int)Math.Round(58 * scale));

        MarkerAtPoint? best = null;

        for (int y = reticleY - searchRadius;
             y <= reticleY + searchRadius;
             y += 2)
        {
            for (int x = reticleX - searchRadius;
                 x <= reticleX + searchRadius;
                 x += 2)
            {
                if ((uint)x >= (uint)frame.Width
                    || (uint)y >= (uint)frame.Height)
                {
                    continue;
                }

                double distance =
                    Distance(
                        x,
                        y,
                        reticleX,
                        reticleY);

                if (distance > searchRadius)
                {
                    continue;
                }

                MarkerAtPoint marker =
                    MeasureAxisAlignedMarker(
                        frame,
                        x,
                        y,
                        scale);

                if (!marker.Valid)
                {
                    continue;
                }

                // The fixed reticle itself contains a small horizontal white
                // dash. A real body-centre marker is round in both axes.
                double score =
                    marker.Score
                    - distance * 0.08;

                if (best is null
                    || score > best.Score)
                {
                    best = marker with
                    {
                        Score = score
                    };
                }
            }
        }

        if (best is null)
        {
            return null;
        }

        (double refinedX, double refinedY) =
            RefineMarkerCentroid(
                frame,
                best.X,
                best.Y,
                scale);

        return new BodyCenterCandidate(
            refinedX,
            refinedY,
            Math.Clamp(
                0.72
                + best.Roundness * 0.18,
                0d,
                0.98));
    }

    private static BodyCenterCandidate?
        FindBodyCenterFromGuideRay(
            DssCapturedFrame frame,
            int reticleX,
            int reticleY,
            DssDetectionHint? hint)
    {
        List<GuideRayCandidate> rays =
            FindGuideRayShortlist(
                frame,
                reticleX,
                reticleY,
                hint);

        if (rays.Count == 0)
        {
            return null;
        }

        BodyCenterCandidate? bestCenter = null;
        double bestCombinedScore =
            double.NegativeInfinity;

        foreach (GuideRayCandidate ray in rays)
        {
            MarkerOnRayCandidate? marker =
                FindMarkerOnRay(
                    frame,
                    reticleX,
                    reticleY,
                    ray.AngleRadians,
                    hint);

            if (marker is null)
            {
                continue;
            }

            // The critical discriminator: the guide must be present over most
            // of the path from the fixed reticle to the candidate marker.
            // This rejected the false Milky-Way rays in the recorded v7 set,
            // while real DSS paths were typically ~0.9-1.0 support.
            if (marker.PathSupport < 0.62
                || marker.PathAverageContrast < 11)
            {
                continue;
            }

            double combinedScore =
                marker.PathSupport * 210d
                + marker.PathAverageContrast * 0.55
                + marker.MarkerScore * 1.15
                + ray.Score * 0.18;

            if (hint is not null)
            {
                double expectedDistance =
                    Distance(
                        hint.PredictedCenterX,
                        hint.PredictedCenterY,
                        reticleX,
                        reticleY);

                double actualDistance =
                    Distance(
                        marker.X,
                        marker.Y,
                        reticleX,
                        reticleY);

                double radialDifference =
                    Math.Abs(
                        actualDistance
                        - expectedDistance);

                combinedScore -=
                    radialDifference * 0.05;
            }

            if (combinedScore
                <= bestCombinedScore)
            {
                continue;
            }

            bestCombinedScore =
                combinedScore;

            bestCenter =
                new BodyCenterCandidate(
                    marker.X,
                    marker.Y,
                    Math.Clamp(
                        0.52
                        + marker.PathSupport * 0.34
                        + Math.Min(
                            0.12,
                            marker.PathAverageContrast
                            / 500d),
                        0d,
                        0.99));
        }

        return bestCenter;
    }

    private static List<GuideRayCandidate>
        FindGuideRayShortlist(
            DssCapturedFrame frame,
            int reticleX,
            int reticleY,
            DssDetectionHint? hint)
    {
        var candidates =
            new List<GuideRayCandidate>();

        if (hint is null)
        {
            for (double degrees = 0;
                 degrees < 360;
                 degrees += 2)
            {
                GuideRayScore score =
                    ScoreGuideRay(
                        frame,
                        reticleX,
                        reticleY,
                        DegreesToRadians(degrees));

                if (!score.Valid)
                {
                    continue;
                }

                candidates.Add(
                    new GuideRayCandidate(
                        DegreesToRadians(degrees),
                        score.Score));
            }
        }
        else
        {
            double expectedAngle =
                Math.Atan2(
                    hint.PredictedCenterY
                    - reticleY,
                    hint.PredictedCenterX
                    - reticleX);

            for (double deltaDegrees = -14;
                 deltaDegrees <= 14;
                 deltaDegrees += 1)
            {
                double angle =
                    NormalizeRadians(
                        expectedAngle
                        + DegreesToRadians(
                            deltaDegrees));

                GuideRayScore score =
                    ScoreGuideRay(
                        frame,
                        reticleX,
                        reticleY,
                        angle);

                if (!score.Valid)
                {
                    continue;
                }

                candidates.Add(
                    new GuideRayCandidate(
                        angle,
                        score.Score));
            }
        }

        if (candidates.Count == 0)
        {
            return candidates;
        }

        List<GuideRayCandidate> shortlist =
            candidates
                .OrderByDescending(
                    item => item.Score)
                .Take(GlobalRayShortlist)
                .ToList();

        // Sub-degree refinement around shortlisted orientations.
        var refined =
            new List<GuideRayCandidate>();

        foreach (GuideRayCandidate candidate
                 in shortlist)
        {
            GuideRayCandidate best = candidate;

            for (double deltaDegrees = -1.5;
                 deltaDegrees <= 1.5;
                 deltaDegrees += 0.5)
            {
                double angle =
                    NormalizeRadians(
                        candidate.AngleRadians
                        + DegreesToRadians(
                            deltaDegrees));

                GuideRayScore score =
                    ScoreGuideRay(
                        frame,
                        reticleX,
                        reticleY,
                        angle);

                if (score.Valid
                    && score.Score > best.Score)
                {
                    best =
                        new GuideRayCandidate(
                            angle,
                            score.Score);
                }
            }

            refined.Add(best);
        }

        return refined
            .OrderByDescending(
                item => item.Score)
            .Take(GlobalRayShortlist)
            .ToList();
    }

    private static GuideRayScore ScoreGuideRay(
        DssCapturedFrame frame,
        int reticleX,
        int reticleY,
        double angle)
    {
        double dx = Math.Cos(angle);
        double dy = Math.Sin(angle);
        double nx = -dy;
        double ny = dx;

        double maximumTravel =
            GetRayBoundaryDistance(
                frame,
                reticleX,
                reticleY,
                dx,
                dy)
            - 14;

        if (maximumTravel < 70)
        {
            return GuideRayScore.Invalid;
        }

        int hits = 0;
        int nearHits = 0;
        int currentRun = 0;
        int bestRun = 0;
        double contrastSum = 0;

        for (double t = 34;
             t <= maximumTravel;
             t += 4)
        {
            GuideSample sample =
                MeasureGuideSample(
                    frame,
                    reticleX + dx * t,
                    reticleY + dy * t,
                    nx,
                    ny);

            bool hit =
                sample.CenterLuma >= 55
                && sample.Contrast >= 8;

            if (hit)
            {
                hits++;
                contrastSum += sample.Contrast;
                currentRun++;

                if (t <= 100)
                {
                    nearHits++;
                }
            }
            else
            {
                currentRun =
                    Math.Max(0, currentRun - 1);
            }

            bestRun =
                Math.Max(bestRun, currentRun);
        }

        if (nearHits < 3
            || bestRun < 7
            || hits < 11)
        {
            return GuideRayScore.Invalid;
        }

        double score =
            bestRun * 7d
            + hits * 1.2
            + contrastSum * 0.02
            + nearHits * 5d;

        return new GuideRayScore(
            true,
            score,
            bestRun,
            hits,
            nearHits);
    }

    private static MarkerOnRayCandidate?
        FindMarkerOnRay(
            DssCapturedFrame frame,
            int reticleX,
            int reticleY,
            double angle,
            DssDetectionHint? hint)
    {
        double scale =
            frame.Height / 1080d;

        double dx = Math.Cos(angle);
        double dy = Math.Sin(angle);
        double nx = -dy;
        double ny = dx;

        double maximumTravel =
            GetRayBoundaryDistance(
                frame,
                reticleX,
                reticleY,
                dx,
                dy)
            - 16;

        if (maximumTravel < 40)
        {
            return null;
        }

        double startTravel = 36;
        double endTravel = maximumTravel;

        if (hint is not null)
        {
            double expectedDistance =
                Distance(
                    hint.PredictedCenterX,
                    hint.PredictedCenterY,
                    reticleX,
                    reticleY);

            startTravel =
                Math.Max(
                    34,
                    expectedDistance - 190);

            endTravel =
                Math.Min(
                    maximumTravel,
                    expectedDistance + 190);
        }

        MarkerOnRayCandidate? best = null;

        for (double t = startTravel;
             t <= endTravel;
             t += 2)
        {
            double x =
                reticleX + dx * t;

            double y =
                reticleY + dy * t;

            OrientedMarkerShape shape =
                MeasureOrientedMarker(
                    frame,
                    x,
                    y,
                    dx,
                    dy,
                    nx,
                    ny,
                    scale);

            if (!shape.Valid)
            {
                continue;
            }

            PathSupport path =
                MeasurePathSupport(
                    frame,
                    reticleX,
                    reticleY,
                    dx,
                    dy,
                    nx,
                    ny,
                    t);

            if (path.Support < 0.50)
            {
                continue;
            }

            (double refinedX, double refinedY) =
                RefineMarkerCentroid(
                    frame,
                    x,
                    y,
                    scale);

            double markerScore =
                shape.CrossHits * 2.0
                + shape.AlongHits * 2.0
                + shape.PeakLuma * 0.06
                + shape.Roundness * 24d;

            MarkerOnRayCandidate candidate =
                new(
                    refinedX,
                    refinedY,
                    markerScore,
                    path.Support,
                    path.AverageContrast);

            if (best is null
                || CandidateScore(candidate)
                   > CandidateScore(best))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static double CandidateScore(
        MarkerOnRayCandidate candidate) =>
        candidate.PathSupport * 170d
        + candidate.PathAverageContrast * 0.45
        + candidate.MarkerScore;

    private static PathSupport MeasurePathSupport(
        DssCapturedFrame frame,
        int reticleX,
        int reticleY,
        double dx,
        double dy,
        double nx,
        double ny,
        double markerTravel)
    {
        double start = 34;
        double end =
            Math.Max(
                start,
                markerTravel - 14);

        int samples = 0;
        int hits = 0;
        double contrastSum = 0;

        for (double t = start;
             t <= end;
             t += 4)
        {
            GuideSample sample =
                MeasureGuideSample(
                    frame,
                    reticleX + dx * t,
                    reticleY + dy * t,
                    nx,
                    ny);

            samples++;

            if (sample.CenterLuma >= 55
                && sample.Contrast >= 8)
            {
                hits++;
                contrastSum +=
                    sample.Contrast;
            }
        }

        if (samples == 0)
        {
            return new PathSupport(
                0,
                0);
        }

        return new PathSupport(
            (double)hits / samples,
            contrastSum
            / Math.Max(1, samples));
    }

    private static GuideSample MeasureGuideSample(
        DssCapturedFrame frame,
        double x,
        double y,
        double nx,
        double ny)
    {
        int centerLuma = 0;

        for (int offset = -2;
             offset <= 2;
             offset++)
        {
            int px =
                (int)Math.Round(
                    x + nx * offset);

            int py =
                (int)Math.Round(
                    y + ny * offset);

            centerLuma =
                Math.Max(
                    centerLuma,
                    GetNeutralLuma(
                        frame,
                        px,
                        py,
                        GuideMinimumLuma,
                        GuideMaximumSpread));
        }

        int sideTotal = 0;
        int sideSamples = 0;

        foreach (int offset
                 in new[] { -10, -7, 7, 10 })
        {
            int px =
                (int)Math.Round(
                    x + nx * offset);

            int py =
                (int)Math.Round(
                    y + ny * offset);

            sideTotal +=
                GetRawLuma(
                    frame,
                    px,
                    py);

            sideSamples++;
        }

        double sideAverage =
            sideSamples > 0
                ? sideTotal / (double)sideSamples
                : 0;

        return new GuideSample(
            centerLuma,
            centerLuma - sideAverage);
    }

    private static OrientedMarkerShape
        MeasureOrientedMarker(
            DssCapturedFrame frame,
            double x,
            double y,
            double dx,
            double dy,
            double nx,
            double ny,
            double scale)
    {
        int halfExtent =
            Math.Clamp(
                (int)Math.Round(12 * scale),
                9,
                16);

        int crossHits = 0;
        int alongHits = 0;
        int peakLuma = 0;

        for (int offset = -halfExtent;
             offset <= halfExtent;
             offset++)
        {
            int crossX =
                (int)Math.Round(
                    x + nx * offset);

            int crossY =
                (int)Math.Round(
                    y + ny * offset);

            int crossLuma =
                GetNeutralLuma(
                    frame,
                    crossX,
                    crossY,
                    MarkerMinimumLuma,
                    MarkerMaximumSpread);

            if (crossLuma
                >= MarkerMinimumLuma)
            {
                crossHits++;
                peakLuma =
                    Math.Max(
                        peakLuma,
                        crossLuma);
            }

            int alongX =
                (int)Math.Round(
                    x + dx * offset);

            int alongY =
                (int)Math.Round(
                    y + dy * offset);

            int alongLuma =
                GetNeutralLuma(
                    frame,
                    alongX,
                    alongY,
                    MarkerMinimumLuma,
                    MarkerMaximumSpread);

            if (alongLuma
                >= MarkerMinimumLuma)
            {
                alongHits++;
                peakLuma =
                    Math.Max(
                        peakLuma,
                        alongLuma);
            }
        }

        double coreMeanLuma =
            MeasureMarkerCoreMeanLuma(
                frame,
                x,
                y,
                scale);

        (int coreNeutralHits, int coreMinimumQuadrantHits) =
            MeasureMarkerCoreShape(
                frame,
                x,
                y,
                scale);

        int minimumHits =
            Math.Max(
                6,
                (int)Math.Round(7 * scale));

        if (!IsMarkerCoreLumaAccepted(coreMeanLuma)
            || !IsMarkerCoreShapeAccepted(
                coreNeutralHits,
                coreMinimumQuadrantHits,
                scale)
            || crossHits < minimumHits
            || alongHits < minimumHits
            || peakLuma < 140)
        {
            return OrientedMarkerShape.Invalid;
        }

        double roundness =
            Math.Min(crossHits, alongHits)
            / (double)Math.Max(
                1,
                Math.Max(crossHits, alongHits));

        if (roundness < 0.46)
        {
            return OrientedMarkerShape.Invalid;
        }

        return new OrientedMarkerShape(
            true,
            crossHits,
            alongHits,
            peakLuma,
            roundness);
    }

    internal static bool IsMarkerCoreLumaAccepted(
        double meanLuma) =>
        meanLuma >= MarkerCoreMinimumMeanLuma;

    internal static bool IsMarkerCoreShapeAccepted(
        int neutralHits,
        int minimumQuadrantHits,
        double scale)
    {
        double areaScale =
            Math.Clamp(
                scale * scale,
                0.35,
                2.25);

        int requiredHits =
            Math.Max(
                20,
                (int)Math.Round(60d * areaScale));

        int requiredQuadrantHits =
            Math.Max(
                2,
                (int)Math.Round(5d * areaScale));

        return neutralHits >= requiredHits
               && minimumQuadrantHits
                  >= requiredQuadrantHits;
    }

    private static (int NeutralHits, int MinimumQuadrantHits)
        MeasureMarkerCoreShape(
            DssCapturedFrame frame,
            double x,
            double y,
            double scale)
    {
        int radius =
            Math.Clamp(
                (int)Math.Round(8d * scale),
                5,
                12);

        int centerX =
            (int)Math.Round(x);

        int centerY =
            (int)Math.Round(y);

        int neutralHits = 0;
        int quadrant0 = 0;
        int quadrant1 = 0;
        int quadrant2 = 0;
        int quadrant3 = 0;

        for (int offsetY = -radius;
             offsetY <= radius;
             offsetY++)
        {
            for (int offsetX = -radius;
                 offsetX <= radius;
                 offsetX++)
            {
                if (offsetX * offsetX
                    + offsetY * offsetY
                    > radius * radius)
                {
                    continue;
                }

                int luma =
                    GetNeutralLuma(
                        frame,
                        centerX + offsetX,
                        centerY + offsetY,
                        MarkerCoreShapeMinimumLuma,
                        MarkerCoreShapeMaximumSpread);

                if (luma <= 0)
                {
                    continue;
                }

                neutralHits++;

                if (offsetX < 0)
                {
                    if (offsetY < 0)
                    {
                        quadrant0++;
                    }
                    else
                    {
                        quadrant1++;
                    }
                }
                else if (offsetY < 0)
                {
                    quadrant2++;
                }
                else
                {
                    quadrant3++;
                }
            }
        }

        int minimumQuadrantHits =
            Math.Min(
                Math.Min(quadrant0, quadrant1),
                Math.Min(quadrant2, quadrant3));

        return (
            neutralHits,
            minimumQuadrantHits);
    }

    private static double MeasureMarkerCoreMeanLuma(
        DssCapturedFrame frame,
        double x,
        double y,
        double scale)
    {
        int radius =
            Math.Clamp(
                (int)Math.Round(scale),
                1,
                2);

        int centerX =
            (int)Math.Round(x);

        int centerY =
            (int)Math.Round(y);

        int samples = 0;
        double lumaSum = 0;

        for (int offsetY = -radius;
             offsetY <= radius;
             offsetY++)
        {
            for (int offsetX = -radius;
                 offsetX <= radius;
                 offsetX++)
            {
                lumaSum +=
                    GetRawLuma(
                        frame,
                        centerX + offsetX,
                        centerY + offsetY);

                samples++;
            }
        }

        return samples > 0
            ? lumaSum / samples
            : 0;
    }

    private static MarkerAtPoint
        MeasureAxisAlignedMarker(
            DssCapturedFrame frame,
            int x,
            int y,
            double scale)
    {
        int halfExtent =
            Math.Clamp(
                (int)Math.Round(12 * scale),
                9,
                16);

        int horizontalHits = 0;
        int verticalHits = 0;
        int peakLuma = 0;

        for (int offset = -halfExtent;
             offset <= halfExtent;
             offset++)
        {
            int horizontalLuma =
                GetNeutralLuma(
                    frame,
                    x + offset,
                    y,
                    MarkerMinimumLuma,
                    MarkerMaximumSpread);

            if (horizontalLuma
                >= MarkerMinimumLuma)
            {
                horizontalHits++;
                peakLuma =
                    Math.Max(
                        peakLuma,
                        horizontalLuma);
            }

            int verticalLuma =
                GetNeutralLuma(
                    frame,
                    x,
                    y + offset,
                    MarkerMinimumLuma,
                    MarkerMaximumSpread);

            if (verticalLuma
                >= MarkerMinimumLuma)
            {
                verticalHits++;
                peakLuma =
                    Math.Max(
                        peakLuma,
                        verticalLuma);
            }
        }

        int minimumHits =
            Math.Max(
                6,
                (int)Math.Round(7 * scale));

        double roundness =
            Math.Min(
                horizontalHits,
                verticalHits)
            / (double)Math.Max(
                1,
                Math.Max(
                    horizontalHits,
                    verticalHits));

        bool valid =
            horizontalHits >= minimumHits
            && verticalHits >= minimumHits
            && roundness >= 0.52
            && peakLuma >= 145;

        double score =
            horizontalHits
            + verticalHits
            + roundness * 30
            + peakLuma * 0.04;

        return new MarkerAtPoint(
            valid,
            x,
            y,
            score,
            roundness);
    }

    private static (double X, double Y)
        RefineMarkerCentroid(
            DssCapturedFrame frame,
            double centerX,
            double centerY,
            double scale)
    {
        int radius =
            Math.Clamp(
                (int)Math.Round(11 * scale),
                8,
                15);

        double weightedX = 0;
        double weightedY = 0;
        double weight = 0;

        for (int y =
                 (int)Math.Round(centerY) - radius;
             y <=
                 (int)Math.Round(centerY) + radius;
             y++)
        {
            for (int x =
                     (int)Math.Round(centerX) - radius;
                 x <=
                     (int)Math.Round(centerX) + radius;
                 x++)
            {
                double dx =
                    x - centerX;

                double dy =
                    y - centerY;

                if (dx * dx + dy * dy
                    > radius * radius)
                {
                    continue;
                }

                int luma =
                    GetNeutralLuma(
                        frame,
                        x,
                        y,
                        105,
                        130);

                if (luma <= 0)
                {
                    continue;
                }

                double localWeight =
                    Math.Max(1, luma - 90);

                weightedX +=
                    x * localWeight;

                weightedY +=
                    y * localWeight;

                weight += localWeight;
            }
        }

        return weight > 0
            ? (
                weightedX / weight,
                weightedY / weight)
            : (
                centerX,
                centerY);
    }

    private static HorizonRecoveryCandidate?
        FindCenterFromTrustedHorizon(
            DssCapturedFrame frame,
            int reticleX,
            int reticleY,
            double expectedHorizonRadius,
            DssDetectionHint? hint)
    {
        if (expectedHorizonRadius <= 25)
        {
            return null;
        }

        List<GuideRayCandidate> rays =
            FindGuideRayShortlist(
                frame,
                reticleX,
                reticleY,
                hint);

        if (rays.Count == 0)
        {
            return null;
        }

        HorizonRecoveryCandidate? best = null;

        foreach (GuideRayCandidate ray in rays)
        {
            double dx =
                Math.Cos(ray.AngleRadians);

            double dy =
                Math.Sin(ray.AngleRadians);

            double nx = -dy;
            double ny = dx;

            double maximumTravel =
                GetRayBoundaryDistance(
                    frame,
                    reticleX,
                    reticleY,
                    dx,
                    dy)
                - 24;

            if (maximumTravel < 70)
            {
                continue;
            }

            double startTravel = 38;
            double endTravel = maximumTravel;

            // LOCAL recovery is allowed to use the previous centre merely as
            // a search accelerator. GLOBAL recovery passes hint=null and scans
            // all shortlisted reticle-anchored rays.
            if (hint is not null)
            {
                double expectedCenterDistance =
                    Distance(
                        hint.PredictedCenterX,
                        hint.PredictedCenterY,
                        reticleX,
                        reticleY);

                if (expectedCenterDistance
                    > expectedHorizonRadius + 25)
                {
                    double expectedHorizonTravel =
                        expectedCenterDistance
                        - expectedHorizonRadius;

                    startTravel =
                        Math.Max(
                            34,
                            expectedHorizonTravel - 150);

                    endTravel =
                        Math.Min(
                            maximumTravel,
                            expectedHorizonTravel + 150);
                }
            }

            for (double t = startTravel;
                 t <= endTravel;
                 t += 2)
            {
                double horizonX =
                    reticleX + dx * t;

                double horizonY =
                    reticleY + dy * t;

                if (!IsSafeInitialHorizonPoint(
                        frame,
                        horizonX,
                        horizonY))
                {
                    continue;
                }

                DashShape shape =
                    MeasureHorizonDash(
                        frame,
                        horizonX,
                        horizonY,
                        dx,
                        dy,
                        nx,
                        ny);

                if (!shape.Valid)
                {
                    continue;
                }

                PathSupport before =
                    MeasureGuideSegmentSupport(
                        frame,
                        reticleX,
                        reticleY,
                        dx,
                        dy,
                        nx,
                        ny,
                        34,
                        Math.Max(
                            34,
                            t - 14));

                if (before.Support < 0.62
                    || before.AverageContrast < 10)
                {
                    continue;
                }

                double afterStart =
                    t + 14;

                double afterEnd =
                    Math.Min(
                        maximumTravel,
                        t
                        + expectedHorizonRadius
                        - 14);

                bool hasUsefulAfterSegment =
                    afterEnd - afterStart >= 42;

                PathSupport after =
                    hasUsefulAfterSegment
                        ? MeasureGuideSegmentSupport(
                            frame,
                            reticleX,
                            reticleY,
                            dx,
                            dy,
                            nx,
                            ny,
                            afterStart,
                            afterEnd)
                        : new PathSupport(1, 0);

                // The user-visible failure case explicitly still has the guide
                // continuing from H toward the clipped/off-screen centre. This
                // is a powerful discriminator against Milky-Way texture.
                if (hasUsefulAfterSegment
                    && (after.Support < 0.42
                        || after.AverageContrast < 6))
                {
                    continue;
                }

                double centerTravel =
                    t + expectedHorizonRadius;

                double centerX =
                    reticleX
                    + dx * centerTravel;

                double centerY =
                    reticleY
                    + dy * centerTravel;

                double margin =
                    Math.Max(
                        frame.Width,
                        frame.Height)
                    * 0.65;

                if (centerX < -margin
                    || centerX
                       > frame.Width - 1 + margin
                    || centerY < -margin
                    || centerY
                       > frame.Height - 1 + margin)
                {
                    continue;
                }

                double dashScore =
                    shape.InnerHits * 3.5
                    - shape.OuterHits * 3.0
                    - shape.ParallelHits * 0.20
                    + shape.PeakLuma * 0.05;

                double score =
                    dashScore
                    + before.Support * 180d
                    + before.AverageContrast * 0.50
                    + after.Support * 115d
                    + after.AverageContrast * 0.28
                    + ray.Score * 0.12;

                double confidence =
                    Math.Clamp(
                        0.68
                        + before.Support * 0.12
                        + (hasUsefulAfterSegment
                            ? after.Support * 0.08
                            : 0.04),
                        0d,
                        0.94);

                if (best is null
                    || score > best.Score)
                {
                    best =
                        new HorizonRecoveryCandidate(
                            centerX,
                            centerY,
                            horizonX,
                            horizonY,
                            confidence,
                            score);
                }
            }
        }

        return best;
    }

    private static PathSupport
        MeasureGuideSegmentSupport(
            DssCapturedFrame frame,
            int reticleX,
            int reticleY,
            double dx,
            double dy,
            double nx,
            double ny,
            double startTravel,
            double endTravel)
    {
        if (endTravel < startTravel)
        {
            return new PathSupport(
                0,
                0);
        }

        int samples = 0;
        int hits = 0;
        double contrastSum = 0;

        for (double t = startTravel;
             t <= endTravel;
             t += 4)
        {
            GuideSample sample =
                MeasureGuideSample(
                    frame,
                    reticleX + dx * t,
                    reticleY + dy * t,
                    nx,
                    ny);

            samples++;

            if (sample.CenterLuma >= 55
                && sample.Contrast >= 8)
            {
                hits++;
                contrastSum +=
                    sample.Contrast;
            }
        }

        if (samples == 0)
        {
            return new PathSupport(
                0,
                0);
        }

        return new PathSupport(
            (double)hits / samples,
            contrastSum
            / Math.Max(1, samples));
    }

    private static DssHudGeometry
        BuildRecoveredGeometry(
            DssCapturedFrame frame,
            double verticalFovDegrees,
            HorizonRecoveryCandidate recovery,
            double expectedHorizonRadius)
    {
        int reticleX =
            frame.Width / 2;

        int reticleY =
            frame.Height / 2;

        double dx =
            recovery.CenterX - reticleX;

        double dy =
            recovery.CenterY - reticleY;

        double aimRadius =
            Math.Sqrt(
                dx * dx + dy * dy);

        double focalPixels =
            GetFocalPixels(
                frame.Height,
                verticalFovDegrees);

        double aimOffsetDegrees =
            Math.Atan2(
                aimRadius,
                focalPixels)
            * 180d / Math.PI;

        return new DssHudGeometry(
            reticleX,
            reticleY,
            true,
            recovery.CenterX,
            recovery.CenterY,
            recovery.Confidence,
            true,
            true,
            recovery.HorizonX,
            recovery.HorizonY,
            recovery.Confidence,
            0,
            expectedHorizonRadius,
            aimRadius
            - expectedHorizonRadius,
            aimOffsetDegrees);
    }

    private static HorizonCandidate?
        FindHorizonMarker(
        DssCapturedFrame frame,
        double centerX,
        double centerY,
        int reticleX,
        int reticleY,
        double? expectedRadius)
    {
        double vx =
            reticleX - centerX;

        double vy =
            reticleY - centerY;

        double aimRadius =
            Math.Sqrt(vx * vx + vy * vy);

        if (aimRadius < 35)
        {
            return null;
        }

        double dx = vx / aimRadius;
        double dy = vy / aimRadius;
        double nx = -dy;
        double ny = dx;

        double startTravel;
        double endTravel;

        if (expectedRadius is > 25)
        {
            double gate =
                Math.Clamp(
                    expectedRadius.Value
                    * 0.065,
                    13,
                    34);

            startTravel =
                Math.Max(
                    20,
                    expectedRadius.Value
                    - gate);

            endTravel =
                expectedRadius.Value
                + gate;
        }
        else
        {
            // Initial Rh acquisition must happen in the empirically clean
            // radius band. The v8 session showed two recurring false families:
            // tiny radii around planet/reticle detail and ~700 px radii where
            // the scan reached static lower HUD. Known-good horizon radii in
            // our datasets are roughly 330-405 px at 1080p.
            //
            // Keep the gate resolution-independent while still generous.
            double minimumInitialRadius =
                frame.Height * 0.18;

            double maximumInitialRadius =
                frame.Height * 0.62;

            startTravel =
                Math.Max(
                    24,
                    minimumInitialRadius);

            endTravel =
                Math.Min(
                    Math.Min(
                        aimRadius
                        + Math.Min(
                            frame.Width,
                            frame.Height)
                          * 0.42,
                        Math.Max(
                            frame.Width,
                            frame.Height)),
                    maximumInitialRadius);
        }

        HorizonCandidate? best = null;

        for (double t = startTravel;
             t <= endTravel;
             t += 1)
        {
            double x =
                centerX + dx * t;

            double y =
                centerY + dy * t;

            if (x < 24
                || y < 24
                || x >= frame.Width - 24
                || y >= frame.Height - 24)
            {
                continue;
            }

            // Before Rh is trusted, do not acquire from known static HUD
            // bands. Once an expected radius exists, the tight radial gate is
            // already the dominant discriminator and this restriction is no
            // longer necessary.
            if (expectedRadius is null
                && !IsSafeInitialHorizonPoint(
                    frame,
                    x,
                    y))
            {
                continue;
            }

            DashShape shape =
                MeasureHorizonDash(
                    frame,
                    x,
                    y,
                    dx,
                    dy,
                    nx,
                    ny);

            if (!shape.Valid)
            {
                continue;
            }

            double score =
                shape.InnerHits * 3.5
                - shape.OuterHits * 3.0
                - shape.ParallelHits * 0.20
                + shape.PeakLuma * 0.05;

            if (expectedRadius is > 25)
            {
                score -=
                    Math.Abs(
                        t - expectedRadius.Value)
                    / Math.Max(
                        1d,
                        expectedRadius.Value)
                    * 90d;
            }

            if (best is null
                || score > best.Score)
            {
                best =
                    new HorizonCandidate(
                        x,
                        y,
                        score,
                        Math.Clamp(
                            0.30
                            + (shape.InnerHits
                               - shape.OuterHits)
                              / 16d,
                            0d,
                            1d));
            }
        }

        return best;
    }

    private static DashShape MeasureHorizonDash(
        DssCapturedFrame frame,
        double x,
        double y,
        double dx,
        double dy,
        double nx,
        double ny)
    {
        const int halfCross = 24;
        const int radialThickness = 2;

        int sampleCount =
            halfCross * 2 + 1;

        int[] luma =
            new int[sampleCount];

        int peakLuma = 0;

        for (int crossOffset = -halfCross;
             crossOffset <= halfCross;
             crossOffset++)
        {
            int bestLuma = 0;

            for (int radialOffset = -radialThickness;
                 radialOffset <= radialThickness;
                 radialOffset++)
            {
                int px =
                    (int)Math.Round(
                        x
                        + nx * crossOffset
                        + dx * radialOffset);

                int py =
                    (int)Math.Round(
                        y
                        + ny * crossOffset
                        + dy * radialOffset);

                bestLuma =
                    Math.Max(
                        bestLuma,
                        GetNeutralLuma(
                            frame,
                            px,
                            py,
                            HorizonMinimumLuma,
                            HorizonMaximumSpread));
            }

            luma[crossOffset + halfCross] =
                bestLuma;

            peakLuma =
                Math.Max(
                    peakLuma,
                    bestLuma);
        }

        if (peakLuma < 170)
        {
            return DashShape.Invalid;
        }

        var runs =
            new List<HorizonWhiteRun>();

        int runStart = -1;
        int runPeak = 0;

        for (int index = 0;
             index <= sampleCount;
             index++)
        {
            bool white =
                index < sampleCount
                && luma[index]
                   >= HorizonMinimumLuma;

            if (white)
            {
                if (runStart < 0)
                {
                    runStart = index;
                    runPeak = luma[index];
                }
                else
                {
                    runPeak =
                        Math.Max(
                            runPeak,
                            luma[index]);
                }

                continue;
            }

            if (runStart < 0)
            {
                continue;
            }

            int runEnd =
                index - 1;

            runs.Add(
                new HorizonWhiteRun(
                    runStart - halfCross,
                    runEnd - halfCross,
                    runEnd - runStart + 1,
                    runPeak));

            runStart = -1;
            runPeak = 0;
        }

        // Frontier's actual horizon marker is not a generic line. In the
        // supplied clean captures it is consistently:
        //
        //    ---   -------   ---
        //
        // centred on the radial guide and perpendicular to it.
        //
        // The v10 false candidate inside the planet was a cyan projected-grid
        // arc. A planet limb is one long continuous run. Neither has this
        // three-run neutral-white topology.
        HorizonWhiteRun? centerRun =
            runs
                .Where(run =>
                    run.Start <= 4
                    && run.End >= -4
                    && run.Width >= 2
                    && run.Width <= 11)
                .OrderBy(run =>
                    Math.Abs(run.Center))
                .FirstOrDefault();

        if (centerRun is null
            || Math.Abs(centerRun.Center) > 4)
        {
            return DashShape.Invalid;
        }

        HorizonWhiteRun? leftRun =
            runs
                .Where(run =>
                    run.End
                        < centerRun.Start - 1
                    && run.Center >= -18
                    && run.Center <= -5
                    && run.Width >= 1
                    && run.Width <= 8)
                .OrderByDescending(run =>
                    run.PeakLuma)
                .FirstOrDefault();

        HorizonWhiteRun? rightRun =
            runs
                .Where(run =>
                    run.Start
                        > centerRun.End + 1
                    && run.Center >= 5
                    && run.Center <= 18
                    && run.Width >= 1
                    && run.Width <= 8)
                .OrderByDescending(run =>
                    run.PeakLuma)
                .FirstOrDefault();

        if (leftRun is null
            || rightRun is null)
        {
            return DashShape.Invalid;
        }

        double symmetryError =
            Math.Abs(
                Math.Abs(leftRun.Center)
                - Math.Abs(rightRun.Center));

        if (symmetryError > 5)
        {
            return DashShape.Invalid;
        }

        int totalSpan =
            rightRun.End
            - leftRun.Start
            + 1;

        if (totalSpan < 13
            || totalSpan > 31)
        {
            return DashShape.Invalid;
        }

        // Reject a long white structure continuing beyond the triplet. This
        // protects against the neutral planet limb and HUD text strokes.
        int externalHits = 0;

        foreach (HorizonWhiteRun run
                 in runs)
        {
            bool chosen =
                ReferenceEquals(
                    run,
                    centerRun)
                || ReferenceEquals(
                    run,
                    leftRun)
                || ReferenceEquals(
                    run,
                    rightRun);

            if (chosen)
            {
                continue;
            }

            if (run.End
                    < leftRun.Start - 1
                || run.Start
                    > rightRun.End + 1)
            {
                externalHits +=
                    run.Width;
            }
        }

        if (externalHits > 4)
        {
            return DashShape.Invalid;
        }

        int tripletHits =
            leftRun.Width
            + centerRun.Width
            + rightRun.Width;

        int tripletPeak =
            Math.Max(
                centerRun.PeakLuma,
                Math.Max(
                    leftRun.PeakLuma,
                    rightRun.PeakLuma));

        // Preserve the existing DashShape/score contract. "InnerHits" now
        // means pixels belonging to the verified three-dash marker and
        // "OuterHits" means unrelated white continuation.
        return new DashShape(
            true,
            tripletHits,
            externalHits,
            0,
            tripletPeak);
    }

    private static bool IsSafeInitialHorizonPoint(
        DssCapturedFrame frame,
        double x,
        double y)
    {
        double xRatio =
            x / Math.Max(1d, frame.Width);

        double yRatio =
            y / Math.Max(1d, frame.Height);

        // Exclude:
        // - left/right DSS scales and edge labels;
        // - upper edge labels;
        // - the lower information/control HUD where v8 repeatedly acquired
        //   Rh ~700 px false positives.
        return xRatio >= 0.12
               && xRatio <= 0.88
               && yRatio >= 0.10
               && yRatio <= 0.78;
    }

    private static double GetRayBoundaryDistance(
        DssCapturedFrame frame,
        double x,
        double y,
        double dx,
        double dy)
    {
        double best =
            double.PositiveInfinity;

        if (dx > 0.000001)
        {
            best =
                Math.Min(
                    best,
                    (frame.Width - 1 - x)
                    / dx);
        }
        else if (dx < -0.000001)
        {
            best =
                Math.Min(
                    best,
                    (0 - x) / dx);
        }

        if (dy > 0.000001)
        {
            best =
                Math.Min(
                    best,
                    (frame.Height - 1 - y)
                    / dy);
        }
        else if (dy < -0.000001)
        {
            best =
                Math.Min(
                    best,
                    (0 - y) / dy);
        }

        return double.IsFinite(best)
            ? best
            : 0;
    }

    private static int GetNeutralLuma(
        DssCapturedFrame frame,
        int x,
        int y,
        int minimumLuma,
        int maximumSpread)
    {
        if ((uint)x >= (uint)frame.Width
            || (uint)y >= (uint)frame.Height)
        {
            return 0;
        }

        int index =
            y * frame.Stride + x * 4;

        int blue =
            frame.Bgra32[index];

        int green =
            frame.Bgra32[index + 1];

        int red =
            frame.Bgra32[index + 2];

        int maximum =
            Math.Max(
                red,
                Math.Max(green, blue));

        int minimum =
            Math.Min(
                red,
                Math.Min(green, blue));

        if (maximum - minimum
            > maximumSpread)
        {
            return 0;
        }

        int luma =
            (red * 54
             + green * 183
             + blue * 19) >> 8;

        return luma >= minimumLuma
            ? luma
            : 0;
    }

    private static int GetRawLuma(
        DssCapturedFrame frame,
        int x,
        int y)
    {
        if ((uint)x >= (uint)frame.Width
            || (uint)y >= (uint)frame.Height)
        {
            return 0;
        }

        int index =
            y * frame.Stride + x * 4;

        int blue =
            frame.Bgra32[index];

        int green =
            frame.Bgra32[index + 1];

        int red =
            frame.Bgra32[index + 2];

        return (red * 54
                + green * 183
                + blue * 19) >> 8;
    }

    private static double Distance(
        double x1,
        double y1,
        double x2,
        double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;

        return Math.Sqrt(
            dx * dx + dy * dy);
    }

    private static double DegreesToRadians(
        double degrees) =>
        degrees * Math.PI / 180d;

    private static double NormalizeRadians(
        double radians)
    {
        double twoPi =
            Math.PI * 2d;

        radians %= twoPi;

        if (radians < 0)
        {
            radians += twoPi;
        }

        return radians;
    }

    private sealed record BodyCenterCandidate(
        double X,
        double Y,
        double Confidence);

    private sealed record GuideRayCandidate(
        double AngleRadians,
        double Score);

    private readonly record struct GuideRayScore(
        bool Valid,
        double Score,
        int BestRun,
        int Hits,
        int NearHits)
    {
        public static GuideRayScore Invalid { get; } =
            new(false, 0, 0, 0, 0);
    }

    private readonly record struct GuideSample(
        int CenterLuma,
        double Contrast);

    private sealed record MarkerOnRayCandidate(
        double X,
        double Y,
        double MarkerScore,
        double PathSupport,
        double PathAverageContrast);

    private readonly record struct PathSupport(
        double Support,
        double AverageContrast);

    private readonly record struct OrientedMarkerShape(
        bool Valid,
        int CrossHits,
        int AlongHits,
        int PeakLuma,
        double Roundness)
    {
        public static OrientedMarkerShape Invalid { get; } =
            new(false, 0, 0, 0, 0);
    }

    private sealed record MarkerAtPoint(
        bool Valid,
        double X,
        double Y,
        double Score,
        double Roundness);

    private sealed record HorizonRecoveryCandidate(
        double CenterX,
        double CenterY,
        double HorizonX,
        double HorizonY,
        double Confidence,
        double Score);

    private sealed record HorizonCandidate(
        double X,
        double Y,
        double Score,
        double Confidence);

    private sealed record HorizonWhiteRun(
        int Start,
        int End,
        int Width,
        int PeakLuma)
    {
        public double Center =>
            (Start + End) / 2d;
    }

    private readonly record struct DashShape(
        bool Valid,
        int InnerHits,
        int OuterHits,
        int ParallelHits,
        int PeakLuma)
    {
        public static DashShape Invalid { get; } =
            new(false, 0, 0, 0, 0);
    }
}
