using System;
using System.Windows;
using EDActivityOverlay.Services;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay;

public partial class MainWindow
{
    private const double CollapsedMainOverlayBaseWidth = 170d;
    private const double CollapsedMainOverlayBaseHeight = 32d;

    private bool mainOverlayCollapsed;

    private void CollapseMainOverlayButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (mainOverlayCollapsed)
        {
            return;
        }

        mainOverlayCollapsed = true;

        SettingsService.Instance.SetMainOverlayCollapsed(
            true);

        ExpandedControlContent.Visibility =
            Visibility.Collapsed;
        CollapsedControlContent.Visibility =
            Visibility.Visible;
        OverlayFrame.Padding =
            new Thickness(
                6,
                4,
                6,
                4);

        ApplyAdaptiveSizeForTarget();
        PositionMainOverlayInPhysicalCorner();
        UpdateInteractionStatusUi();
    }

    private void ExpandMainOverlayButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!mainOverlayCollapsed)
        {
            return;
        }

        mainOverlayCollapsed = false;

        SettingsService.Instance.SetMainOverlayCollapsed(
            false);

        CollapsedControlContent.Visibility =
            Visibility.Collapsed;
        ExpandedControlContent.Visibility =
            Visibility.Visible;
        OverlayFrame.Padding =
            new Thickness(
                10,
                7,
                10,
                7);

        ApplyAdaptiveSizeForTarget();
        PositionMainOverlayInPhysicalCorner();
        UpdateInteractionStatusUi();
    }

    private void RestoreMainOverlayCollapsedState()
    {
        mainOverlayCollapsed =
            SettingsService.Instance.Settings.MainOverlayCollapsed;

        ExpandedControlContent.Visibility =
            mainOverlayCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

        CollapsedControlContent.Visibility =
            mainOverlayCollapsed
                ? Visibility.Visible
                : Visibility.Collapsed;

        OverlayFrame.Padding =
            mainOverlayCollapsed
                ? new Thickness(
                    6,
                    4,
                    6,
                    4)
                : new Thickness(
                    10,
                    7,
                    10,
                    7);

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

        double baseWidth =
            mainOverlayCollapsed
                ? CollapsedMainOverlayBaseWidth
                : baseWindowWidth;

        double baseHeight =
            mainOverlayCollapsed
                ? CollapsedMainOverlayBaseHeight
                : baseWindowHeight;

        double desiredWidth =
            Math.Round(
                baseWidth
                * scale);

        double desiredHeight =
            Math.Round(
                baseHeight
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
