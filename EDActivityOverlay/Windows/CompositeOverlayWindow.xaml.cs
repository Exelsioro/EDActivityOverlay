using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

/// <summary>
/// Single-HWND overlay renderer. It can be used on the desktop and is the only
/// supported capture surface when VR compatibility is enabled.
/// </summary>
public partial class CompositeOverlayWindow : Window
{
    private readonly EDActivityOverlay.MainWindow controller;
    private readonly DispatcherTimer layoutTimer;
    private readonly MainOverlayPanelControl mainPanel;
    private readonly CompositeActivityHostControl activityHost;
    private readonly Dictionary<ActivityType, Point> compactActivityPositions = new();
    private bool interactive;
    private bool showCursor;
    private bool navigationBusy;
    private bool activityDragActive;
    private Point activityDragStart;
    private Point activityDragOrigin;
    private bool disposed;

    public CompositeOverlayWindow(EDActivityOverlay.MainWindow controller)
    {
        this.controller = controller;
        InitializeComponent();

        mainPanel = new MainOverlayPanelControl(controller);
        MainPanelHost.Content = mainPanel;

        activityHost = new CompositeActivityHostControl(controller);
        ActivityHost.Content = activityHost;

        PinnedRoutePanel.ConfigureHost(
            () => controller.TargetWindowHandle,
            controller.UnpinRouteOverlay,
            controller.ReturnControlToGameForNavigation);

        Loaded += OnLoaded;
        Closed += OnClosed;
        NotificationPanel.ContentChanged += OnNotificationContentChanged;
        PinnedRoutePanel.ContentChanged += OnPinnedRouteContentChanged;
        PinnedRoutePanel.PreferredHeightChanged += OnPinnedRouteHeightChanged;
        PinnedRoutePanel.NavigationBusyChanged += OnPinnedRouteNavigationBusyChanged;
        ShipStatusPanel.PreferredHeightChanged += OnShipStatusHeightChanged;
        activityHost.PresentationChanged += OnActivityPresentationChanged;
        activityHost.CompactDragRequested += OnActivityCompactDragRequested;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;

        layoutTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        layoutTimer.Tick += LayoutTimer_Tick;
    }

    public void RefreshLocalization()
    {
        mainPanel.RefreshLocalization();
        ShipStatusPanel.RefreshLocalization();
        NotificationPanel.RefreshLocalization();
        PinnedRoutePanel.RefreshLocalization();
        activityHost.RefreshLocalization();
        RefreshLayout();
    }

    public Task BeginCargoSaleFromMiningAsync() => activityHost.BeginCargoSaleFromMiningAsync();

    public void RefreshWindowMode()
    {
        VrOverlaySupport.ApplyCompositeWindowIdentity(this);
        RefreshLayout();
    }

    public void ApplyInteractionMode(bool enabled, bool shouldShowCursor)
    {
        interactive = enabled;
        showCursor = shouldShowCursor;
        ApplyInteractionState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsAPI.SetupOverlayWindow(this);
        VrOverlaySupport.ApplyCompositeWindowIdentity(this);
        ShipStatusPanel.ApplySettings(SettingsService.Instance.Settings);
        PinnedRoutePanel.ApplySettings(SettingsService.Instance.Settings);
        ApplyInteractionState();
        layoutTimer.Start();
        RefreshLayout();
    }

    private void LayoutTimer_Tick(object? sender, EventArgs e) => RefreshLayout();

    private void OnSettingsChanged(
        object? sender,
        SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnSettingsChanged(sender, e)));
            return;
        }

        VrOverlaySupport.ApplyCompositeWindowIdentity(this);
        ShipStatusPanel.ApplySettings(e.Settings);
        PinnedRoutePanel.ApplySettings(e.Settings);
        activityHost.ApplySettings(e.Settings);
        RefreshLayout();
    }

    private void OnNotificationContentChanged(object? sender, EventArgs e) => RefreshLayout();
    private void OnPinnedRouteContentChanged(object? sender, EventArgs e) => RefreshLayout();
    private void OnPinnedRouteHeightChanged(object? sender, EventArgs e) => RefreshLayout();
    private void OnShipStatusHeightChanged(object? sender, EventArgs e) => RefreshLayout();

    private void OnActivityPresentationChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(RefreshLayout));

    private void OnActivityCompactDragRequested(object? sender, EventArgs e)
    {
        if (!interactive
            || activityHost.IsFullMode
            || activityHost.RenderedActivity is not ActivityType activity)
        {
            return;
        }

        Point current = Mouse.GetPosition(OverlayCanvas);
        double left = Canvas.GetLeft(ActivityHost);
        double top = Canvas.GetTop(ActivityHost);
        activityDragStart = current;
        activityDragOrigin = new Point(
            double.IsNaN(left) ? 0 : left,
            double.IsNaN(top) ? 0 : top);
        activityDragActive = true;
        Mouse.Capture(ActivityHost);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!activityDragActive
            || e.LeftButton != MouseButtonState.Pressed
            || activityHost.RenderedActivity is not ActivityType activity)
        {
            return;
        }

        Point current = e.GetPosition(OverlayCanvas);
        double maxLeft = Math.Max(0, OverlayCanvas.Width - ActivityHost.Width);
        double maxTop = Math.Max(0, OverlayCanvas.Height - ActivityHost.Height);
        double left = Math.Clamp(
            activityDragOrigin.X + current.X - activityDragStart.X,
            0,
            maxLeft);
        double top = Math.Clamp(
            activityDragOrigin.Y + current.Y - activityDragStart.Y,
            0,
            maxTop);

        Canvas.SetLeft(ActivityHost, left);
        Canvas.SetTop(ActivityHost, top);
        compactActivityPositions[activity] = new Point(
            maxLeft > 0 ? left / maxLeft : 0,
            maxTop > 0 ? top / maxTop : 0);
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!activityDragActive)
        {
            return;
        }

        activityDragActive = false;
        if (Mouse.Captured == ActivityHost)
        {
            Mouse.Capture(null);
        }
    }

    private void OnPinnedRouteNavigationBusyChanged(
        object? sender,
        PinnedRouteNavigationBusyChangedEventArgs e)
    {
        navigationBusy = e.IsBusy;
        ApplyInteractionState();
    }

    private void RefreshLayout()
    {
        if (disposed || !OverlayRenderCoordinator.IsCompositeMode)
        {
            return;
        }

        IntPtr targetWindow = controller.TargetWindowHandle;
        bool targetExists = targetWindow != IntPtr.Zero && WindowsAPI.IsWindow(targetWindow);
        bool targetReady = OverlayVisibilityPolicy.TargetReady(
            targetExists,
            targetExists && WindowsAPI.IsWindowVisible(targetWindow),
            targetExists && WindowsAPI.IsIconic(targetWindow));

        IntPtr foreground = WindowsAPI.GetForegroundWindow();
        bool presentationFocused = OverlayVisibilityPolicy.FocusAllowsPresentation(
            foreground == targetWindow,
            WindowsAPI.IsOverlayWindow(foreground));

        if (!targetReady || !presentationFocused)
        {
            if (IsVisible)
            {
                Hide();
            }
            return;
        }

        if (!WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT targetRect))
        {
            return;
        }

        double targetWidth = Math.Max(1, targetRect.Right - targetRect.Left);
        double targetHeight = Math.Max(1, targetRect.Bottom - targetRect.Top);
        ApplyBounds(targetRect, targetWidth, targetHeight);
        ApplyInteractionState();

        AppSettings settings = SettingsService.Instance.Settings;
        bool suppressAll = OverlayVisibilityState.SuppressAll;
        bool suppressActivity = OverlayVisibilityState.SuppressActivity;

        activityHost.SetActivity(controller.CompositeCurrentActivity);
        activityHost.SetPresentationEnabled(!suppressAll && !suppressActivity);
        ActivityHost.Visibility =
            !suppressAll && !suppressActivity && activityHost.HasContent
                ? Visibility.Visible
                : Visibility.Collapsed;
        MainPanelHost.Visibility = !suppressAll ? Visibility.Visible : Visibility.Collapsed;
        ShipStatusPanel.Visibility =
            !suppressAll && settings.EnableShipStatusWidget
                ? Visibility.Visible
                : Visibility.Collapsed;
        NotificationPanel.Visibility =
            !suppressAll && NotificationPanel.HasNotifications
                ? Visibility.Visible
                : Visibility.Collapsed;
        PinnedRoutePanel.Visibility =
            !suppressAll
            && !suppressActivity
            && PinnedRoutePanel.IsPinned
            && !PinnedRoutePanel.IsSuppressedByTradeWorkspace
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (MainPanelHost.Visibility == Visibility.Visible)
        {
            double mainTop = Math.Max(0, targetHeight - mainPanel.PreferredHeight - 18);
            Canvas.SetLeft(MainPanelHost, 18);
            Canvas.SetTop(MainPanelHost, mainTop);
            Panel.SetZIndex(MainPanelHost, 60);
        }

        if (ActivityHost.Visibility == Visibility.Visible)
        {
            PositionActivity(targetWidth, targetHeight, settings);
        }

        if (ShipStatusPanel.Visibility == Visibility.Visible)
        {
            PositionShipStatus(targetWidth, targetHeight, settings.ShipStatusWidgetPosition);
        }

        if (NotificationPanel.Visibility == Visibility.Visible)
        {
            PositionNotifications(targetWidth, targetHeight, settings);
        }

        if (PinnedRoutePanel.Visibility == Visibility.Visible)
        {
            PositionPinnedRoute(targetWidth, targetHeight, settings.PinnedRoutePosition);
        }

        if (!IsVisible)
        {
            Show();
        }

        WindowsAPI.SetTopmost(this, VrOverlaySupport.IsEnabled || presentationFocused);
    }

    private void ApplyBounds(
        WindowsAPI.RECT targetRect,
        double targetWidth,
        double targetHeight)
    {
        if (Math.Abs(Left - targetRect.Left) > 0.5) Left = targetRect.Left;
        if (Math.Abs(Top - targetRect.Top) > 0.5) Top = targetRect.Top;
        if (Math.Abs(Width - targetWidth) > 0.5) Width = targetWidth;
        if (Math.Abs(Height - targetHeight) > 0.5) Height = targetHeight;

        OverlayCanvas.Width = targetWidth;
        OverlayCanvas.Height = targetHeight;
    }

    private void PositionActivity(
        double targetWidth,
        double targetHeight,
        AppSettings settings)
    {
        (double width, double height) = activityHost.GetPreferredSize(
            targetWidth,
            targetHeight);
        ActivityHost.Width = width;
        ActivityHost.Height = height;

        double left;
        double top;
        if (activityHost.IsFullMode)
        {
            left = Math.Max(0, (targetWidth - width) / 2d);
            top = Math.Max(0, (targetHeight - height) / 2d);
            Panel.SetZIndex(ActivityHost, 70);
        }
        else if (activityHost.RenderedActivity is ActivityType activity
                 && compactActivityPositions.TryGetValue(activity, out Point ratio))
        {
            left = Math.Clamp(
                ratio.X * Math.Max(0, targetWidth - width),
                0,
                Math.Max(0, targetWidth - width));
            top = Math.Clamp(
                ratio.Y * Math.Max(0, targetHeight - height),
                0,
                Math.Max(0, targetHeight - height));
            Panel.SetZIndex(ActivityHost, 50);
        }
        else
        {
            var localRect = new WindowsAPI.RECT
            {
                Left = 0,
                Top = 0,
                Right = (int)Math.Round(targetWidth),
                Bottom = (int)Math.Round(targetHeight)
            };
            string placement = OverlayLayoutHelper.GetOppositeSidePlacement(
                settings.PinnedRoutePosition);
            (left, top) = OverlayLayoutHelper.GetPinnedPosition(
                localRect,
                width,
                height,
                placement,
                18);
            left = Math.Clamp(left, 0, Math.Max(0, targetWidth - width));
            top = Math.Clamp(top, 0, Math.Max(0, targetHeight - height));
            Panel.SetZIndex(ActivityHost, 50);
        }

        Canvas.SetLeft(ActivityHost, left);
        Canvas.SetTop(ActivityHost, top);
    }

    private void ApplyInteractionState()
    {
        bool canInteract = interactive && !navigationBusy;
        OverlayCanvas.IsHitTestVisible = canInteract;
        PinnedRoutePanel.ApplyInteractionMode(canInteract);
        activityHost.ApplyInteractionMode(canInteract);
        WindowsAPI.SetClickThrough(this, !canInteract);

        if (canInteract && showCursor && IsVisible)
        {
            WindowsAPI.EnsureCursorVisibleOnWindow(this);
        }
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

        (double left, double top) = OverlayLayoutHelper.GetPinnedPosition(
            localRect,
            ShipStatusPanel.Width,
            ShipStatusPanel.PreferredHeight,
            placement,
            18);

        left = Math.Clamp(left, 0, Math.Max(0, targetWidth - ShipStatusPanel.Width));
        top = Math.Clamp(top, 0, Math.Max(0, targetHeight - ShipStatusPanel.PreferredHeight));
        Canvas.SetLeft(ShipStatusPanel, left);
        Canvas.SetTop(ShipStatusPanel, top);
        Panel.SetZIndex(ShipStatusPanel, 40);
    }

    private void PositionNotifications(
        double targetWidth,
        double targetHeight,
        AppSettings settings)
    {
        double width = NotificationPanel.Width;
        double height = Math.Max(NotificationPanel.ActualHeight, 80);
        double left = Math.Max(0, (targetWidth - width) / 2d);
        double top = settings.EnableShipStatusWidget
                     && settings.ShipStatusWidgetPosition.Equals(
                         "TopCenter",
                         StringComparison.OrdinalIgnoreCase)
            ? 118
            : 72;

        top = Math.Clamp(top, 0, Math.Max(0, targetHeight - height));
        Canvas.SetLeft(NotificationPanel, left);
        Canvas.SetTop(NotificationPanel, top);
        Panel.SetZIndex(NotificationPanel, 90);
    }

    private void PositionPinnedRoute(
        double targetWidth,
        double targetHeight,
        string placement)
    {
        int placementMaxWidth = placement.Equals(
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

        (double left, double top) = OverlayLayoutHelper.GetPinnedPosition(
            localRect,
            width,
            PinnedRoutePanel.PreferredHeight,
            placement);

        left = Math.Clamp(left, 0, Math.Max(0, targetWidth - width));
        top = Math.Clamp(top, 0, Math.Max(0, targetHeight - PinnedRoutePanel.PreferredHeight));
        Canvas.SetLeft(PinnedRoutePanel, left);
        Canvas.SetTop(PinnedRoutePanel, top);
        Panel.SetZIndex(PinnedRoutePanel, 30);
    }

    private void OnClosed(object? sender, EventArgs e)
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
        PinnedRoutePanel.PreferredHeightChanged -= OnPinnedRouteHeightChanged;
        PinnedRoutePanel.NavigationBusyChanged -= OnPinnedRouteNavigationBusyChanged;
        ShipStatusPanel.PreferredHeightChanged -= OnShipStatusHeightChanged;
        activityHost.PresentationChanged -= OnActivityPresentationChanged;
        activityHost.CompactDragRequested -= OnActivityCompactDragRequested;
        PreviewMouseMove -= OnPreviewMouseMove;
        PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        if (Mouse.Captured == ActivityHost)
        {
            Mouse.Capture(null);
        }
        mainPanel.Dispose();
        activityHost.Dispose();
        NotificationPanel.Dispose();
        PinnedRoutePanel.Dispose();
        ShipStatusPanel.Dispose();
    }
}
