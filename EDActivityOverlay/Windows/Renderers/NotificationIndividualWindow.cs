using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using EDActivityOverlay.Services;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows.Renderers;

/// <summary>
/// Individual-renderer HWND shell for the shared notification control.
/// </summary>
internal sealed class NotificationIndividualWindow : Window
{
    private readonly NotificationPanelControl panel;
    private readonly DispatcherTimer updateTimer;
    private IntPtr targetWindow;
    private bool disposed;

    public NotificationIndividualWindow(IntPtr targetWindow)
    {
        this.targetWindow = targetWindow;

        Width = 440;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 310;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;

        panel = new NotificationPanelControl();
        Content = panel;
        panel.ContentChanged += OnContentChanged;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;

        Loaded += OnLoaded;
        updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        updateTimer.Tick += UpdateTimer_Tick;
        updateTimer.Start();
    }

    public void SetTargetWindow(IntPtr value) => targetWindow = value;

    public void RefreshLocalization() => panel.RefreshLocalization();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsAPI.SetupOverlayWindow(this);
        WindowsAPI.SetClickThrough(this, true);
        PositionOverlay();
    }

    private void OnContentChanged(object? sender, EventArgs e) =>
        UpdatePresentation();

    private void OnSettingsChanged(
        object? sender,
        SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(UpdatePresentation));
            return;
        }

        UpdatePresentation();
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e) =>
        UpdatePresentation();

    private void UpdatePresentation()
    {
        if (disposed)
        {
            return;
        }

        bool targetExists = targetWindow != IntPtr.Zero && WindowsAPI.IsWindow(targetWindow);
        bool targetReady = OverlayVisibilityPolicy.TargetReady(
            targetExists,
            targetExists && WindowsAPI.IsWindowVisible(targetWindow),
            targetExists && WindowsAPI.IsIconic(targetWindow));
        IntPtr foreground = WindowsAPI.GetForegroundWindow();
        bool focused = OverlayVisibilityPolicy.FocusAllowsPresentation(
            foreground == targetWindow,
            WindowsAPI.IsOverlayWindow(foreground));

        if (!panel.HasNotifications
            || OverlayVisibilityState.SuppressAll
            || !targetReady
            || !focused)
        {
            if (IsVisible)
            {
                Hide();
            }
            return;
        }

        PositionOverlay();
        if (!IsVisible)
        {
            Show();
        }
        WindowsAPI.SetTopmost(this, true);
    }

    private void PositionOverlay()
    {
        if (!WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect))
        {
            return;
        }

        Rect workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
        double left = rect.Left + ((rect.Right - rect.Left - Width) / 2d);
        AppSettings settings = SettingsService.Instance.Settings;
        double top = settings.EnableShipStatusWidget
                     && settings.ShipStatusWidgetPosition.Equals(
                         "TopCenter",
                         StringComparison.OrdinalIgnoreCase)
            ? rect.Top + 118
            : rect.Top + 72;
        OverlayLayoutHelper.ClampPosition(
            ref left,
            ref top,
            Width,
            Math.Max(ActualHeight, 80),
            workArea,
            10,
            10);
        Left = left;
        Top = top;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!disposed)
        {
            disposed = true;
            updateTimer.Stop();
            updateTimer.Tick -= UpdateTimer_Tick;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            panel.ContentChanged -= OnContentChanged;
            panel.Dispose();
        }

        base.OnClosed(e);
    }
}
