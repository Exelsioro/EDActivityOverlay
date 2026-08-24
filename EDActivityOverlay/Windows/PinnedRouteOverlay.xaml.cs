using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Navigation;
using EDActivityOverlay.Utils;
using EDActivityOverlay.Models.Trading;

namespace EDActivityOverlay.Windows;

public partial class PinnedRouteOverlay : Window
{
    private IntPtr targetWindow;
    private uint targetProcessId;
    private readonly DispatcherTimer updateTimer;
    private TradeRouteProgressTracker? progressTracker;
    private TradeRoute? currentRoute;
    private readonly MainWindow? parentWindow;
    private string placement = "MiddleLeft";
    private bool interactive;
    private bool showCursorWhenInteractive;
    private bool hasManualPosition;
    private double manualXRatio;
    private double manualYRatio;
    private bool disposed;
    private string fromSystem = string.Empty;
    private string fromStation = string.Empty;
    private string toSystem = string.Empty;
    private string toStation = string.Empty;
    private string chromeStyle = OverlayChromeStyles.Compact;
    private CancellationTokenSource? navigationCancellation;
    private TradeRouteProgress currentProgress = new();

    public PinnedRouteOverlay(MainWindow? parentWindow = null)
    {
        this.parentWindow = parentWindow;
        InitializeComponent();
        SetChromeStyle(Services.SettingsService.Instance.Settings.OverlayChromeStyle);
        Loaded += (_, _) =>
        {
            WindowsAPI.SetupOverlayWindow(this);
            ApplyInteractionMode(interactive, showCursorWhenInteractive);
            PositionOverlay();
        };

        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        updateTimer.Tick += UpdateTimer_Tick;
        updateTimer.Start();
    }

    public void SetPlacement(string? value)
    {
        placement = string.IsNullOrWhiteSpace(value) ? "MiddleLeft" : value;
        hasManualPosition = false;
        ApplyChrome();
        PositionOverlay();
    }

    public void SetChromeStyle(string? value)
    {
        chromeStyle = OverlayChromeStyles.Normalize(value);
        ApplyChrome();
    }

    private void ApplyChrome() => OverlayChromeHelper.Apply(
        OverlayFrame,
        chromeStyle);

    public void ApplyInteractionMode(bool enabled, bool showCursor)
    {
        interactive = enabled;
        showCursorWhenInteractive = showCursor;
        WindowsAPI.SetClickThrough(this, !enabled);
        UnpinButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        CopyFromStationButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        CopyToStationButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        InteractionHint.Text = enabled ? Loc.Get("Loc_DRAG_TO_MOVE") : Loc.Get("Loc_CTRL_6_INTERACT");
        DragHandle.Cursor = enabled ? Cursors.SizeAll : Cursors.Arrow;
        if (enabled && showCursor && IsVisible)
        {
            WindowsAPI.EnsureCursorVisibleOnWindow(this);
        }
        UpdateNavigationControls();
    }

    public void SetTargetWindow(IntPtr windowHandle, uint processId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        targetWindow = windowHandle;
        targetProcessId = processId;
        PositionOverlay();
    }

    public void PinTradeRoute(TradeRoute tradeRoute)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        currentRoute = tradeRoute;
        fromSystem = tradeRoute.CardHeader.FromStation.System;
        fromStation = tradeRoute.CardHeader.FromStation.Name;
        toSystem = tradeRoute.CardHeader.ToStation.System;
        toStation = tradeRoute.CardHeader.ToStation.Name;
        ApplyRouteEndpoints();
        progressTracker?.Dispose();
        progressTracker = new TradeRouteProgressTracker(tradeRoute);
        progressTracker.ProgressChanged += OnProgressChanged;
        ApplyProgress(progressTracker.Current);
        PositionOverlay();
        Show();

        Logger.Logger.LogUserAction(
            $"Trade route pinned: {tradeRoute.CardHeader.FromStation.System} -> {tradeRoute.CardHeader.ToStation.System}");
    }

    private void OnProgressChanged(object? sender, TradeRouteProgressChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => ApplyProgress(e.Progress)));
    }

    private void ApplyProgress(TradeRouteProgress progress)
    {
        currentProgress = progress;
        LegText.Text = Loc.Format("Loc_Leg_Format", progress.LegNumber, progress.LegCount);
        ActionText.Text = progress.Action;
        CommodityText.Text = progress.Quantity > 0
            ? Loc.Format("Loc_Cargo_Format", progress.Quantity, progress.Commodity.ToUpperInvariant())
            : progress.Commodity.ToUpperInvariant();
        JumpsText.Text = progress.RemainingJumps > 0
            ? Loc.Format("Loc_Jumps_Format", progress.RemainingJumps)
            : Loc.Get("Loc_DESTINATION");

        long plannedProfit = currentRoute?.TotalProfitPerTrip ?? 0;
        ProfitText.Text = progress.Stage == TradeRouteStage.Completed
            ? Loc.Format("Loc_Actual_Profit_Format", progress.ActualProfit)
            : Loc.Format("Loc_Planned_Profit_Format", plannedProfit);
        NoteText.Text = progress.Note;
        NoteText.Foreground = progress.IsInDanger
            ? (System.Windows.Media.Brush)FindResource("FailureColorBrush")
            : (System.Windows.Media.Brush)FindResource("MutedTextColorBrush");
        UpdateNavigationControls();
    }

    private void UpdateNavigationControls()
    {
        bool flying = currentProgress.Stage is TradeRouteStage.FlyToBuy or TradeRouteStage.FlyToSell
                      && !string.IsNullOrWhiteSpace(currentProgress.System)
                      && !string.Equals(JournalMonitorService.Instance.Current.StarSystem,
                          currentProgress.System, StringComparison.OrdinalIgnoreCase);
        NavigationPanel.Visibility = interactive && flying ? Visibility.Visible : Visibility.Collapsed;
        Height = interactive && flying ? 226 : 184;
        AutomaticNavigationButton.IsEnabled = SettingsService.Instance.Settings.EnableExperimentalRouteAutomation;
        AutomaticNavigationButton.ToolTip = AutomaticNavigationButton.IsEnabled
            ? null : Loc.Get("Loc_NAVIGATION_AUTO_DISABLED");
    }

    public void RefreshLocalization()
    {
        ApplyRouteEndpoints();
        if (progressTracker != null)
        {
            ApplyProgress(progressTracker.Current);
        }
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (disposed || targetWindow == IntPtr.Zero)
        {
            return;
        }
        if (OverlayVisibilityState.SuppressAll || OverlayVisibilityState.SuppressActivity)
        {
            if (IsVisible) Hide();
            return;
        }
        if (!WindowsAPI.IsWindow(targetWindow))
        {
            Close();
            return;
        }

        PositionOverlay();
        IntPtr foreground = WindowsAPI.GetForegroundWindow();
        bool focused = foreground == targetWindow || WindowsAPI.IsOverlayWindow(foreground);
        bool visible = WindowsAPI.IsWindowVisible(targetWindow) && !WindowsAPI.IsIconic(targetWindow) && focused;
        if (visible && !IsVisible) Show();
        else if (!visible && IsVisible) Hide();
        if (IsVisible && IsLoaded) WindowsAPI.SetTopmost(this, focused);
    }

    private void PositionOverlay()
    {
        if (targetWindow == IntPtr.Zero || !WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect))
        {
            return;
        }

        Rect workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
        int targetWidth = rect.Right - rect.Left;
        int placementMaxWidth = placement.Equals("TopCenter", StringComparison.OrdinalIgnoreCase)
            || placement.Equals("BottomCenter", StringComparison.OrdinalIgnoreCase)
            ? OverlayLayoutSettings.PinnedMaxWidth
            : 400;
        int width = Math.Min(
            (int)(workArea.Width * OverlayLayoutSettings.PinnedWidthByMonitor),
            Math.Min(placementMaxWidth, (int)(targetWidth * OverlayLayoutSettings.PinnedWidthByTarget)));
        int height = (int)Height;
        double left;
        double top;
        if (hasManualPosition)
        {
            left = rect.Left + (Math.Max(0, targetWidth - width) * manualXRatio);
            top = rect.Top + (Math.Max(0, rect.Bottom - rect.Top - height) * manualYRatio);
        }
        else
        {
            (left, top) = OverlayLayoutHelper.GetPinnedPosition(rect, width, height, placement);
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
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!interactive || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
            if (targetWindow != IntPtr.Zero && WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect))
            {
                double availableX = Math.Max(1, rect.Right - rect.Left - ActualWidth);
                double availableY = Math.Max(1, rect.Bottom - rect.Top - ActualHeight);
                manualXRatio = Math.Clamp((Left - rect.Left) / availableX, 0, 1);
                manualYRatio = Math.Clamp((Top - rect.Top) / availableY, 0, 1);
                hasManualPosition = true;
                ApplyChrome();
            }
        }
        catch (InvalidOperationException)
        {
            // The mouse may have been released between the event and DragMove.
        }
    }

    private void UnpinButton_Click(object sender, RoutedEventArgs e) => parentWindow?.UnpinRouteOverlay();

    private void FromPointText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        CopyRoutePoint(fromSystem, "origin system");

    private void CopyFromStationButton_Click(object sender, RoutedEventArgs e) => CopyRoutePoint(fromStation, "origin station");

    private void ToPointText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        CopyRoutePoint(toSystem, "destination system");

    private void CopyToStationButton_Click(object sender, RoutedEventArgs e) => CopyRoutePoint(toStation, "destination station");

    private async void PrepareNavigationButton_Click(object sender, RoutedEventArgs e) =>
        await NavigateToTradeSystemAsync(false);

    private async void AutomaticNavigationButton_Click(object sender, RoutedEventArgs e) =>
        await NavigateToTradeSystemAsync(true);

    private async Task NavigateToTradeSystemAsync(bool confirmAutomatically)
    {
        string target = currentProgress.System;
        if (string.IsNullOrWhiteSpace(target)) return;
        navigationCancellation?.Cancel();
        navigationCancellation?.Dispose();
        navigationCancellation = new CancellationTokenSource();
        Clipboard.SetText(target);
        RouteNavigationStatusText.Text = Loc.Format("Loc_NAVIGATION_PREPARING", target);
        WindowsAPI.SetClickThrough(this, true);
        try
        {
            await Task.Yield();
            EliteNavigationResult result = await EliteRouteNavigationService.Instance.PrepareAsync(
                target, targetWindow, confirmAutomatically, navigationCancellation.Token);
            RouteNavigationStatusText.Text = string.IsNullOrWhiteSpace(result.Detail)
                ? Loc.Format(result.MessageKey, result.TargetSystem)
                : Loc.Format(result.MessageKey, result.TargetSystem, result.Detail);
        }
        finally
        {
            ApplyInteractionMode(interactive, showCursorWhenInteractive);
        }
    }

    private void ApplyRouteEndpoints()
    {
        FromPointText.Text = FormatRoutePoint(fromSystem, fromStation);
        ToPointText.Text = FormatRoutePoint(toSystem, toStation);
        CopyFromStationButton.IsEnabled = !string.IsNullOrWhiteSpace(fromStation);
        CopyToStationButton.IsEnabled = !string.IsNullOrWhiteSpace(toStation);
    }

    private static string FormatRoutePoint(string system, string station)
    {
        string normalizedSystem = system.Trim().ToUpperInvariant();
        string normalizedStation = station.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedStation)) return normalizedSystem;
        if (string.IsNullOrWhiteSpace(normalizedSystem)) return normalizedStation;
        return $"{normalizedSystem}  /  {normalizedStation}";
    }

    private static void CopyRoutePoint(string value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            Clipboard.SetText(value);
            Logger.Logger.Info($"Pinned route {kind} copied: {value}");
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Unable to copy pinned route {kind}: {ex.Message}");
        }
    }

    public new void Close()
    {
        if (!disposed)
        {
            disposed = true;
            updateTimer.Stop();
            if (progressTracker != null)
            {
                progressTracker.ProgressChanged -= OnProgressChanged;
                progressTracker.Dispose();
                progressTracker = null;
            }
            navigationCancellation?.Cancel();
            navigationCancellation?.Dispose();
            navigationCancellation = null;
        }
        base.Close();
    }
}
