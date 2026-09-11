using System;
using System.Windows;
using EDActivityOverlay.Services;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay;

public partial class MainWindow
{
    private void RestoreMainOverlayCollapsedState()
    {
        mainPanel.ApplySettings(
            SettingsService.Instance.Settings);

        ApplyMainOverlaySizeForCurrentState();
    }

    private void ApplyMainOverlaySizeForCurrentState()
    {
        double scale =
            lastAppliedScale > 0
                ? lastAppliedScale
                : 1d;

        if (targetWindow != IntPtr.Zero
            && WindowsAPI.TryGetWindowRectDips(
                targetWindow,
                out WindowsAPI.RECT targetRect))
        {
            double targetWidth =
                targetRect.Right
                - targetRect.Left;

            double targetHeight =
                targetRect.Bottom
                - targetRect.Top;

            scale =
                OverlayLayoutHelper.ComputeAdaptiveScale(
                    targetWidth,
                    targetHeight,
                    OverlayLayoutSettings.MainMinScale,
                    OverlayLayoutSettings.MainMaxScale);

            lastAppliedScale =
                scale;
        }

        double desiredWidth =
            Math.Round(
                mainPanel.PreferredWidth
                * scale);

        double desiredHeight =
            Math.Round(
                mainPanel.PreferredHeight
                * scale);

        if (Math.Abs(
                Width
                - desiredWidth) > 0.5d)
        {
            Width =
                desiredWidth;
        }

        if (Math.Abs(
                Height
                - desiredHeight) > 0.5d)
        {
            Height =
                desiredHeight;
        }

        mainPanel.Width = desiredWidth;
        mainPanel.Height = desiredHeight;
    }

    private void PositionMainOverlayInPhysicalCorner()
    {
        Rect monitor =
            WindowsAPI.GetMonitorBounds(
                targetWindow);

        Left =
            Math.Round(
                monitor.Left);

        Top =
            Math.Round(
                monitor.Bottom
                - Height);
    }
}