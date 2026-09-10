using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

/// <summary>
/// Stable single-HWND VR capture surface. Panels hosted here are real WPF
/// controls, not mirrored top-level windows. The first migration slice contains
/// ship status, notifications and pinned routes; later overlay windows move
/// into additional composite slots without changing the capture HWND.
/// </summary>
public partial class VrCompositeOverlayWindow : Window
{
    private readonly Func<IntPtr> targetWindowProvider;
    private readonly DispatcherTimer layoutTimer;
    private bool disposed;

    public VrCompositeOverlayWindow(
        Func<IntPtr> targetWindowProvider)
    {
        this.targetWindowProvider = targetWindowProvider;
        InitializeComponent();

        Loaded += OnLoaded;
        Closed += OnClosed;
        NotificationPanel.ContentChanged += OnNotificationContentChanged;
        PinnedRoutePanel.ContentChanged += OnPinnedRouteContentChanged;
        ShipStatusPanel.PreferredHeightChanged += OnShipStatusHeightChanged;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;

        layoutTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        layoutTimer.Tick += LayoutTimer_Tick;
    }

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        WindowsAPI.SetupOverlayWindow(this);
        WindowsAPI.SetClickThrough(this, true);
        WindowsAPI.SetTopmost(this, true);

        AppSettings settings =
            SettingsService.Instance.Settings;
        ShipStatusPanel.ApplySettings(settings);

        layoutTimer.Start();
        RefreshLayout();
    }

    private void LayoutTimer_Tick(
        object? sender,
        EventArgs e) =>
        RefreshLayout();

    private void OnSettingsChanged(
        object? sender,
        SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(() => OnSettingsChanged(sender, e)));
            return;
        }

        ShipStatusPanel.ApplySettings(e.Settings);
        RefreshLayout();
    }

    private void OnNotificationContentChanged(
        object? sender,
        EventArgs e) =>
        RefreshLayout();

    private void OnPinnedRouteContentChanged(
        object? sender,
        EventArgs e) =>
        RefreshLayout();

    private void OnShipStatusHeightChanged(
        object? sender,
        EventArgs e) =>
        RefreshLayout();

    private void RefreshLayout()
    {
        if (disposed || !VrOverlaySupport.IsEnabled)
        {
            return;
        }

        IntPtr targetWindow =
            targetWindowProvider();

        if (targetWindow == IntPtr.Zero
            || !WindowsAPI.IsWindow(targetWindow)
            || !WindowsAPI.TryGetWindowRectDips(
                targetWindow,
                out WindowsAPI.RECT targetRect))
        {
            return;
        }

        double targetWidth =
            Math.Max(1, targetRect.Right - targetRect.Left);
        double targetHeight =
            Math.Max(1, targetRect.Bottom - targetRect.Top);

        if (Math.Abs(Left - targetRect.Left) > 0.5)
        {
            Left = targetRect.Left;
        }
        if (Math.Abs(Top - targetRect.Top) > 0.5)
        {
            Top = targetRect.Top;
        }
        if (Math.Abs(Width - targetWidth) > 0.5)
        {
            Width = targetWidth;
        }
        if (Math.Abs(Height - targetHeight) > 0.5)
        {
            Height = targetHeight;
        }

        OverlayCanvas.Width = targetWidth;
        OverlayCanvas.Height = targetHeight;

        AppSettings settings =
            SettingsService.Instance.Settings;
        bool suppressed =
            OverlayVisibilityState.SuppressAll;

        ShipStatusPanel.Visibility =
            !suppressed && settings.EnableShipStatusWidget
                ? Visibility.Visible
                : Visibility.Collapsed;

        NotificationPanel.Visibility =
            !suppressed && NotificationPanel.HasNotifications
                ? Visibility.Visible
                : Visibility.Collapsed;

        PinnedRoutePanel.Visibility =
            !suppressed
            && !OverlayVisibilityState.SuppressActivity
            && PinnedRoutePanel.IsPinned
            && !PinnedRoutePanel.IsSuppressedByTradeWorkspace
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (ShipStatusPanel.Visibility == Visibility.Visible)
        {
            PositionShipStatus(
                targetWidth,
                targetHeight,
                settings.ShipStatusWidgetPosition);
        }

        if (NotificationPanel.Visibility == Visibility.Visible)
        {
            PositionNotifications(
                targetWidth,
                targetHeight,
                settings);
        }

        if (PinnedRoutePanel.Visibility == Visibility.Visible)
        {
            PositionPinnedRoute(
                targetWidth,
                targetHeight,
                settings.PinnedRoutePosition);
        }

        WindowsAPI.SetTopmost(this, true);
    }

    private void PositionShipStatus(
        double targetWidth,
        double targetHeight,
        string placement)
    {
        var localRect = new WindowsAPI.RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)Math.Round(targetWidth),
            Bottom = (int)Math.Round(targetHeight)
        };

        (double left, double top) =
            OverlayLayoutHelper.GetPinnedPosition(
                localRect,
                ShipStatusPanel.Width,
                ShipStatusPanel.PreferredHeight,
                placement,
                18);

        left = Math.Clamp(
            left,
            0,
            Math.Max(0, targetWidth - ShipStatusPanel.Width));
        top = Math.Clamp(
            top,
            0,
            Math.Max(0, targetHeight - ShipStatusPanel.PreferredHeight));

        Canvas.SetLeft(ShipStatusPanel, left);
        Canvas.SetTop(ShipStatusPanel, top);
        Panel.SetZIndex(ShipStatusPanel, 40);
    }

    private void PositionNotifications(
        double targetWidth,
        double targetHeight,
        AppSettings settings)
    {
        double width =
            NotificationPanel.Width;
        double height =
            Math.Max(NotificationPanel.ActualHeight, 80);
        double left =
            Math.Max(0, (targetWidth - width) / 2d);
        double top =
            settings.EnableShipStatusWidget
            && settings.ShipStatusWidgetPosition.Equals(
                "TopCenter",
                StringComparison.OrdinalIgnoreCase)
                ? 118
                : 72;

        top = Math.Clamp(
            top,
            0,
            Math.Max(0, targetHeight - height));

        Canvas.SetLeft(NotificationPanel, left);
        Canvas.SetTop(NotificationPanel, top);
        Panel.SetZIndex(NotificationPanel, 90);
    }


    private void PositionPinnedRoute(
        double targetWidth,
        double targetHeight,
        string placement)
    {
        int placementMaxWidth =
            placement.Equals(
                "TopCenter",
                StringComparison.OrdinalIgnoreCase)
            || placement.Equals(
                "BottomCenter",
                StringComparison.OrdinalIgnoreCase)
                ? OverlayLayoutSettings.PinnedMaxWidth
                : 400;

        double width = Math.Max(
            280,
            Math.Min(
                placementMaxWidth,
                targetWidth * OverlayLayoutSettings.PinnedWidthByTarget));

        PinnedRoutePanel.Width = width;

        var localRect = new WindowsAPI.RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)Math.Round(targetWidth),
            Bottom = (int)Math.Round(targetHeight)
        };

        (double left, double top) =
            OverlayLayoutHelper.GetPinnedPosition(
                localRect,
                width,
                PinnedRoutePanel.PreferredHeight,
                placement);

        left = Math.Clamp(
            left,
            0,
            Math.Max(0, targetWidth - width));
        top = Math.Clamp(
            top,
            0,
            Math.Max(0, targetHeight - PinnedRoutePanel.PreferredHeight));

        Canvas.SetLeft(PinnedRoutePanel, left);
        Canvas.SetTop(PinnedRoutePanel, top);
        Panel.SetZIndex(PinnedRoutePanel, 30);
    }

    private void OnClosed(
        object? sender,
        EventArgs e)
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        layoutTimer.Stop();
        layoutTimer.Tick -= LayoutTimer_Tick;
        SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
        NotificationPanel.ContentChanged -= OnNotificationContentChanged;
        PinnedRoutePanel.ContentChanged -= OnPinnedRouteContentChanged;
        ShipStatusPanel.PreferredHeightChanged -= OnShipStatusHeightChanged;
        NotificationPanel.Dispose();
        PinnedRoutePanel.Dispose();
        ShipStatusPanel.Dispose();
    }
}
