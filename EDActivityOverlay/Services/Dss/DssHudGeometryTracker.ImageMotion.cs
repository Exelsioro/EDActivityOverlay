using System;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Fast image-motion path layered on top of the conservative absolute DSS
/// geometry tracker.
///
/// Heavy C/H detection remains authoritative. After a good heavy anchor, up to
/// two captured frames are allowed to use the existing annulus texture matcher
/// around the planet centre. The third frame is forced back through heavy CV.
///
/// This has two purposes:
/// 1) move C using pixels from the current capture instead of waiting for the
///    expensive marker/horizon detector on every frame;
/// 2) keep C alive when the native reticle temporarily hides the centre marker.
///
/// Image tracking never replaces Rh. The last trusted horizon radius remains
/// owned by the heavy tracker and is only projected from the image-tracked C.
/// </summary>
internal sealed partial class DssHudGeometryTracker
{
    private const int MaximumConsecutiveImageMotionFrames = 2;
    private const double MinimumImageMotionConfidence = 0.58d;
    private const double ImageMotionJitterDeadbandPixels = 1.25d;
    private const double MaximumImageMotionVelocityPixelsPerSecond = 1800d;

    private static readonly TimeSpan RecentImageTrackWindow =
        TimeSpan.FromMilliseconds(420);

    private readonly DssCenterImageTracker imageMotionTracker =
        new();

    // Start at the budget so the first frame must establish a heavy anchor.
    private int consecutiveImageMotionFrames =
        MaximumConsecutiveImageMotionFrames;

    private DateTimeOffset lastImageMotionUtc =
        DateTimeOffset.MinValue;

    private void ResetImageMotionTracking()
    {
        imageMotionTracker.Reset();

        consecutiveImageMotionFrames =
            MaximumConsecutiveImageMotionFrames;

        lastImageMotionUtc =
            DateTimeOffset.MinValue;
    }

    private bool TryProcessImageMotionFrame(
        DssCapturedFrame frame,
        double verticalFovDegrees,
        DateTimeOffset timestampUtc,
        out DssHudTrackResult result)
    {
        result = default!;

        // Do not let the texture tracker bootstrap geometry. It is a fast path
        // only after both C and Rh have already been trusted by heavy CV.
        if (!hasTrustedCenter
            || !hasTrustedHorizon
            || lastCenterObservedUtc
               == DateTimeOffset.MinValue
            || consecutiveImageMotionFrames
               >= MaximumConsecutiveImageMotionFrames)
        {
            return false;
        }

        double ageSeconds =
            Math.Clamp(
                (timestampUtc
                 - lastCenterObservedUtc)
                .TotalSeconds,
                0d,
                0.20d);

        double predictedX =
            centerX
            + velocityX * ageSeconds;

        double predictedY =
            centerY
            + velocityY * ageSeconds;

        if (!imageMotionTracker.TryTrack(
                frame,
                predictedX,
                predictedY,
                out DssImageTrackResult? image)
            || image is null
            || image.Confidence
               < MinimumImageMotionConfidence)
        {
            // Fail closed: run the authoritative detector on this same frame.
            consecutiveImageMotionFrames =
                MaximumConsecutiveImageMotionFrames;

            return false;
        }

        double dt =
            (timestampUtc
             - lastCenterObservedUtc)
            .TotalSeconds;

        if (dt > 0.01d
            && dt < 0.32d)
        {
            UpdateDirectVelocity(
                image.CenterX,
                image.CenterY,
                dt);
        }
        else
        {
            velocityX = 0;
            velocityY = 0;
        }

        centerX =
            image.CenterX;

        centerY =
            image.CenterY;

        lastCenterObservedUtc =
            timestampUtc;

        hasTrustedCenter = true;
        hasHistoricalCenter = true;
        localMisses = 0;

        consecutiveImageMotionFrames++;
        lastImageMotionUtc =
            timestampUtc;

        DssHudGeometry geometry =
            GeometryForCenter(
                frame,
                verticalFovDegrees,
                centerX,
                centerY,
                Math.Max(
                    0.66d,
                    image.Confidence));

        geometry =
            BuildHorizonGeometry(
                geometry,
                timestampUtc,
                DssHorizonTrackState.Predicting);

        result =
            new DssHudTrackResult(
                geometry,
                DssCenterTrackState.Tracking,
                DssHorizonTrackState.Predicting,
                "IMAGE",
                velocityX,
                velocityY,
                false,
                false,
                image.Confidence);

        return true;
    }

    private void UpdateImageMotionAnchor(
        DssCapturedFrame frame,
        DssHudGeometry raw,
        DssCenterTrackState centerState,
        DateTimeOffset timestampUtc)
    {
        bool goodHeavyCenter =
            centerState
                == DssCenterTrackState.Tracking
            && raw.BodyCenterFound
            && raw.BodyCenterConfidence >= 0.66d
            && hasTrustedCenter;

        if (goodHeavyCenter)
        {
            imageMotionTracker.CaptureTemplate(
                frame,
                centerX,
                centerY);

            consecutiveImageMotionFrames = 0;
            return;
        }

        bool recentImageTrack =
            lastImageMotionUtc
                != DateTimeOffset.MinValue
            && timestampUtc
               - lastImageMotionUtc
               <= RecentImageTrackWindow
            && hasTrustedCenter
            && hasTrustedHorizon;

        if (recentImageTrack)
        {
            // A heavy frame may fail exactly when the native reticle obscures
            // C. Permit another short image bridge, then force heavy CV again.
            consecutiveImageMotionFrames = 0;
            return;
        }

        consecutiveImageMotionFrames =
            MaximumConsecutiveImageMotionFrames;
    }

    private void UpdateDirectVelocity(
        double nextX,
        double nextY,
        double dt)
    {
        double dx =
            nextX - centerX;

        double dy =
            nextY - centerY;

        double distance =
            Math.Sqrt(
                dx * dx
                + dy * dy);

        if (distance
            <= ImageMotionJitterDeadbandPixels)
        {
            velocityX = 0;
            velocityY = 0;
            return;
        }

        double measuredVelocityX =
            dx / dt;

        double measuredVelocityY =
            dy / dt;

        double speed =
            Math.Sqrt(
                measuredVelocityX
                * measuredVelocityX
                + measuredVelocityY
                  * measuredVelocityY);

        if (!double.IsFinite(speed)
            || speed <= 0d)
        {
            velocityX = 0;
            velocityY = 0;
            return;
        }

        if (speed
            > MaximumImageMotionVelocityPixelsPerSecond)
        {
            double scale =
                MaximumImageMotionVelocityPixelsPerSecond
                / speed;

            measuredVelocityX *= scale;
            measuredVelocityY *= scale;
        }

        // Deliberately no EMA here. An EMA on velocity was the source of the
        // visible "crawl" after the player stopped moving the camera.
        velocityX =
            measuredVelocityX;

        velocityY =
            measuredVelocityY;
    }
}
