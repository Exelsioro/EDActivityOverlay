using System;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Shared continuity rules for texture-based DSS centre tracking.
///
/// The native DSS body contains repeated grid/coverage patterns. A pure SAD
/// matcher can therefore find a low-error patch several pixels away even when
/// the camera is completely still. These rules distinguish that texture noise
/// from real screen motion without adding an EMA to the displayed position.
/// </summary>
internal static class DssVisualMotionPolicy
{
    internal const double StationaryAnchorSpeedPixelsPerSecond = 45d;
    internal const double StationaryTrackSpeedPixelsPerSecond = 80d;

    // Research run 20260827-015317565 showed the true LOCAL centre stable near
    // (1303.7, 684.0), while IMAGE intermittently accepted (1309, 686).
    // Normal sub-pixel / one-pixel detector noise is much smaller than this.
    internal const double StationaryHoldRadiusPixels = 2.25d;

    internal const double MovingDeadbandPixels = 1.25d;

    internal static bool IsStationary(
        double anchorVelocityX,
        double anchorVelocityY,
        double trackVelocityX,
        double trackVelocityY)
    {
        double anchorSpeed =
            Speed(
                anchorVelocityX,
                anchorVelocityY);

        double trackSpeed =
            Speed(
                trackVelocityX,
                trackVelocityY);

        return anchorSpeed
                   <= StationaryAnchorSpeedPixelsPerSecond
               && trackSpeed
                  <= StationaryTrackSpeedPixelsPerSecond;
    }

    internal static int ResolveSearchRadius(
        bool stationary,
        double speedPixelsPerSecond,
        double dtSeconds)
    {
        if (stationary)
        {
            // A wide +/-18..48 px search at rest is actively harmful on the
            // repeated DSS grid. Eight pixels is enough to detect that motion
            // has started; a rejected onset frame falls through to heavy CV.
            return 8;
        }

        return Math.Clamp(
            18
            + (int)Math.Ceiling(
                Math.Max(
                    0d,
                    speedPixelsPerSecond)
                * Math.Max(
                    0d,
                    dtSeconds)),
            18,
            48);
    }

    internal static double ResolveMaximumInnovationPixels(
        bool stationary,
        double speedPixelsPerSecond,
        double dtSeconds)
    {
        if (stationary)
        {
            return StationaryHoldRadiusPixels;
        }

        // Allow prediction error during acceleration, but never let a repeated
        // texture cell pull the track across most of the search window.
        return Math.Clamp(
            6.5d
            + Math.Max(
                0d,
                speedPixelsPerSecond)
              * Math.Max(
                  0d,
                  dtSeconds)
              * 0.75d,
            6.5d,
            24d);
    }

    internal static double Distance(
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

    internal static double Speed(
        double velocityX,
        double velocityY)
    {
        if (!double.IsFinite(
                velocityX)
            || !double.IsFinite(
                velocityY))
        {
            return 0d;
        }

        return Math.Sqrt(
            velocityX * velocityX
            + velocityY * velocityY);
    }
}
