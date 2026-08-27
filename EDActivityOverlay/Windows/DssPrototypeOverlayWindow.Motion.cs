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
        dynamicHudTranslation.X = 0d;
        dynamicHudTranslation.Y = 0d;

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
            return;
        }

        dynamicHudBaseCenterX =
            geometry.BodyCenterX
            * scaleX;

        dynamicHudBaseCenterY =
            geometry.BodyCenterY
            * scaleY;

        // Tracking includes both heavy CV anchors and the IMAGE fast path.
        // Predicting already contains tracker-side extrapolation, so do not
        // stack a second extrapolator on top of it.
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
    }

    private void OnDynamicHudRendering(
        object? sender,
        EventArgs e)
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

        double ageSeconds =
            Math.Clamp(
                (DateTimeOffset.UtcNow
                 - dynamicHudAnchorFrameUtc)
                .TotalSeconds,
                0d,
                DynamicHudMaximumPredictionSeconds);

        (
            double dx,
            double dy) =
            CalculateDynamicHudTranslation(
                dynamicHudVelocityX,
                dynamicHudVelocityY,
                ageSeconds);

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

        // Aim markers/halo/label are recreated at CV cadence. Sharing the same
        // render transform keeps them locked to C between those redraws.
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
