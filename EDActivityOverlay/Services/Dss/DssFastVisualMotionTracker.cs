using System;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssFastVisualMotionSnapshot(
    DateTimeOffset TimestampUtc,
    int FrameWidth,
    int FrameHeight,
    double CenterX,
    double CenterY,
    double VelocityX,
    double VelocityY,
    double Confidence,
    double MeanError);

/// <summary>
/// Presentation-only high-cadence native DSS centre-marker tracker.
///
/// v36 used texture SAD around the planet surface. That proved fundamentally
/// fragile: the DSS grid and coverage texture repeat, frame-to-frame template
/// refresh can drift, and after a pan the visual centre can oscillate around
/// the correct heavy-CV centre.
///
/// v37 tracks Frontier's own filled neutral-white centre marker instead.
/// Heavy DssHudGeometryTracker remains authoritative for gameplay logic and
/// supplies absolute anchors / velocity. This class only gives WPF a newer C.
/// </summary>
internal sealed class DssFastVisualMotionTracker
{
    private static readonly TimeSpan MaximumHeavyAnchorAge =
        TimeSpan.FromMilliseconds(1500);

    private static readonly TimeSpan MaximumTrackGap =
        TimeSpan.FromMilliseconds(320);

    private const double HeavyAnchorMinimumConfidence = 0.66d;
    private const double PositionHoldRadiusPixels = 0.85d;
    private const double MaximumVelocityPixelsPerSecond = 2600d;

    private const int MinimumSearchRadiusPixels = 18;
    private const int MaximumSearchRadiusPixels = 72;

    private readonly object gate =
        new();

    private bool hasTrack;

    private double centerX;
    private double centerY;

    private double velocityX;
    private double velocityY;

    private double lastHeavyVelocityX;
    private double lastHeavyVelocityY;

    private DateTimeOffset lastTrackUtc =
        DateTimeOffset.MinValue;

    private DateTimeOffset lastHeavyAnchorUtc =
        DateTimeOffset.MinValue;

    private int consecutiveFailures;

    public void Reset()
    {
        lock (gate)
        {
            hasTrack = false;

            centerX = 0;
            centerY = 0;

            velocityX = 0;
            velocityY = 0;

            lastHeavyVelocityX = 0;
            lastHeavyVelocityY = 0;

            lastTrackUtc =
                DateTimeOffset.MinValue;

            lastHeavyAnchorUtc =
                DateTimeOffset.MinValue;

            consecutiveFailures = 0;
        }
    }

    public void UpdateHeavyAnchor(
        DssCapturedFrame frame,
        DssHudTrackResult tracking)
    {
        DssHudGeometry geometry =
            tracking.Geometry;

        // IMAGE is a texture bridge in the main tracker. Only an actual
        // non-IMAGE heavy observation is allowed to calibrate this tracker.
        if (tracking.SearchMode.Equals(
                "IMAGE",
                StringComparison.OrdinalIgnoreCase)
            || tracking.CenterState
               != DssCenterTrackState.Tracking
            || !geometry.BodyCenterFound
            || geometry.BodyCenterConfidence
               < HeavyAnchorMinimumConfidence)
        {
            return;
        }

        lock (gate)
        {
            if (frame.TimestampUtc
                > lastHeavyAnchorUtc)
            {
                lastHeavyAnchorUtc =
                    frame.TimestampUtc;

                lastHeavyVelocityX =
                    FiniteOrZero(
                        tracking.CenterVelocityX);

                lastHeavyVelocityY =
                    FiniteOrZero(
                        tracking.CenterVelocityY);
            }

            // Heavy CV can finish after the fast task has already consumed a
            // newer WGC frame. Never rewind visual C to an older timestamp.
            if (hasTrack
                && frame.TimestampUtc
                   < lastTrackUtc)
            {
                return;
            }

            centerX =
                geometry.BodyCenterX;

            centerY =
                geometry.BodyCenterY;

            velocityX =
                FiniteOrZero(
                    tracking.CenterVelocityX);

            velocityY =
                FiniteOrZero(
                    tracking.CenterVelocityY);

            lastTrackUtc =
                frame.TimestampUtc;

            hasTrack = true;
            consecutiveFailures = 0;
        }
    }

    public bool TryTrack(
        DssCapturedFrame frame,
        out DssFastVisualMotionSnapshot? snapshot)
    {
        snapshot = null;

        lock (gate)
        {
            if (!hasTrack
                || lastTrackUtc
                   == DateTimeOffset.MinValue
                || lastHeavyAnchorUtc
                   == DateTimeOffset.MinValue
                || frame.TimestampUtc
                   <= lastTrackUtc)
            {
                return false;
            }

            TimeSpan anchorAge =
                frame.TimestampUtc
                - lastHeavyAnchorUtc;

            TimeSpan trackGap =
                frame.TimestampUtc
                - lastTrackUtc;

            if (anchorAge < TimeSpan.Zero
                || anchorAge
                   > MaximumHeavyAnchorAge
                || trackGap <= TimeSpan.Zero
                || trackGap
                   > MaximumTrackGap)
            {
                velocityX = 0;
                velocityY = 0;

                return false;
            }

            double dt =
                trackGap.TotalSeconds;

            double trackSpeed =
                Speed(
                    velocityX,
                    velocityY);

            double heavySpeed =
                Speed(
                    lastHeavyVelocityX,
                    lastHeavyVelocityY);

            double predictionVelocityX =
                velocityX;

            double predictionVelocityY =
                velocityY;

            // Heavy sees the beginning of a pan before FAST has its first
            // measured displacement. Use that vector only as a prediction seed.
            if (trackSpeed < 70d
                && heavySpeed >= 70d)
            {
                predictionVelocityX =
                    lastHeavyVelocityX;

                predictionVelocityY =
                    lastHeavyVelocityY;
            }

            double predictedX =
                centerX
                + predictionVelocityX
                  * dt;

            double predictedY =
                centerY
                + predictionVelocityY
                  * dt;

            double referenceSpeed =
                Math.Max(
                    trackSpeed,
                    heavySpeed);

            int searchRadius =
                Math.Clamp(
                    MinimumSearchRadiusPixels
                    + (int)Math.Ceiling(
                        referenceSpeed
                        * dt),
                    MinimumSearchRadiusPixels,
                    MaximumSearchRadiusPixels);

            if (!TryFindNativeCenterMarker(
                    frame,
                    predictedX,
                    predictedY,
                    searchRadius,
                    out NativeCenterMarker marker))
            {
                HandleMiss(
                    heavySpeed);

                return false;
            }

            double innovation =
                Distance(
                    marker.X,
                    marker.Y,
                    predictedX,
                    predictedY);

            double maximumInnovation =
                Math.Clamp(
                    8d
                    + referenceSpeed
                      * dt
                      * 0.85d,
                    8d,
                    38d);

            if (innovation
                > maximumInnovation)
            {
                HandleMiss(
                    heavySpeed);

                return false;
            }

            double dx =
                marker.X
                - centerX;

            double dy =
                marker.Y
                - centerY;

            double displacement =
                Math.Sqrt(
                    dx * dx
                    + dy * dy);

            if (displacement
                <= PositionHoldRadiusPixels)
            {
                // Keep the sub-pixel absolute C fixed at rest. We still move
                // lastTrackUtc forward so freshness is preserved.
                velocityX = 0;
                velocityY = 0;
            }
            else
            {
                double measuredVelocityX =
                    dx / dt;

                double measuredVelocityY =
                    dy / dt;

                double measuredSpeed =
                    Speed(
                        measuredVelocityX,
                        measuredVelocityY);

                if (measuredSpeed
                    > MaximumVelocityPixelsPerSecond)
                {
                    double scale =
                        MaximumVelocityPixelsPerSecond
                        / measuredSpeed;

                    measuredVelocityX *=
                        scale;

                    measuredVelocityY *=
                        scale;
                }

                velocityX =
                    measuredVelocityX;

                velocityY =
                    measuredVelocityY;

                centerX =
                    marker.X;

                centerY =
                    marker.Y;
            }

            lastTrackUtc =
                frame.TimestampUtc;

            consecutiveFailures = 0;

            snapshot =
                new DssFastVisualMotionSnapshot(
                    frame.TimestampUtc,
                    frame.Width,
                    frame.Height,
                    centerX,
                    centerY,
                    velocityX,
                    velocityY,
                    marker.Confidence,
                    marker.Error);

            return true;
        }
    }

    private void HandleMiss(
        double heavySpeed)
    {
        consecutiveFailures++;

        if (heavySpeed < 55d)
        {
            velocityX = 0;
            velocityY = 0;
            return;
        }

        if (consecutiveFailures >= 2)
        {
            // Keep direction during a short marker dropout. The visual path
            // will still fall back to the normal heavy predictor if FAST age
            // exceeds its display lifetime.
            velocityX =
                lastHeavyVelocityX;

            velocityY =
                lastHeavyVelocityY;
        }
    }

    /// <summary>
    /// Finds Frontier's filled white body-centre disk in a small ROI around
    /// the predicted C. The marker is validated as:
    /// - neutral and bright;
    /// - filled in all four quadrants;
    /// - roughly round on horizontal/vertical axes;
    /// - connected to a guide-like segment toward the fixed DSS reticle.
    ///
    /// This tracks an actual native screen-space feature, so unlike texture
    /// matching it has no cumulative template drift.
    /// </summary>
    internal static bool TryFindNativeCenterMarker(
        DssCapturedFrame frame,
        double predictedCenterX,
        double predictedCenterY,
        int searchRadiusPixels,
        out NativeCenterMarker marker)
    {
        marker =
            NativeCenterMarker.Empty;

        int radius =
            Math.Clamp(
                searchRadiusPixels,
                8,
                MaximumSearchRadiusPixels);

        int predictedX =
            (int)Math.Round(
                predictedCenterX);

        int predictedY =
            (int)Math.Round(
                predictedCenterY);

        double scale =
            frame.Height / 1080d;

        NativeCenterMarker? best =
            null;

        // Coarse 2 px lattice. The native disk radius is large enough that at
        // least one candidate lands well inside the core.
        for (int y = predictedY - radius;
             y <= predictedY + radius;
             y += 2)
        {
            for (int x = predictedX - radius;
                 x <= predictedX + radius;
                 x += 2)
            {
                if ((uint)x
                        >= (uint)frame.Width
                    || (uint)y
                       >= (uint)frame.Height)
                {
                    continue;
                }

                int centerLuma =
                    GetNeutralLuma(
                        frame,
                        x,
                        y,
                        minimumLuma: 135,
                        maximumSpread: 105);

                if (centerLuma <= 0)
                {
                    continue;
                }

                if (!MeasureMarkerShape(
                        frame,
                        x,
                        y,
                        scale,
                        out double shapeScore))
                {
                    continue;
                }

                if (!MeasureLocalGuideSupport(
                        frame,
                        x,
                        y,
                        out double guideSupport))
                {
                    continue;
                }

                double distance =
                    Distance(
                        x,
                        y,
                        predictedCenterX,
                        predictedCenterY);

                double score =
                    shapeScore
                    + guideSupport * 85d
                    - distance * 0.35d;

                (double refinedX, double refinedY) =
                    RefineMarkerCentroid(
                        frame,
                        x,
                        y,
                        scale);

                double error =
                    Distance(
                        refinedX,
                        refinedY,
                        predictedCenterX,
                        predictedCenterY);

                double confidence =
                    Math.Clamp(
                        0.72d
                        + guideSupport * 0.18d
                        + Math.Min(
                            0.08d,
                            shapeScore / 1500d),
                        0d,
                        0.99d);

                var candidate =
                    new NativeCenterMarker(
                        refinedX,
                        refinedY,
                        confidence,
                        error,
                        score);

                if (best is null
                    || candidate.Score
                       > best.Value.Score)
                {
                    best =
                        candidate;
                }
            }
        }

        if (best is null)
        {
            return false;
        }

        marker =
            best.Value;

        return true;
    }

    private static bool MeasureMarkerShape(
        DssCapturedFrame frame,
        int x,
        int y,
        double scale,
        out double score)
    {
        score = 0d;

        int halfExtent =
            Math.Clamp(
                (int)Math.Round(
                    12d * scale),
                9,
                16);

        int horizontalHits = 0;
        int verticalHits = 0;
        int peakLuma = 0;

        for (int offset = -halfExtent;
             offset <= halfExtent;
             offset++)
        {
            int horizontal =
                GetNeutralLuma(
                    frame,
                    x + offset,
                    y,
                    120,
                    120);

            if (horizontal >= 120)
            {
                horizontalHits++;
                peakLuma =
                    Math.Max(
                        peakLuma,
                        horizontal);
            }

            int vertical =
                GetNeutralLuma(
                    frame,
                    x,
                    y + offset,
                    120,
                    120);

            if (vertical >= 120)
            {
                verticalHits++;
                peakLuma =
                    Math.Max(
                        peakLuma,
                        vertical);
            }
        }

        int minimumHits =
            Math.Max(
                6,
                (int)Math.Round(
                    7d * scale));

        double roundness =
            Math.Min(
                horizontalHits,
                verticalHits)
            / (double)Math.Max(
                1,
                Math.Max(
                    horizontalHits,
                    verticalHits));

        if (horizontalHits < minimumHits
            || verticalHits < minimumHits
            || roundness < 0.52d
            || peakLuma < 145)
        {
            return false;
        }

        int coreRadius =
            Math.Clamp(
                (int)Math.Round(
                    8d * scale),
                5,
                12);

        int neutralHits = 0;
        int[] quadrants =
            new int[4];

        for (int oy = -coreRadius;
             oy <= coreRadius;
             oy++)
        {
            for (int ox = -coreRadius;
                 ox <= coreRadius;
                 ox++)
            {
                if (ox * ox
                    + oy * oy
                    > coreRadius
                      * coreRadius)
                {
                    continue;
                }

                int luma =
                    GetNeutralLuma(
                        frame,
                        x + ox,
                        y + oy,
                        110,
                        80);

                if (luma <= 0)
                {
                    continue;
                }

                neutralHits++;

                int quadrant =
                    ox < 0
                        ? (oy < 0 ? 0 : 1)
                        : (oy < 0 ? 2 : 3);

                quadrants[quadrant]++;
            }
        }

        double areaScale =
            Math.Clamp(
                scale * scale,
                0.35d,
                2.25d);

        int requiredHits =
            Math.Max(
                20,
                (int)Math.Round(
                    60d * areaScale));

        int requiredQuadrantHits =
            Math.Max(
                2,
                (int)Math.Round(
                    5d * areaScale));

        int minimumQuadrant =
            Math.Min(
                Math.Min(
                    quadrants[0],
                    quadrants[1]),
                Math.Min(
                    quadrants[2],
                    quadrants[3]));

        if (neutralHits < requiredHits
            || minimumQuadrant
               < requiredQuadrantHits)
        {
            return false;
        }

        double coreMean =
            MeasureRawCoreMean(
                frame,
                x,
                y,
                scale);

        if (coreMean < 125d)
        {
            return false;
        }

        score =
            horizontalHits
            + verticalHits
            + roundness * 30d
            + peakLuma * 0.04d
            + neutralHits * 0.08d;

        return true;
    }

    private static bool MeasureLocalGuideSupport(
        DssCapturedFrame frame,
        int markerX,
        int markerY,
        out double support)
    {
        support = 0d;

        double reticleX =
            frame.Width / 2d;

        double reticleY =
            frame.Height / 2d;

        double vx =
            markerX - reticleX;

        double vy =
            markerY - reticleY;

        double length =
            Math.Sqrt(
                vx * vx
                + vy * vy);

        if (length < 72d)
        {
            // Very near the reticle there is not enough independent guide
            // length. Marker shape + prediction proximity remain the gates.
            support = 0.55d;
            return true;
        }

        double ux =
            vx / length;

        double uy =
            vy / length;

        double nx =
            -uy;

        double ny =
            ux;

        double maximumBacktrack =
            Math.Min(
                110d,
                length - 34d);

        int samples = 0;
        int hits = 0;

        for (double back = 18d;
             back <= maximumBacktrack;
             back += 7d)
        {
            double px =
                markerX - ux * back;

            double py =
                markerY - uy * back;

            int centerLuma = 0;

            for (int offset = -2;
                 offset <= 2;
                 offset++)
            {
                int sx =
                    (int)Math.Round(
                        px + nx * offset);

                int sy =
                    (int)Math.Round(
                        py + ny * offset);

                centerLuma =
                    Math.Max(
                        centerLuma,
                        GetNeutralLuma(
                            frame,
                            sx,
                            sy,
                            50,
                            120));
            }

            int sideTotal = 0;
            int sideCount = 0;

            foreach (int offset
                     in new[]
                     {
                         -10,
                         -7,
                         7,
                         10
                     })
            {
                int sx =
                    (int)Math.Round(
                        px + nx * offset);

                int sy =
                    (int)Math.Round(
                        py + ny * offset);

                sideTotal +=
                    GetRawLuma(
                        frame,
                        sx,
                        sy);

                sideCount++;
            }

            double sideAverage =
                sideCount > 0
                    ? sideTotal
                      / (double)sideCount
                    : 0d;

            double contrast =
                centerLuma
                - sideAverage;

            samples++;

            if (centerLuma >= 55
                && (contrast >= 6d
                    || centerLuma >= 155))
            {
                hits++;
            }
        }

        if (samples < 4)
        {
            return false;
        }

        support =
            hits / (double)samples;

        return support >= 0.34d;
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
                (int)Math.Round(
                    11d * scale),
                8,
                15);

        double weightedX = 0d;
        double weightedY = 0d;
        double weight = 0d;

        int roundedX =
            (int)Math.Round(
                centerX);

        int roundedY =
            (int)Math.Round(
                centerY);

        for (int y = roundedY - radius;
             y <= roundedY + radius;
             y++)
        {
            for (int x = roundedX - radius;
                 x <= roundedX + radius;
                 x++)
            {
                double dx =
                    x - centerX;

                double dy =
                    y - centerY;

                if (dx * dx
                    + dy * dy
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
                    Math.Max(
                        1,
                        luma - 90);

                weightedX +=
                    x * localWeight;

                weightedY +=
                    y * localWeight;

                weight +=
                    localWeight;
            }
        }

        return weight > 0d
            ? (
                weightedX / weight,
                weightedY / weight)
            : (
                centerX,
                centerY);
    }

    private static double MeasureRawCoreMean(
        DssCapturedFrame frame,
        int x,
        int y,
        double scale)
    {
        int radius =
            Math.Clamp(
                (int)Math.Round(
                    scale),
                1,
                2);

        int samples = 0;
        double sum = 0d;

        for (int oy = -radius;
             oy <= radius;
             oy++)
        {
            for (int ox = -radius;
                 ox <= radius;
                 ox++)
            {
                sum +=
                    GetRawLuma(
                        frame,
                        x + ox,
                        y + oy);

                samples++;
            }
        }

        return samples > 0
            ? sum / samples
            : 0d;
    }

    private static int GetNeutralLuma(
        DssCapturedFrame frame,
        int x,
        int y,
        int minimumLuma,
        int maximumSpread)
    {
        if ((uint)x
                >= (uint)frame.Width
            || (uint)y
               >= (uint)frame.Height)
        {
            return 0;
        }

        int index =
            y * frame.Stride
            + x * 4;

        int blue =
            frame.Bgra32[index];

        int green =
            frame.Bgra32[index + 1];

        int red =
            frame.Bgra32[index + 2];

        int maximum =
            Math.Max(
                red,
                Math.Max(
                    green,
                    blue));

        int minimum =
            Math.Min(
                red,
                Math.Min(
                    green,
                    blue));

        int luma =
            (
                red * 54
                + green * 183
                + blue * 19) >> 8;

        return luma >= minimumLuma
               && maximum - minimum
                  <= maximumSpread
            ? luma
            : 0;
    }

    private static int GetRawLuma(
        DssCapturedFrame frame,
        int x,
        int y)
    {
        if ((uint)x
                >= (uint)frame.Width
            || (uint)y
               >= (uint)frame.Height)
        {
            return 0;
        }

        int index =
            y * frame.Stride
            + x * 4;

        int blue =
            frame.Bgra32[index];

        int green =
            frame.Bgra32[index + 1];

        int red =
            frame.Bgra32[index + 2];

        return (
            red * 54
            + green * 183
            + blue * 19) >> 8;
    }

    private static double Distance(
        double ax,
        double ay,
        double bx,
        double by)
    {
        double dx =
            ax - bx;

        double dy =
            ay - by;

        return Math.Sqrt(
            dx * dx
            + dy * dy);
    }

    private static double Speed(
        double vx,
        double vy)
    {
        if (!double.IsFinite(vx)
            || !double.IsFinite(vy))
        {
            return 0d;
        }

        return Math.Sqrt(
            vx * vx
            + vy * vy);
    }

    private static double FiniteOrZero(
        double value) =>
        double.IsFinite(value)
            ? value
            : 0d;

    internal readonly record struct NativeCenterMarker(
        double X,
        double Y,
        double Confidence,
        double Error,
        double Score)
    {
        internal static NativeCenterMarker Empty =>
            new(
                0d,
                0d,
                0d,
                double.PositiveInfinity,
                double.NegativeInfinity);
    }
}
