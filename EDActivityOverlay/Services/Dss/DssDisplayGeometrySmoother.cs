using System;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Zero-lag display stabilizer for DSS geometry.
///
/// v31 deliberately rendered ~132 ms in the past. Combined with the real
/// capture + CV cost (~80 ms median in the 7/7 run), that made the overlay
/// visibly trail the live planet and then "crawl" into place after the camera
/// slowed.
///
/// v32 does the opposite:
/// - render the newest tracker measurement immediately;
/// - use the last two measured centres only to compensate the processing age
///   of the current frame (small forward projection, capped at 12 px);
/// - remove compensation instantly when the measured motion stops;
/// - suppress only sub-pixel / ~1 px stationary jitter;
/// - hold display geometry through the characteristic centre-reticle
///   occlusion without changing raw CV/readiness telemetry.
/// </summary>
internal sealed class DssDisplayGeometrySmoother
{
    internal static readonly TimeSpan GenericDropoutHold =
        TimeSpan.FromMilliseconds(900);

    internal static readonly TimeSpan CenterOcclusionHold =
        TimeSpan.FromSeconds(12);

    private const double CenterOcclusionAimOffsetDegrees = 2.5d;
    private const double StationaryDeadbandPixels = 1.25d;
    private const double MinimumMotionSpeedPixelsPerSecond = 14d;
    private const double MaximumMeasuredSpeedPixelsPerSecond = 1800d;
    private const double MaximumLatencyCompensationSeconds = 0.11d;
    private const double MaximumLatencyCompensationPixels = 12d;

    private Sample? lastRaw;
    private DssHudGeometry? lastDisplay;
    private DateTimeOffset lastObservedUtc =
        DateTimeOffset.MinValue;
    private double lastObservedAimOffsetDegrees;

    public void Reset()
    {
        lastRaw = null;
        lastDisplay = null;
        lastObservedUtc =
            DateTimeOffset.MinValue;
        lastObservedAimOffsetDegrees = 0;
    }

    public DssHudGeometry Update(
        DateTimeOffset frameUtc,
        DssHudGeometry raw) =>
        Update(
            frameUtc,
            DateTimeOffset.UtcNow,
            raw);

    internal DssHudGeometry Update(
        DateTimeOffset frameUtc,
        DateTimeOffset renderUtc,
        DssHudGeometry raw)
    {
        if (raw.BodyCenterFound)
        {
            DssHudGeometry stabilized =
                StabilizeObserved(
                    frameUtc,
                    renderUtc,
                    raw);

            lastRaw =
                new Sample(
                    frameUtc,
                    raw);

            lastDisplay = stabilized;
            lastObservedUtc = frameUtc;
            lastObservedAimOffsetDegrees =
                raw.AimOffsetDegrees;

            return stabilized;
        }

        if (lastDisplay is null
            || lastObservedUtc
               == DateTimeOffset.MinValue)
        {
            return raw;
        }

        TimeSpan age =
            frameUtc - lastObservedUtc;

        bool centreReticleOcclusion =
            lastObservedAimOffsetDegrees
            <= CenterOcclusionAimOffsetDegrees;

        TimeSpan allowed =
            centreReticleOcclusion
                ? CenterOcclusionHold
                : GenericDropoutHold;

        if (age >= TimeSpan.Zero
            && age <= allowed)
        {
            return lastDisplay;
        }

        return raw;
    }

    private DssHudGeometry StabilizeObserved(
        DateTimeOffset frameUtc,
        DateTimeOffset renderUtc,
        DssHudGeometry raw)
    {
        double centerX =
            raw.BodyCenterX;

        double centerY =
            raw.BodyCenterY;

        double radius =
            ResolveDisplayRadius(
                raw);

        bool hasHorizon =
            raw.HorizonMarkerFound
            && raw.HorizonRadiusPixels > 25;

        if (!hasHorizon
            && lastDisplay is not null
            && lastDisplay.HorizonMarkerFound
            && lastDisplay.HorizonRadiusPixels > 25)
        {
            // Visual continuity only. The raw tracker/readiness pipeline still
            // knows that H is not currently observable.
            radius =
                lastDisplay.HorizonRadiusPixels;
            hasHorizon = true;
        }

        if (lastRaw is not null)
        {
            double dt =
                (frameUtc - lastRaw.Utc)
                    .TotalSeconds;

            if (dt >= 0.025d
                && dt <= 0.32d)
            {
                double measuredDx =
                    raw.BodyCenterX
                    - lastRaw.Geometry.BodyCenterX;

                double measuredDy =
                    raw.BodyCenterY
                    - lastRaw.Geometry.BodyCenterY;

                double measuredDistance =
                    Math.Sqrt(
                        measuredDx * measuredDx
                        + measuredDy * measuredDy);

                double speed =
                    measuredDistance / dt;

                if (lastDisplay is not null)
                {
                    double fromDisplayX =
                        raw.BodyCenterX
                        - lastDisplay.BodyCenterX;

                    double fromDisplayY =
                        raw.BodyCenterY
                        - lastDisplay.BodyCenterY;

                    double fromDisplay =
                        Math.Sqrt(
                            fromDisplayX * fromDisplayX
                            + fromDisplayY * fromDisplayY);

                    if (speed
                            < MinimumMotionSpeedPixelsPerSecond
                        && fromDisplay
                           <= StationaryDeadbandPixels)
                    {
                        // True dead-band, not a low-pass filter. There is no
                        // residual error that has to be paid back later.
                        centerX =
                            lastDisplay.BodyCenterX;
                        centerY =
                            lastDisplay.BodyCenterY;
                    }
                }

                if (speed
                    >= MinimumMotionSpeedPixelsPerSecond)
                {
                    double velocityX =
                        measuredDx / dt;

                    double velocityY =
                        measuredDy / dt;

                    if (speed
                        > MaximumMeasuredSpeedPixelsPerSecond)
                    {
                        double scale =
                            MaximumMeasuredSpeedPixelsPerSecond
                            / speed;

                        velocityX *= scale;
                        velocityY *= scale;
                    }

                    double latencySeconds =
                        Math.Clamp(
                            (renderUtc - frameUtc)
                                .TotalSeconds,
                            0d,
                            MaximumLatencyCompensationSeconds);

                    double compensationX =
                        velocityX
                        * latencySeconds;

                    double compensationY =
                        velocityY
                        * latencySeconds;

                    double compensation =
                        Math.Sqrt(
                            compensationX * compensationX
                            + compensationY * compensationY);

                    if (compensation
                            > MaximumLatencyCompensationPixels
                        && compensation > 0)
                    {
                        double scale =
                            MaximumLatencyCompensationPixels
                            / compensation;

                        compensationX *= scale;
                        compensationY *= scale;
                    }

                    centerX =
                        raw.BodyCenterX
                        + compensationX;

                    centerY =
                        raw.BodyCenterY
                        + compensationY;
                }
            }
        }

        return RebuildGeometry(
            raw,
            centerX,
            centerY,
            radius,
            hasHorizon);
    }

    private double ResolveDisplayRadius(
        DssHudGeometry raw)
    {
        if (raw.HorizonMarkerFound
            && raw.HorizonRadiusPixels > 25)
        {
            if (lastDisplay is not null
                && lastDisplay.HorizonMarkerFound
                && Math.Abs(
                    raw.HorizonRadiusPixels
                    - lastDisplay.HorizonRadiusPixels)
                   < 0.85d)
            {
                return
                    lastDisplay.HorizonRadiusPixels;
            }

            return raw.HorizonRadiusPixels;
        }

        return lastDisplay?.HorizonRadiusPixels
               ?? raw.HorizonRadiusPixels;
    }

    private static DssHudGeometry RebuildGeometry(
        DssHudGeometry basis,
        double centerX,
        double centerY,
        double horizonRadius,
        bool hasHorizon)
    {
        if (!hasHorizon
            || horizonRadius <= 25)
        {
            return basis with
            {
                BodyCenterFound = true,
                BodyCenterX = centerX,
                BodyCenterY = centerY
            };
        }

        double vx =
            basis.ReticleX - centerX;

        double vy =
            basis.ReticleY - centerY;

        double aimRadius =
            Math.Sqrt(
                vx * vx
                + vy * vy);

        double ux =
            aimRadius > 0.5d
                ? vx / aimRadius
                : 0d;

        double uy =
            aimRadius > 0.5d
                ? vy / aimRadius
                : -1d;

        return basis with
        {
            BodyCenterFound = true,
            BodyCenterX = centerX,
            BodyCenterY = centerY,
            HorizonMarkerFound = true,
            HorizonMarkerX =
                centerX
                + ux * horizonRadius,
            HorizonMarkerY =
                centerY
                + uy * horizonRadius,
            HorizonRadiusPixels =
                horizonRadius,
            HorizonAimErrorPixels =
                aimRadius - horizonRadius
        };
    }

    private sealed record Sample(
        DateTimeOffset Utc,
        DssHudGeometry Geometry);
}
