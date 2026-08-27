using System;
using System.Windows;
using System.Windows.Media;
using EDActivityOverlay.Services.Dss;

namespace EDActivityOverlay.Windows;

/// <summary>
/// Composition-rate visual pose for the DSS overlay.
///
/// CV/layout still supplies absolute positions. This class only translates
/// body-relative visuals from the capture timestamp toward "now" between CV
/// updates. It never feeds predicted coordinates back into MISS, coverage,
/// readiness or shot logic.
/// </summary>
public partial class DssPrototypeOverlayWindow
{
    private const double DynamicHudMaximumPredictionSeconds = 0.18d;
    private const double DynamicHudMaximumPredictionPixels = 96d;

    // Fast WGC texture tracking supplies a measured centre, so only the small
    // residual interval from that frame to composition needs extrapolation.
    private const double FastVisualMaximumPredictionSeconds = 0.08d;
    private const double FastVisualMaximumPredictionPixels = 48d;
    private const double FastVisualMaximumAgeSeconds = 0.25d;

    private readonly TranslateTransform dynamicHudTranslation =
        new();

    private DateTimeOffset dynamicHudAnchorFrameUtc =
        DateTimeOffset.MinValue;

    private double dynamicHudVelocityX;
    private double dynamicHudVelocityY;
    private double dynamicHudScaleX = 1d;
    private double dynamicHudScaleY = 1d;
    private double dynamicHudBaseCenterX;
    private double dynamicHudBaseCenterY;
    private double dynamicHudBaseCaptureCenterX;
    private double dynamicHudBaseCaptureCenterY;

    private DateTimeOffset dynamicHudFastFrameUtc =
        DateTimeOffset.MinValue;

    private double dynamicHudFastCenterX;
    private double dynamicHudFastCenterY;
    private double dynamicHudFastVelocityX;
    private double dynamicHudFastVelocityY;
    private bool dynamicHudFastAvailable;

    private double dynamicHudReticleX;
    private double dynamicHudReticleY;
    private double dynamicHudHorizonRadius;
    private bool dynamicHudCenterAvailable;
    private bool dynamicHudHorizonAvailable;

    private void InitializeDynamicHudMotion()
    {
        DetectedBodyCenter.RenderTransform =
            dynamicHudTranslation;

        DetectedHorizonCircle.RenderTransform =
            dynamicHudTranslation;

        CompositionTarget.Rendering +=
            OnDynamicHudRendering;

        Closed +=
            OnDynamicHudClosed;
    }

    private void OnDynamicHudClosed(
        object? sender,
        EventArgs e)
    {
        CompositionTarget.Rendering -=
            OnDynamicHudRendering;
    }

    private void UpdateDynamicHudMotionFrame(
        DateTimeOffset frameUtc,
        DssHudTrackResult tracking,
        DssHudGeometry geometry,
        double scaleX,
        double scaleY,
        double reticleX,
        double reticleY)
    {
        // Do not zero dynamicHudTranslation here. UpdateGeometry runs at
        // DispatcherPriority.Render; clearing the shared transform before the
        // next CompositionTarget.Rendering tick can expose one stale BASE pose
        // between FAST measurements and creates visible two-axis chatter.

        dynamicHudAnchorFrameUtc =
            frameUtc;

        dynamicHudScaleX =
            scaleX;

        dynamicHudScaleY =
            scaleY;

        dynamicHudReticleX =
            reticleX;

        dynamicHudReticleY =
            reticleY;

        dynamicHudCenterAvailable =
            geometry.BodyCenterFound;

        if (!dynamicHudCenterAvailable)
        {
            dynamicHudVelocityX = 0d;
            dynamicHudVelocityY = 0d;
            dynamicHudHorizonAvailable = false;
            dynamicHudFastAvailable = false;

            dynamicHudTranslation.X = 0d;
            dynamicHudTranslation.Y = 0d;
            return;
        }

        dynamicHudBaseCenterX =
            geometry.BodyCenterX
            * scaleX;

        dynamicHudBaseCenterY =
            geometry.BodyCenterY
            * scaleY;

        dynamicHudBaseCaptureCenterX =
            geometry.BodyCenterX;

        dynamicHudBaseCaptureCenterY =
            geometry.BodyCenterY;

        // BASE and FAST are separate sources. A main LOCAL/IMAGE result is
        // never promoted into FAST state. If BASE has caught up with or passed
        // the last independent FAST frame, that FAST sample is simply stale
        // and the visual path falls back to the main tracker until a genuinely
        // newer independent WGC measurement arrives.
        if (dynamicHudFastAvailable
            && frameUtc >= dynamicHudFastFrameUtc)
        {
            dynamicHudFastAvailable =
                false;
        }

        // Predicting already contains tracker-side extrapolation, so do not
        // stack a second heavy extrapolator on top of it.
        if (tracking.CenterState
            == DssCenterTrackState.Tracking)
        {
            dynamicHudVelocityX =
                tracking.CenterVelocityX;

            dynamicHudVelocityY =
                tracking.CenterVelocityY;
        }
        else
        {
            dynamicHudVelocityX = 0d;
            dynamicHudVelocityY = 0d;
        }

        dynamicHudHorizonAvailable =
            geometry.HorizonMarkerFound
            && geometry.HorizonRadiusPixels > 25d;

        dynamicHudHorizonRadius =
            geometry.HorizonRadiusPixels
            * ((scaleX + scaleY) / 2d);

        // Rebase the visual transform in the same Dispatcher callback that
        // changed Canvas geometry. WPF never gets a composition opportunity
        // with a new BASE layout plus an old/zero translation.
        ApplyDynamicHudPose(
            DateTimeOffset.UtcNow);
    }

    internal void UpdateFastVisualMotion(
        DssFastVisualMotionSnapshot motion)
    {
        if (!dynamicHudCenterAvailable
            || motion.FrameWidth < 1
            || motion.FrameHeight < 1
            || motion.TimestampUtc
               <= dynamicHudAnchorFrameUtc
            || (dynamicHudFastAvailable
                && motion.TimestampUtc
                   <= dynamicHudFastFrameUtc))
        {
            return;
        }

        dynamicHudFastFrameUtc =
            motion.TimestampUtc;

        dynamicHudFastCenterX =
            motion.CenterX;

        dynamicHudFastCenterY =
            motion.CenterY;

        dynamicHudFastVelocityX =
            motion.VelocityX;

        dynamicHudFastVelocityY =
            motion.VelocityY;

        dynamicHudFastAvailable =
            true;

        dynamicHudScaleX =
            ActualWidth > 0
                ? ActualWidth
                  / motion.FrameWidth
                : 1d;

        dynamicHudScaleY =
            ActualHeight > 0
                ? ActualHeight
                  / motion.FrameHeight
                : 1d;

        // The FAST update already runs on the WPF dispatcher. Apply it
        // immediately instead of waiting for a later composition callback.
        ApplyDynamicHudPose(
            DateTimeOffset.UtcNow);
    }

    private void OnDynamicHudRendering(
        object? sender,
        EventArgs e)
    {
        ApplyDynamicHudPose(
            DateTimeOffset.UtcNow);
    }

    private void ApplyDynamicHudPose(
        DateTimeOffset now)
    {
        if (!IsVisible
            || !dynamicHudCenterAvailable
            || dynamicHudAnchorFrameUtc
               == DateTimeOffset.MinValue)
        {
            dynamicHudTranslation.X = 0d;
            dynamicHudTranslation.Y = 0d;
            return;
        }

        double dx;
        double dy;

        double fastAgeSeconds =
            dynamicHudFastAvailable
                ? (now
                   - dynamicHudFastFrameUtc)
                  .TotalSeconds
                : double.PositiveInfinity;

        bool useFast =
            dynamicHudFastAvailable
            && dynamicHudFastFrameUtc
               > dynamicHudAnchorFrameUtc
            && fastAgeSeconds >= 0d
            && fastAgeSeconds
               <= FastVisualMaximumAgeSeconds;

        if (useFast)
        {
            (
                double residualX,
                double residualY) =
                CalculateFastVisualResidual(
                    dynamicHudFastVelocityX,
                    dynamicHudFastVelocityY,
                    fastAgeSeconds);

            // Measured FAST displacement is intentionally not capped.
            dx =
                dynamicHudFastCenterX
                - dynamicHudBaseCaptureCenterX
                + residualX;

            dy =
                dynamicHudFastCenterY
                - dynamicHudBaseCaptureCenterY
                + residualY;
        }
        else
        {
            double ageSeconds =
                Math.Clamp(
                    (now
                     - dynamicHudAnchorFrameUtc)
                    .TotalSeconds,
                    0d,
                    DynamicHudMaximumPredictionSeconds);

            (
                dx,
                dy) =
                CalculateDynamicHudTranslation(
                    dynamicHudVelocityX,
                    dynamicHudVelocityY,
                    ageSeconds);
        }

        double renderDx =
            dx * dynamicHudScaleX;

        double renderDy =
            dy * dynamicHudScaleY;

        dynamicHudTranslation.X =
            renderDx;

        dynamicHudTranslation.Y =
            renderDy;

        double centerX =
            dynamicHudBaseCenterX
            + renderDx;

        double centerY =
            dynamicHudBaseCenterY
            + renderDy;

        if (CenterGuide.Visibility
            == Visibility.Visible)
        {
            // Reticle endpoint is screen-fixed.
            CenterGuide.X2 =
                centerX;

            CenterGuide.Y2 =
                centerY;
        }

        if (dynamicHudHorizonAvailable
            && DetectedHorizonDash.Visibility
               == Visibility.Visible)
        {
            // The dash is not rigidly attached to the circle: it lies on the
            // radial line from current C to the fixed native reticle.
            double vx =
                dynamicHudReticleX
                - centerX;

            double vy =
                dynamicHudReticleY
                - centerY;

            double length =
                Math.Sqrt(
                    vx * vx
                    + vy * vy);

            if (length > 1d)
            {
                double ux =
                    vx / length;

                double uy =
                    vy / length;

                double markerX =
                    centerX
                    + ux
                      * dynamicHudHorizonRadius;

                double markerY =
                    centerY
                    + uy
                      * dynamicHudHorizonRadius;

                double nx =
                    -uy;

                double ny =
                    ux;

                const double half =
                    10d;

                DetectedHorizonDash.X1 =
                    markerX
                    - nx * half;

                DetectedHorizonDash.Y1 =
                    markerY
                    - ny * half;

                DetectedHorizonDash.X2 =
                    markerX
                    + nx * half;

                DetectedHorizonDash.Y2 =
                    markerY
                    + ny * half;
            }
        }

        foreach (FrameworkElement element
                 in aimPointElements)
        {
            if (!ReferenceEquals(
                    element.RenderTransform,
                    dynamicHudTranslation))
            {
                element.RenderTransform =
                    dynamicHudTranslation;
            }
        }
    }

    internal static (
        double X,
        double Y)
        CalculateFastVisualResidual(
            double velocityX,
            double velocityY,
            double ageSeconds)
    {
        double clampedAge =
            Math.Clamp(
                ageSeconds,
                0d,
                FastVisualMaximumPredictionSeconds);

        double dx =
            velocityX
            * clampedAge;

        double dy =
            velocityY
            * clampedAge;

        double distance =
            Math.Sqrt(
                dx * dx
                + dy * dy);

        if (distance
                > FastVisualMaximumPredictionPixels
            && distance > 0d)
        {
            double scale =
                FastVisualMaximumPredictionPixels
                / distance;

            dx *= scale;
            dy *= scale;
        }

        return (
            dx,
            dy);
    }

    internal static (
        double X,
        double Y)
        CalculateDynamicHudTranslation(
            double velocityX,
            double velocityY,
            double ageSeconds)
    {
        double clampedAge =
            Math.Clamp(
                ageSeconds,
                0d,
                DynamicHudMaximumPredictionSeconds);

        double dx =
            velocityX
            * clampedAge;

        double dy =
            velocityY
            * clampedAge;

        double distance =
            Math.Sqrt(
                dx * dx
                + dy * dy);

        if (distance
                > DynamicHudMaximumPredictionPixels
            && distance > 0d)
        {
            double scale =
                DynamicHudMaximumPredictionPixels
                / distance;

            dx *= scale;
            dy *= scale;
        }

        return (
            dx,
            dy);
    }
}
