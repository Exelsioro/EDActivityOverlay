using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EDActivityOverlay.Services;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows.Renderers;

/// <summary>
/// Individual-renderer HWND shell for the shared pinned-route control.
/// Route execution is owned by PinnedRoutePresentationService.
/// </summary>
internal sealed class PinnedRouteIndividualWindow : Window
{
    private readonly PinnedRoutePanelControl panel;
    private readonly DispatcherTimer updateTimer;
    private IntPtr targetWindow;
    private string placement = "MiddleLeft";
    private bool interactive;
    private bool showCursorWhenInteractive;
    private bool hasManualPosition;
    private double manualXRatio;
    private double manualYRatio;
    private bool navigationBusy;
    private bool disposed;

    public PinnedRouteIndividualWindow(EDActivityOverlay.MainWindow parentWindow)
    {
        Width = 700;
        Height = 184;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;

        panel = new PinnedRoutePanelControl();
        Content = panel;
        panel.ConfigureHost(
            () => targetWindow,
            parentWindow.UnpinRouteOverlay,
            parentWindow.ReturnControlToGameForNavigation);
        panel.ContentChanged += OnContentChanged;
        panel.PreferredHeightChanged += OnPreferredHeightChanged;
        panel.DragRequested += OnDragRequested;
        panel.NavigationBusyChanged += OnNavigationBusyChanged;

        Loaded += OnLoaded;
        updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        updateTimer.Tick += UpdateTimer_Tick;
        updateTimer.Start();
    }

    public void SetPlacement(string? value)
    {
        placement = string.IsNullOrWhiteSpace(value) ? "MiddleLeft" : value;
        hasManualPosition = false;
        PositionOverlay();
    }

    public void SetSuppressedByTradeWorkspace(bool value) =>
        PinnedRoutePresentationService.Instance.SetSuppressed(value);

    public void SetChromeStyle(string? value) =>
        panel.ApplySettings(SettingsService.Instance.Settings);

    public void ApplyInteractionMode(bool enabled, bool showCursor)
    {
        interactive = enabled;
        showCursorWhenInteractive = showCursor;
        panel.ApplyInteractionMode(enabled);
        WindowsAPI.SetClickThrough(this, !enabled || navigationBusy);
        if (enabled && showCursor && IsVisible)
        {
            WindowsAPI.EnsureCursorVisibleOnWindow(this);
        }
    }

    public void SetTargetWindow(IntPtr windowHandle, uint processId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        targetWindow = windowHandle;
        PositionOverlay();
    }

    public void RefreshLocalization() => panel.RefreshLocalization();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsAPI.SetupOverlayWindow(this);
        ApplyInteractionMode(interactive, showCursorWhenInteractive);
        PositionOverlay();
    }

    private void OnContentChanged(object? sender, EventArgs e) =>
        UpdatePresentation();

    private void OnPreferredHeightChanged(object? sender, EventArgs e)
    {
        Height = panel.PreferredHeight;
        PositionOverlay();
    }

    private void OnNavigationBusyChanged(
        object? sender,
        PinnedRouteNavigationBusyChangedEventArgs e)
    {
        navigationBusy = e.IsBusy;
        WindowsAPI.SetClickThrough(this, navigationBusy || !interactive);
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e) =>
        UpdatePresentation();

    private void UpdatePresentation()
    {
        if (disposed || targetWindow == IntPtr.Zero)
        {
            return;
        }

        PinnedRoutePresentationSnapshot state =
            PinnedRoutePresentationService.Instance.Current;

        if (!state.IsPinned
            || state.SuppressedByTradeWorkspace
            || OverlayVisibilityState.SuppressAll
            || OverlayVisibilityState.SuppressActivity)
        {
            if (IsVisible)
            {
                Hide();
            }
            return;
        }

        if (!WindowsAPI.IsWindow(targetWindow))
        {
            Close();
            return;
        }

        PositionOverlay();
        IntPtr foreground = WindowsAPI.GetForegroundWindow();
        bool focused = OverlayVisibilityPolicy.FocusAllowsPresentation(
            foreground == targetWindow,
            WindowsAPI.IsOverlayWindow(foreground));
        bool visible = OverlayVisibilityPolicy.TargetReady(
            true,
            WindowsAPI.IsWindowVisible(targetWindow),
            WindowsAPI.IsIconic(targetWindow)) && focused;

        if (visible && !IsVisible)
        {
            Show();
        }
        else if (!visible && IsVisible)
        {
            Hide();
        }

        if (IsVisible && IsLoaded)
        {
            WindowsAPI.SetTopmost(this, focused);
        }
    }

    private void PositionOverlay()
    {
        if (targetWindow == IntPtr.Zero
            || !WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect))
        {
            return;
        }

        Rect workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
        int targetWidth = rect.Right - rect.Left;
        int placementMaxWidth = placement.Equals(
                "TopCenter",
                StringComparison.OrdinalIgnoreCase)
            || placement.Equals(
                "BottomCenter",
                StringComparison.OrdinalIgnoreCase)
            ? OverlayLayoutSettings.PinnedMaxWidth
            : 400;
        int width = Math.Min(
            (int)(workArea.Width * OverlayLayoutSettings.PinnedWidthByMonitor),
            Math.Min(
                placementMaxWidth,
                (int)(targetWidth * OverlayLayoutSettings.PinnedWidthByTarget)));
        int height = (int)panel.PreferredHeight;

        double left;
        double top;
        if (hasManualPosition)
        {
            left = rect.Left + (Math.Max(0, targetWidth - width) * manualXRatio);
            top = rect.Top + (Math.Max(0, rect.Bottom - rect.Top - height) * manualYRatio);
        }
        else
        {
            (left, top) = OverlayLayoutHelper.GetPinnedPosition(
                rect,
                width,
                height,
                placement);
        }

        OverlayLayoutHelper.ClampPosition(
            ref left,
            ref top,
            width,
            height,
            workArea,
            OverlayLayoutSettings.DefaultMargin,
            OverlayLayoutSettings.PinnedClampMarginY);
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        panel.Width = width;
    }

    private void OnDragRequested(object? sender, EventArgs e)
    {
        if (!interactive || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
            if (targetWindow != IntPtr.Zero
                && WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect))
            {
                double availableX = Math.Max(1, rect.Right - rect.Left - ActualWidth);
                double availableY = Math.Max(1, rect.Bottom - rect.Top - ActualHeight);
                manualXRatio = Math.Clamp((Left - rect.Left) / availableX, 0, 1);
                manualYRatio = Math.Clamp((Top - rect.Top) / availableY, 0, 1);
                hasManualPosition = true;
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!disposed)
        {
            disposed = true;
            updateTimer.Stop();
            updateTimer.Tick -= UpdateTimer_Tick;
            panel.ContentChanged -= OnContentChanged;
            panel.PreferredHeightChanged -= OnPreferredHeightChanged;
            panel.DragRequested -= OnDragRequested;
            panel.NavigationBusyChanged -= OnNavigationBusyChanged;
            panel.Dispose();
        }

        base.OnClosed(e);
    }
}
