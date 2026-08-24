using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class ShipStatusOverlayWindow : Window
{
    private readonly DispatcherTimer updateTimer;
    private IntPtr targetWindow;
    private bool interactive;
    private bool enabled = true;
    private string placement = "TopCenter";
    private string currentSystem = string.Empty;
    private string nextSystem = string.Empty;
    private Func<bool>? contextSuppression;
    private bool disposed;

    public ShipStatusOverlayWindow(IntPtr targetWindow)
    {
        this.targetWindow = targetWindow;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            WindowsAPI.SetupOverlayWindow(this);
            ApplyInteractionMode(interactive, false);
            PositionOverlay();
        };
        JournalMonitorService.Instance.StateChanged += OnStateChanged;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        updateTimer.Tick += UpdateTimer_Tick;
        updateTimer.Start();
        ApplySettings(SettingsService.Instance.Settings);
        Refresh(JournalMonitorService.Instance.Current);
    }

    public void SetTargetWindow(IntPtr value) { targetWindow = value; PositionOverlay(); }
    public void SetContextSuppression(Func<bool>? value) => contextSuppression = value;
    public void ApplyInteractionMode(bool value, bool showCursor)
    {
        interactive = value;
        WindowsAPI.SetClickThrough(this, !value);
        DragHandle.Cursor = value ? Cursors.SizeAll : Cursors.Arrow;
    }

    public void RefreshLocalization() => Refresh(JournalMonitorService.Instance.Current);

    private void OnStateChanged(object? sender, GameStateChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => Refresh(e.State)));

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => ApplySettings(e.Settings)));

    private void ApplySettings(AppSettings settings)
    {
        enabled = settings.EnableShipStatusWidget;
        placement = settings.ShipStatusWidgetPosition;
        OverlayChromeHelper.Apply(OverlayFrame, settings.OverlayChromeStyle);
        PositionOverlay();
    }

    private void Refresh(GameStateSnapshot state)
    {
        ShipStatusPresentation view = ShipStatusPresentationBuilder.Build(state);
        currentSystem = view.CurrentSystem;
        nextSystem = view.NextSystem;
        CurrentSystemText.Text = string.IsNullOrWhiteSpace(currentSystem)
            ? Loc.Get("Loc_WAITING_FOR_GAME") : currentSystem.ToUpperInvariant();
        NextSystemText.Text = string.IsNullOrWhiteSpace(nextSystem)
            ? Loc.Get("Loc_ROUTE_NOT_PLOTTED") : nextSystem.ToUpperInvariant();
        RouteCaptionText.Text = view.RemainingJumps > 0
            ? Loc.Format("Loc_SHIP_STATUS_ROUTE_CAPTION_FORMAT", view.RemainingJumps, view.NextStarClass,
                view.NextStarScoopable ? Loc.Get("Loc_SCOOPABLE_SHORT") : Loc.Get("Loc_NOT_SCOOPABLE_SHORT"))
            : Loc.Get("Loc_NEXT_SYSTEM_SHORT");
        AdvisoryText.Text = view.Advisory switch
        {
            ShipStatusAdvisoryKind.FuelCritical => Loc.Format("Loc_SHIP_STATUS_FUEL_CRITICAL_FORMAT", view.FuelPercent),
            ShipStatusAdvisoryKind.FuelCaution => Loc.Format("Loc_SHIP_STATUS_FUEL_CAUTION_FORMAT", view.FuelPercent),
            ShipStatusAdvisoryKind.NoScoopableStars => Loc.Get("Loc_FUEL_NO_SCOOPABLE_ON_ROUTE"),
            ShipStatusAdvisoryKind.HazardousNextStar => Loc.Format("Loc_SHIP_STATUS_HAZARDOUS_STAR_FORMAT", view.NextSystem, view.NextStarClass),
            _ => view.RemainingJumps > 0
                ? Loc.Format("Loc_SHIP_STATUS_ROUTE_OK_FORMAT", view.NextSystem, view.NextStarClass)
                : Loc.Get("Loc_SHIP_STATUS_NO_ROUTE")
        };
        AdvisoryPanel.Visibility = view.Advisory == ShipStatusAdvisoryKind.None
            ? Visibility.Collapsed : Visibility.Visible;
        Height = view.Advisory == ShipStatusAdvisoryKind.None ? 58 : 92;
        AdvisoryPanel.BorderBrush = (System.Windows.Media.Brush)FindResource(
            view.Advisory is ShipStatusAdvisoryKind.FuelCritical or ShipStatusAdvisoryKind.HazardousNextStar
                ? "FailureColorBrush" : "AccentColorBrush");
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (disposed) return;
        bool contextSuppressed = contextSuppression?.Invoke() == true;
        bool targetReady = enabled && !contextSuppressed && !OverlayVisibilityState.SuppressAll
            && targetWindow != IntPtr.Zero && WindowsAPI.IsWindow(targetWindow)
            && WindowsAPI.IsWindowVisible(targetWindow) && !WindowsAPI.IsIconic(targetWindow);
        IntPtr foreground = WindowsAPI.GetForegroundWindow();
        bool focused = foreground == targetWindow || WindowsAPI.IsOverlayWindow(foreground);
        if (!targetReady || !focused) { if (IsVisible) Hide(); return; }
        PositionOverlay();
        if (!IsVisible) Show();
        WindowsAPI.SetTopmost(this, true);
    }

    private void PositionOverlay()
    {
        if (!WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect)) return;
        Rect workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
        (double left, double top) = OverlayLayoutHelper.GetPinnedPosition(rect, Width, Height, placement, 18);
        OverlayLayoutHelper.ClampPosition(ref left, ref top, Width, Height, workArea, 10, 10);
        Left = left; Top = top;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!interactive || e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void CurrentSystemText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Copy(currentSystem);
    private void NextSystemText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Copy(nextSystem);
    private static void Copy(string value) { if (!string.IsNullOrWhiteSpace(value)) Clipboard.SetText(value); }

    protected override void OnClosed(EventArgs e)
    {
        if (!disposed)
        {
            disposed = true;
            updateTimer.Stop();
            JournalMonitorService.Instance.StateChanged -= OnStateChanged;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
        }
        base.OnClosed(e);
    }
}
