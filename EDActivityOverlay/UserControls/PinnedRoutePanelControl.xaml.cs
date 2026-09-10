using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EDActivityOverlay.Models.Trading;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Navigation;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.UserControls;

internal sealed class PinnedRouteNavigationBusyChangedEventArgs(
    bool isBusy) : EventArgs
{
    public bool IsBusy { get; } = isBusy;
}

/// <summary>
/// Renderer-independent pinned-route surface. Trade-route execution lives in
/// PinnedRoutePresentationService; this control only renders state and forwards
/// user interactions to its host/controller.
/// </summary>
public partial class PinnedRoutePanelControl : UserControl, IDisposable
{
    private bool disposed;
    private bool interactive;
    private TradeRoute? currentRoute;
    private TradeRouteProgress currentProgress = new();
    private Func<IntPtr>? targetWindowProvider;
    private Action? unpinAction;
    private Action? returnControlToGameAction;
    private CancellationTokenSource? navigationCancellation;

    public event EventHandler? ContentChanged;
    public event EventHandler? PreferredHeightChanged;
    public event EventHandler? DragRequested;
    internal event EventHandler<PinnedRouteNavigationBusyChangedEventArgs>? NavigationBusyChanged;

    public bool IsPinned { get; private set; }
    public bool IsSuppressedByTradeWorkspace { get; private set; }
    public double PreferredHeight { get; private set; } = 184;

    public PinnedRoutePanelControl()
    {
        InitializeComponent();
        PinnedRoutePresentationService.Instance.Changed += OnPresentationChanged;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        RefreshFromState();
    }

    public void ConfigureHost(
        Func<IntPtr> targetWindowProvider,
        Action unpinAction,
        Action? returnControlToGameAction = null)
    {
        this.targetWindowProvider = targetWindowProvider;
        this.unpinAction = unpinAction;
        this.returnControlToGameAction = returnControlToGameAction;
    }

    public void ApplyInteractionMode(bool enabled)
    {
        interactive = enabled;
        UnpinButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        CopyFromStationButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        CopyToStationButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        InteractionHint.Text = enabled
            ? Loc.Get("Loc_DRAG_TO_MOVE")
            : Loc.Get("Loc_CTRL_6_INTERACT");
        DragHandle.Cursor = enabled ? Cursors.SizeAll : Cursors.Arrow;
        UpdateNavigationControls();
    }

    public void ApplySettings(AppSettings settings) =>
        OverlayChromeHelper.Apply(
            OverlayFrame,
            settings.OverlayChromeStyle);

    public void RefreshLocalization() => RefreshFromState();

    private void OnPresentationChanged(
        object? sender,
        EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshFromState));
            return;
        }

        RefreshFromState();
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
        UpdateNavigationControls();
    }

    private void RefreshFromState()
    {
        PinnedRoutePresentationSnapshot snapshot =
            PinnedRoutePresentationService.Instance.Current;

        IsPinned = snapshot.IsPinned;
        IsSuppressedByTradeWorkspace = snapshot.SuppressedByTradeWorkspace;
        currentRoute = snapshot.Route;
        currentProgress = snapshot.Progress;
        ApplySettings(SettingsService.Instance.Settings);

        if (currentRoute is not null)
        {
            ApplyRoute(currentRoute, currentProgress);
        }
        else
        {
            RouteNavigationStatusText.Text = string.Empty;
        }

        UpdateNavigationControls();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyRoute(
        TradeRoute route,
        TradeRouteProgress progress)
    {
        LegText.Text =
            Loc.Format(
                "Loc_Leg_Format",
                progress.LegNumber,
                progress.LegCount);

        ActionText.Text = progress.Action;
        CommodityText.Text =
            progress.Quantity > 0
                ? Loc.Format(
                    "Loc_Cargo_Format",
                    progress.Quantity,
                    progress.Commodity.ToUpperInvariant())
                : progress.Commodity.ToUpperInvariant();

        JumpsText.Text =
            progress.RemainingJumps > 0
                ? Loc.Format(
                    "Loc_Jumps_Format",
                    progress.RemainingJumps)
                : Loc.Get("Loc_DESTINATION");

        ProfitText.Text = route.IsCargoSaleOnly
            ? progress.Stage == TradeRouteStage.Completed
                ? Loc.Format(
                    "Loc_TRADE_CARGO_PIN_SOLD_VALUE",
                    progress.SaleRevenue)
                : Loc.Format(
                    "Loc_TRADE_CARGO_PIN_PLANNED_VALUE",
                    route.PlannedSaleValue)
            : progress.Stage == TradeRouteStage.Completed
                ? Loc.Format(
                    "Loc_Actual_Profit_Format",
                    progress.ActualProfit)
                : Loc.Format(
                    "Loc_Planned_Profit_Format",
                    route.TotalProfitPerTrip);

        FromPointText.Text =
            FormatRoutePoint(
                route.CardHeader.FromStation.System,
                route.CardHeader.FromStation.Name);
        ToPointText.Text =
            FormatRoutePoint(
                route.CardHeader.ToStation.System,
                route.CardHeader.ToStation.Name);

        CopyFromStationButton.IsEnabled =
            !string.IsNullOrWhiteSpace(route.CardHeader.FromStation.Name);
        CopyToStationButton.IsEnabled =
            !string.IsNullOrWhiteSpace(route.CardHeader.ToStation.Name);

        NoteText.Text = progress.Note;
        NoteText.Foreground =
            progress.IsInDanger
                ? (System.Windows.Media.Brush)FindResource("FailureColorBrush")
                : (System.Windows.Media.Brush)FindResource("MutedTextColorBrush");
    }

    private void UpdateNavigationControls()
    {
        bool flying =
            currentProgress.Stage is TradeRouteStage.FlyToBuy or TradeRouteStage.FlyToSell
            && !string.IsNullOrWhiteSpace(currentProgress.System)
            && !string.Equals(
                JournalMonitorService.Instance.Current.StarSystem,
                currentProgress.System,
                StringComparison.OrdinalIgnoreCase);

        NavigationPanel.Visibility =
            interactive && flying
                ? Visibility.Visible
                : Visibility.Collapsed;

        double preferredHeight =
            interactive && flying
                ? 226
                : 184;

        if (Math.Abs(PreferredHeight - preferredHeight) > 0.1)
        {
            PreferredHeight = preferredHeight;
            Height = preferredHeight;
            PreferredHeightChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Height = preferredHeight;
        }

        AutomaticNavigationButton.IsEnabled =
            SettingsService.Instance.Settings.EnableExperimentalRouteAutomation;
        AutomaticNavigationButton.ToolTip =
            AutomaticNavigationButton.IsEnabled
                ? null
                : Loc.Get("Loc_NAVIGATION_AUTO_DISABLED");
    }

    private void DragHandle_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (interactive && e.LeftButton == MouseButtonState.Pressed)
        {
            DragRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UnpinButton_Click(
        object sender,
        RoutedEventArgs e) =>
        unpinAction?.Invoke();

    private void FromPointText_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        CopyRoutePoint(
            currentRoute?.CardHeader.FromStation.System ?? string.Empty,
            "origin system");

    private void CopyFromStationButton_Click(
        object sender,
        RoutedEventArgs e) =>
        CopyRoutePoint(
            currentRoute?.CardHeader.FromStation.Name ?? string.Empty,
            "origin station");

    private void ToPointText_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        CopyRoutePoint(
            currentRoute?.CardHeader.ToStation.System ?? string.Empty,
            "destination system");

    private void CopyToStationButton_Click(
        object sender,
        RoutedEventArgs e) =>
        CopyRoutePoint(
            currentRoute?.CardHeader.ToStation.Name ?? string.Empty,
            "destination station");

    private async void PrepareNavigationButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await NavigateToTradeSystemAsync(false);

    private async void AutomaticNavigationButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await NavigateToTradeSystemAsync(true);

    private async Task NavigateToTradeSystemAsync(
        bool confirmAutomatically)
    {
        string target = currentProgress.System;
        IntPtr targetWindow = targetWindowProvider?.Invoke() ?? IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(target) || targetWindow == IntPtr.Zero)
        {
            return;
        }

        navigationCancellation?.Cancel();
        navigationCancellation?.Dispose();
        navigationCancellation = new CancellationTokenSource();

        Clipboard.SetText(target);
        RouteNavigationStatusText.Text =
            Loc.Format("Loc_NAVIGATION_PREPARING", target);
        NavigationBusyChanged?.Invoke(
            this,
            new PinnedRouteNavigationBusyChangedEventArgs(true));

        try
        {
            returnControlToGameAction?.Invoke();
            await Task.Yield();

            EliteNavigationResult result =
                await EliteRouteNavigationService.Instance.PrepareAsync(
                    target,
                    targetWindow,
                    confirmAutomatically,
                    navigationCancellation.Token);

            RouteNavigationStatusText.Text =
                string.IsNullOrWhiteSpace(result.Detail)
                    ? Loc.Format(result.MessageKey, result.TargetSystem)
                    : Loc.Format(
                        result.MessageKey,
                        result.TargetSystem,
                        result.Detail);
        }
        finally
        {
            NavigationBusyChanged?.Invoke(
                this,
                new PinnedRouteNavigationBusyChangedEventArgs(false));
        }
    }

    private static string FormatRoutePoint(
        string system,
        string station)
    {
        string normalizedSystem = system.Trim().ToUpperInvariant();
        string normalizedStation = station.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedStation)) return normalizedSystem;
        if (string.IsNullOrWhiteSpace(normalizedSystem)) return normalizedStation;
        return $"{normalizedSystem}  /  {normalizedStation}";
    }

    private static void CopyRoutePoint(
        string value,
        string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            Clipboard.SetText(value);
            Logger.Logger.Info($"Pinned route {kind} copied: {value}");
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"Unable to copy pinned route {kind}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        navigationCancellation?.Cancel();
        navigationCancellation?.Dispose();
        navigationCancellation = null;
        PinnedRoutePresentationService.Instance.Changed -= OnPresentationChanged;
        SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
    }
}
