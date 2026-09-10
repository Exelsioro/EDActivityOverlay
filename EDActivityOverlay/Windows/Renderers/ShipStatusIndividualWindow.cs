using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EDActivityOverlay.Services;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows.Renderers;

/// <summary>
/// Individual-renderer HWND shell for the shared ship-status control.
/// </summary>
internal sealed class ShipStatusIndividualWindow : Window
{
    private readonly ShipStatusPanelControl panel;
    private readonly DispatcherTimer updateTimer;
    private IntPtr targetWindow;
    private bool interactive;
    private bool enabled = true;
    private string placement = "TopCenter";
    private Func<bool>? contextSuppression;
    private bool disposed;

    public ShipStatusIndividualWindow(IntPtr targetWindow)
    {
        this.targetWindow = targetWindow;

        Width = 560;
        Height = 92;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;

        panel = new ShipStatusPanelControl();
        Content = panel;
        panel.PreferredHeightChanged += OnPreferredHeightChanged;
        panel.DragRequested += OnDragRequested;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;

        Loaded += OnLoaded;
        updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        updateTimer.Tick += UpdateTimer_Tick;
        updateTimer.Start();

        ApplySettings(SettingsService.Instance.Settings);
    }

    public void SetTargetWindow(IntPtr value)
    {
        targetWindow = value;
        PositionOverlay();
    }

    public void SetContextSuppression(Func<bool>? value) =>
        contextSuppression = value;

    public void ApplyInteractionMode(bool value, bool showCursor)
    {
        interactive = value;
        WindowsAPI.SetClickThrough(this, !value);
        panel.SetDragCursor(value);
        if (value && showCursor && IsVisible)
        {
            WindowsAPI.EnsureCursorVisibleOnWindow(this);
        }
    }

    public void RefreshLocalization() => panel.RefreshLocalization();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsAPI.SetupOverlayWindow(this);
        ApplyInteractionMode(interactive, false);
        PositionOverlay();
    }

    private void OnSettingsChanged(
        object? sender,
        SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnSettingsChanged(sender, e)));
            return;
        }

        ApplySettings(e.Settings);
    }

    private void ApplySettings(AppSettings settings)
    {
        enabled = settings.EnableShipStatusWidget;
        placement = settings.ShipStatusWidgetPosition;
        panel.ApplySettings(settings);
        PositionOverlay();
    }

    private void OnPreferredHeightChanged(object? sender, EventArgs e)
    {
        Height = panel.PreferredHeight;
        PositionOverlay();
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (disposed)
        {
            return;
        }

        bool contextSuppressed = contextSuppression?.Invoke() == true;
        bool targetExists = targetWindow != IntPtr.Zero && WindowsAPI.IsWindow(targetWindow);
        bool targetReady =
            enabled
            && !contextSuppressed
            && !OverlayVisibilityState.SuppressAll
            && OverlayVisibilityPolicy.TargetReady(
                targetExists,
                targetExists && WindowsAPI.IsWindowVisible(targetWindow),
                targetExists && WindowsAPI.IsIconic(targetWindow));

        IntPtr foreground = WindowsAPI.GetForegroundWindow();
        WindowsAPI.GetWindowThreadProcessId(targetWindow, out uint targetProcessId);
        bool focused = OverlayVisibilityPolicy.FocusAllowsPresentation(
            WindowsAPI.IsWindowOwnedByProcess(foreground, targetProcessId),
            WindowsAPI.IsOverlayWindow(foreground));

        if (!targetReady || !focused)
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

        Height = panel.PreferredHeight;
        Rect workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
        (double left, double top) =
            OverlayLayoutHelper.GetPinnedPosition(
                rect,
                Width,
                Height,
                placement,
                18);
        OverlayLayoutHelper.ClampPosition(
            ref left,
            ref top,
            Width,
            Height,
            workArea,
            10,
            10);
        Left = left;
        Top = top;
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
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            panel.PreferredHeightChanged -= OnPreferredHeightChanged;
            panel.DragRequested -= OnDragRequested;
            panel.Dispose();
        }

        base.OnClosed(e);
    }
}
