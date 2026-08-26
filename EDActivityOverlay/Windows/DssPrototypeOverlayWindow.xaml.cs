using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class DssPrototypeOverlayWindow : Window
{
    private const uint WsExToolWindow = 0x00000080;
    private const uint WdaExcludeFromCapture = 0x00000011;

    private readonly IntPtr targetWindow;

    private readonly List<FrameworkElement> aimPointElements =
        new();

    private const double AimPointDiameter = 22d;

    internal DssPrototypeOverlayWindow(
        IntPtr targetWindow)
    {
        this.targetWindow = targetWindow;
        InitializeComponent();

        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;

        SourceInitialized += (_, _) =>
            ApplyPassiveWindowStyles();

        Loaded += (_, _) =>
        {
            ApplyPassiveWindowStyles();
            WindowsAPI.SetTopmost(this, true);
            SyncToTarget();
        };
    }

    internal void ShowPassive()
    {
        if (!IsVisible)
        {
            Show();
        }

        ApplyPassiveWindowStyles();
        WindowsAPI.SetTopmost(this, true);
        SyncToTarget();
    }

    internal void SetContext(
        GameStateSnapshot state,
        DssPrototypeSessionContext context,
        string sessionId)
    {
        DebugText.Text =
            $"DSS PROTOTYPE  {sessionId}\n" +
            $"BODY  {DisplayBody(state, context)}\n" +
            $"FOV   {context.VerticalFovDegrees:0.###}°   " +
            $"PATCH {DisplayPatch(context)}";
    }

    internal void UpdateGeometry(
        DssCapturedFrame frame,
        DssHudTrackResult tracking,
        DssAssistantReadinessSnapshot readiness,
        GameStateSnapshot state,
        DssPrototypeSessionContext context,
        long sequence)
    {
        DssHudGeometry geometry = tracking.Geometry;

        SyncToTarget();
        UpdateReadinessPanel(readiness);

        double scaleX = ActualWidth > 0
            ? ActualWidth / frame.Width
            : 1d;
        double scaleY = ActualHeight > 0
            ? ActualHeight / frame.Height
            : 1d;

        double reticleX =
            geometry.ReticleX * scaleX;
        double reticleY =
            geometry.ReticleY * scaleY;

        ReticleHorizontal.X1 = reticleX - 12;
        ReticleHorizontal.X2 = reticleX + 12;
        ReticleHorizontal.Y1 =
            ReticleHorizontal.Y2 = reticleY;

        ReticleVertical.X1 =
            ReticleVertical.X2 = reticleX;
        ReticleVertical.Y1 = reticleY - 12;
        ReticleVertical.Y2 = reticleY + 12;

        if (geometry.BodyCenterFound)
        {
            double centerX =
                geometry.BodyCenterX * scaleX;
            double centerY =
                geometry.BodyCenterY * scaleY;

            DetectedBodyCenter.Visibility =
                Visibility.Visible;
            Canvas.SetLeft(
                DetectedBodyCenter,
                centerX
                - DetectedBodyCenter.Width / 2);
            Canvas.SetTop(
                DetectedBodyCenter,
                centerY
                - DetectedBodyCenter.Height / 2);

            CenterGuide.Visibility =
                Visibility.Visible;
            CenterGuide.X1 = reticleX;
            CenterGuide.Y1 = reticleY;
            CenterGuide.X2 = centerX;
            CenterGuide.Y2 = centerY;

            if (geometry.HorizonMarkerFound)
            {
                double radiusX =
                    geometry.HorizonRadiusPixels
                    * scaleX;
                double radiusY =
                    geometry.HorizonRadiusPixels
                    * scaleY;

                DetectedHorizonCircle.Visibility =
                    Visibility.Visible;
                DetectedHorizonCircle.Width =
                    radiusX * 2;
                DetectedHorizonCircle.Height =
                    radiusY * 2;

                Canvas.SetLeft(
                    DetectedHorizonCircle,
                    centerX - radiusX);
                Canvas.SetTop(
                    DetectedHorizonCircle,
                    centerY - radiusY);

                DrawHorizonDash(
                    geometry,
                    centerX,
                    centerY,
                    scaleX,
                    scaleY);
            }
            else
            {
                DetectedHorizonCircle.Visibility =
                    Visibility.Collapsed;
                DetectedHorizonDash.Visibility =
                    Visibility.Collapsed;
            }
        }
        else
        {
            DetectedBodyCenter.Visibility =
                Visibility.Collapsed;
            CenterGuide.Visibility =
                Visibility.Collapsed;
            DetectedHorizonCircle.Visibility =
                Visibility.Collapsed;
            DetectedHorizonDash.Visibility =
                Visibility.Collapsed;
        }

        DrawProjectedAimPlan(
            state,
            readiness,
            geometry,
            scaleX,
            scaleY);

        string centerText =
            geometry.BodyCenterFound
                ? $"{geometry.BodyCenterX:0},{geometry.BodyCenterY:0} " +
                  $"c={geometry.BodyCenterConfidence:0.00}"
                : "?";

        string horizonText =
            geometry.HorizonMarkerFound
                ? $"{(geometry.HorizonMarkerObserved ? "OBS" : "TRACK")} " +
                  $"R={geometry.HorizonRadiusPixels:0.0}px " +
                  $"err={geometry.HorizonAimErrorPixels:+0.0;-0.0;0.0}px " +
                  $"c={geometry.HorizonMarkerConfidence:0.00}" +
                  (geometry.HorizonMarkerObserved
                      ? string.Empty
                      : $" age={geometry.HorizonObservationAgeMilliseconds:0}ms")
                : "?";

        DebugText.Text =
            $"DSS PROTOTYPE  #{sequence}  {tracking.SearchMode}\n" +
            $"BODY  {DisplayBody(state, context)}\n" +
            $"FOV   {context.VerticalFovDegrees:0.###}°   " +
            $"PATCH {DisplayPatch(context)}\n" +
            $"CENTER  {tracking.CenterState}  {centerText}\n" +
            $"MOTION  {tracking.CenterVelocityX:+0.0;-0.0;0.0}," +
            $"{tracking.CenterVelocityY:+0.0;-0.0;0.0} px/s\n" +
            $"HORIZON {tracking.HorizonState}  {horizonText}\n" +
            $"AIM β   {geometry.AimOffsetDegrees:0.00}°\n" +
            $"READY  {readiness.State}  " +
            $"{DisplayAngularDiameter(readiness)}  " +
            $"{DisplayEstimatedDistance(readiness)}";
    }

    private void DrawProjectedAimPlan(
        GameStateSnapshot state,
        DssAssistantReadinessSnapshot readiness,
        DssHudGeometry geometry,
        double scaleX,
        double scaleY)
    {
        ClearProjectedAimPlan();

        DssProjectedAimPlan plan =
            DssProbeAimSolver.Solve(
                state,
                readiness,
                geometry);

        if (!plan.IsAvailable)
        {
            return;
        }

        bool targetingV1 =
            plan.Source.StartsWith(
                "TARGETING_V1",
                StringComparison.Ordinal);

        foreach (DssProjectedAimPoint point
                 in plan.Points)
        {
            double x =
                point.ScreenX * scaleX;

            double y =
                point.ScreenY * scaleY;

            bool primary =
                point.Sequence == 1;

            Brush stroke =
                primary
                    ? Brushes.Yellow
                    : point.Zone switch
                    {
                        DssAimZone.FarSide =>
                            Brushes.OrangeRed,
                        DssAimZone.Limb =>
                            Brushes.DeepSkyBlue,
                        _ =>
                            Brushes.White
                    };

            var marker =
                new Ellipse
                {
                    Width =
                        AimPointDiameter,
                    Height =
                        AimPointDiameter,
                    Stroke =
                        stroke,
                    StrokeThickness =
                        primary ? 3 : 1.5,
                    Fill =
                        Brushes.Transparent,
                    Opacity =
                        primary ? 0.95 : 0.58,
                    IsHitTestVisible =
                        false
                };

            Canvas.SetLeft(
                marker,
                x - AimPointDiameter / 2d);

            Canvas.SetTop(
                marker,
                y - AimPointDiameter / 2d);

            OverlayCanvas.Children.Add(
                marker);

            aimPointElements.Add(
                marker);

            var label =
                new TextBlock
                {
                    Text =
                        targetingV1 && primary
                            ? "NEXT AIM"
                            : point.Sequence.ToString(),
                    FontFamily =
                        new FontFamily("Consolas"),
                    FontSize =
                        targetingV1 && primary
                            ? 12
                            : primary ? 13 : 10,
                    FontWeight =
                        primary
                            ? FontWeights.Bold
                            : FontWeights.Normal,
                    Foreground =
                        stroke,
                    Opacity =
                        primary ? 1.0 : 0.68,
                    IsHitTestVisible =
                        false
                };

            label.Measure(
                new Size(
                    double.PositiveInfinity,
                    double.PositiveInfinity));

            Canvas.SetLeft(
                label,
                x
                - label.DesiredSize.Width / 2d);

            if (targetingV1 && primary)
            {
                Canvas.SetTop(
                    label,
                    y
                    - AimPointDiameter / 2d
                    - label.DesiredSize.Height
                    - 4d);
            }
            else
            {
                Canvas.SetTop(
                    label,
                    y
                    - label.DesiredSize.Height / 2d);
            }

            OverlayCanvas.Children.Add(
                label);

            aimPointElements.Add(
                label);
        }
    }

    private void ClearProjectedAimPlan()
    {
        foreach (FrameworkElement element
                 in aimPointElements)
        {
            OverlayCanvas.Children.Remove(
                element);
        }

        aimPointElements.Clear();
    }

    private void UpdateReadinessPanel(
        DssAssistantReadinessSnapshot readiness)
    {
        Canvas.SetLeft(
            ReadinessPanel,
            Math.Max(
                18,
                (ActualWidth - ReadinessPanel.Width) / 2d));

        Canvas.SetTop(
            ReadinessPanel,
            18);

        switch (readiness.State)
        {
            case DssAssistantReadinessState.SelectBodyTarget:
                ReadinessTitle.Text =
                    "SELECT BODY AS TARGET";
                ReadinessDetail.Text =
                    "Target body is required for DSS distance/readiness calculations.";
                ReadinessPanel.BorderBrush =
                    Brushes.OrangeRed;
                break;

            case DssAssistantReadinessState.NeedBodyRadius:
                ReadinessTitle.Text =
                    "BODY DATA REQUIRED";
                ReadinessDetail.Text =
                    "Keep the body selected as target; radius data is not available yet.";
                ReadinessPanel.BorderBrush =
                    Brushes.Orange;
                break;

            case DssAssistantReadinessState.Calibrating:
                ReadinessTitle.Text =
                    "DSS CALIBRATING";
                ReadinessDetail.Text =
                    readiness.BodyRadiusMeters > 0
                        ? $"Waiting for a clean horizon observation • " +
                          $"ready {FormatDistance(readiness.RecommendedNearCenterDistanceMeters)}–" +
                          $"{FormatDistance(readiness.RecommendedFarCenterDistanceMeters)} " +
                          $"(target {FormatDistance(readiness.RecommendedTargetCenterDistanceMeters)})"
                        : "Waiting for a clean horizon observation • " +
                          "body radius lookup pending";
                ReadinessPanel.BorderBrush =
                    Brushes.DeepSkyBlue;
                break;

            case DssAssistantReadinessState.TooClose:
                ReadinessTitle.Text =
                    "TOO CLOSE — MOVE AWAY";
                ReadinessDetail.Text =
                    BuildMeasuredReadinessDetail(
                        readiness);
                ReadinessPanel.BorderBrush =
                    Brushes.Orange;
                break;

            case DssAssistantReadinessState.TooFar:
                ReadinessTitle.Text =
                    "TOO FAR — MOVE CLOSER";
                ReadinessDetail.Text =
                    BuildMeasuredReadinessDetail(
                        readiness);
                ReadinessPanel.BorderBrush =
                    Brushes.Goldenrod;
                break;

            case DssAssistantReadinessState.Ready:
                ReadinessTitle.Text =
                    "DSS ASSISTANT READY";
                ReadinessDetail.Text =
                    BuildMeasuredReadinessDetail(
                        readiness);
                ReadinessPanel.BorderBrush =
                    Brushes.LimeGreen;
                break;
        }
    }

    private static string BuildMeasuredReadinessDetail(
        DssAssistantReadinessSnapshot readiness)
    {
        if (readiness.BodyRadiusMeters <= 0)
        {
            return
                $"diam {readiness.AngularDiameterDegrees:0.0}° " +
                "• angular readiness active " +
                "• physical distance unavailable (radius not found)";
        }

        return
            $"diam {readiness.AngularDiameterDegrees:0.0}° " +
            $"• dist≈{FormatDistance(readiness.EstimatedCenterDistanceMeters)} " +
            $"• ready {FormatDistance(readiness.RecommendedNearCenterDistanceMeters)}–" +
            $"{FormatDistance(readiness.RecommendedFarCenterDistanceMeters)} " +
            $"• target {FormatDistance(readiness.RecommendedTargetCenterDistanceMeters)}";
    }

    private static string DisplayAngularDiameter(
        DssAssistantReadinessSnapshot readiness) =>
        readiness.HasAngularMeasurement
            ? $"diam={readiness.AngularDiameterDegrees:0.00}°"
            : "diam=?";

    private static string DisplayEstimatedDistance(
        DssAssistantReadinessSnapshot readiness) =>
        readiness.HasDistanceEstimate
            ? $"dist≈{FormatDistance(readiness.EstimatedCenterDistanceMeters)}"
            : "dist=?";

    private static string FormatDistance(
        double meters)
    {
        if (meters <= 0)
        {
            return "?";
        }

        const double lightSecondMeters =
            299_792_458d;

        if (meters >= lightSecondMeters)
        {
            return $"{meters / lightSecondMeters:0.00} ls";
        }

        if (meters >= 1_000_000d)
        {
            return $"{meters / 1_000_000d:0.0} Mm";
        }

        if (meters >= 1_000d)
        {
            return $"{meters / 1_000d:0.0} km";
        }

        return $"{meters:0} m";
    }

    private void DrawHorizonDash(
        DssHudGeometry geometry,
        double centerX,
        double centerY,
        double scaleX,
        double scaleY)
    {
        double markerX =
            geometry.HorizonMarkerX * scaleX;
        double markerY =
            geometry.HorizonMarkerY * scaleY;

        double vx = markerX - centerX;
        double vy = markerY - centerY;
        double length = Math.Sqrt(vx * vx + vy * vy);

        if (length < 1)
        {
            DetectedHorizonDash.Visibility =
                Visibility.Collapsed;
            return;
        }

        double nx = -vy / length;
        double ny = vx / length;
        const double half = 10;

        DetectedHorizonDash.Visibility =
            Visibility.Visible;
        DetectedHorizonDash.X1 =
            markerX - nx * half;
        DetectedHorizonDash.Y1 =
            markerY - ny * half;
        DetectedHorizonDash.X2 =
            markerX + nx * half;
        DetectedHorizonDash.Y2 =
            markerY + ny * half;
    }

    private void ApplyPassiveWindowStyles()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero)
        {
            return;
        }

        uint exStyle = WindowsAPI.GetWindowLong(
            helper.Handle,
            WindowsAPI.GWL_EXSTYLE);

        exStyle |= WindowsAPI.WS_EX_LAYERED
                   | WindowsAPI.WS_EX_TRANSPARENT
                   | WindowsAPI.WS_EX_NOACTIVATE
                   | WsExToolWindow;

        WindowsAPI.SetWindowLong(
            helper.Handle,
            WindowsAPI.GWL_EXSTYLE,
            exStyle);

        // The previous prototypes were visible inside their own desktop GDI
        // captures. That poisoned image tracking and created a feedback loop:
        // the tracker followed our green/cyan overlay, then snapped back to
        // Frontier's real marker. Exclude this window from capture explicitly.
        _ = SetWindowDisplayAffinity(
            helper.Handle,
            WdaExcludeFromCapture);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(
        IntPtr hWnd,
        uint dwAffinity);

    private void SyncToTarget()
    {
        if (targetWindow == IntPtr.Zero
            || !WindowsAPI.TryGetWindowRectDips(
                targetWindow,
                out WindowsAPI.RECT rect))
        {
            return;
        }

        Left = rect.Left;
        Top = rect.Top;
        Width = Math.Max(
            1,
            rect.Right - rect.Left);
        Height = Math.Max(
            1,
            rect.Bottom - rect.Top);
    }

    private static string DisplayBody(
        GameStateSnapshot state,
        DssPrototypeSessionContext context) =>
        !string.IsNullOrWhiteSpace(
            state.DestinationName)
            ? state.DestinationName
            : !string.IsNullOrWhiteSpace(
                context.BodyName)
                ? context.BodyName
                : $"ID {context.BodyId}";

    private static string DisplayPatch(
        DssPrototypeSessionContext context) =>
        context.DssPatchRadius > 0
            ? $"{context.DssPatchRadius:0.##}" +
              (context.DssEngineeringLevel > 0
                  ? $" G{context.DssEngineeringLevel}"
                  : string.Empty)
            : "?";
}
