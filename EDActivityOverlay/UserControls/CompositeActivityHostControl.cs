using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Navigation;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

/// <summary>
/// Renderer-neutral activity surface for the single-HWND composite renderer.
/// Existing activity UserControls live here directly; legacy Window classes are
/// not created just to obtain their visual tree.
/// </summary>
public sealed class CompositeActivityHostControl : Grid, IDisposable
{
    private enum MiningSurface
    {
        Compact,
        Analytics,
        Locations
    }

    private const double TradeCompactWidth = 420;
    private const double TradeCompactHeight = 305;
    private const double TradeFullMinWidth = 1040;
    private const double TradeFullMinHeight = 620;
    private const double TradeFullMaxWidth = 1180;
    private const double TradeFullMaxHeight = 760;

    private const double MiningCompactWidth = 420;
    private const double MiningCompactHeight = 525;
    private const double MiningFullMinWidth = 940;
    private const double MiningFullMinHeight = 600;
    private const double MiningFullMaxWidth = 1120;
    private const double MiningFullMaxHeight = 720;

    private readonly EDActivityOverlay.MainWindow controller;
    private readonly TradeWorkspaceControl tradeWorkspaceControl;
    private readonly MiningWorkspaceControl miningWorkspaceControl;
    private readonly MiningAnalyticsWorkspaceControl miningAnalyticsWorkspaceControl;
    private readonly MiningLocationWorkspaceControl miningLocationWorkspaceControl;

    private ActivityType? renderedActivity;
    private MiningSurface miningSurface;
    private bool presentationEnabled = true;
    private bool activityOwnsExclusiveInteraction;
    private bool disposed;

    public CompositeActivityHostControl(EDActivityOverlay.MainWindow controller)
    {
        this.controller = controller;

        tradeWorkspaceControl = new TradeWorkspaceControl();
        miningWorkspaceControl = new MiningWorkspaceControl();
        miningAnalyticsWorkspaceControl = new MiningAnalyticsWorkspaceControl();
        miningLocationWorkspaceControl = new MiningLocationWorkspaceControl();

        Children.Add(tradeWorkspaceControl);
        Children.Add(miningWorkspaceControl);
        Children.Add(miningAnalyticsWorkspaceControl);
        Children.Add(miningLocationWorkspaceControl);

        tradeWorkspaceControl.CloseRequested += CloseCurrentActivityRequested;
        tradeWorkspaceControl.DragRequested += CompactDragRequestedFromChild;
        tradeWorkspaceControl.ViewModeChanged += TradeViewModeChanged;
        tradeWorkspaceControl.PinRequested += PinTradeRouteRequested;
        tradeWorkspaceControl.CargoSalePinRequested += PinCargoSaleRouteRequested;
        tradeWorkspaceControl.RoundTripPinRequested += PinRoundTripRouteRequested;
        tradeWorkspaceControl.ReroutePinUpdateRequested += UpdatePinnedTradeRouteRequested;
        tradeWorkspaceControl.UnpinRequested += UnpinTradeRouteRequested;
        tradeWorkspaceControl.NavigateSystemRequested += NavigateTradeSystemRequested;

        miningWorkspaceControl.CloseRequested += CloseCurrentActivityRequested;
        miningWorkspaceControl.DragRequested += CompactDragRequestedFromChild;
        miningWorkspaceControl.FullRequested += OpenMiningAnalyticsRequested;
        miningWorkspaceControl.SellCargoRequested += SellMiningCargoRequested;
        miningWorkspaceControl.LocationsRequested += OpenMiningLocationsRequested;
        miningAnalyticsWorkspaceControl.BackRequested += CloseMiningAnalyticsRequested;
        miningAnalyticsWorkspaceControl.CloseRequested += CloseCurrentActivityRequested;
        miningLocationWorkspaceControl.BackRequested += CloseMiningLocationsRequested;
        miningLocationWorkspaceControl.CloseRequested += CloseCurrentActivityRequested;
        miningLocationWorkspaceControl.NavigateSystemRequested += NavigateMiningLocationRequested;

        JournalMonitorService.Instance.StateChanged += OnJournalStateChanged;

        ApplySettings(SettingsService.Instance.Settings);
        ApplyJournalState(JournalMonitorService.Instance.Current);
        ApplyVisibility();
    }

    public event EventHandler? PresentationChanged;
    public event EventHandler? CompactDragRequested;

    public bool HasContent =>
        renderedActivity is ActivityType.Trade or ActivityType.Mining;

    public bool IsFullMode =>
        renderedActivity switch
        {
            ActivityType.Trade => tradeWorkspaceControl.IsFullMode,
            ActivityType.Mining => miningSurface != MiningSurface.Compact,
            _ => false
        };

    public ActivityType? RenderedActivity => renderedActivity;

    public void SetActivity(ActivityType activity)
    {
        if (disposed)
        {
            return;
        }

        if (renderedActivity == activity)
        {
            ApplyVisibility();
            return;
        }

        renderedActivity = activity;
        ApplyJournalState(JournalMonitorService.Instance.Current);
        ApplyVisibility();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPresentationEnabled(bool enabled)
    {
        if (disposed || presentationEnabled == enabled)
        {
            return;
        }

        presentationEnabled = enabled;
        ApplyVisibility();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplySettings(AppSettings settings)
    {
        tradeWorkspaceControl.SetChromeStyle(settings.OverlayChromeStyle);
        miningWorkspaceControl.SetChromeStyle(settings.OverlayChromeStyle);
    }

    public void RefreshLocalization()
    {
        tradeWorkspaceControl.RefreshLocalization();
        miningWorkspaceControl.RefreshLocalization();
        miningAnalyticsWorkspaceControl.RefreshLocalization();
        miningLocationWorkspaceControl.RefreshLocalization();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    public (double Width, double Height) GetPreferredSize(
        double availableWidth,
        double availableHeight)
    {
        double widthLimit = Math.Max(1, availableWidth - 36);
        double heightLimit = Math.Max(1, availableHeight - 36);

        return renderedActivity switch
        {
            ActivityType.Trade when tradeWorkspaceControl.IsFullMode =>
                (FitFullSize(availableWidth * 0.82, TradeFullMinWidth, TradeFullMaxWidth, widthLimit),
                 FitFullSize(availableHeight * 0.80, TradeFullMinHeight, TradeFullMaxHeight, heightLimit)),

            ActivityType.Trade =>
                (Math.Min(TradeCompactWidth, widthLimit),
                 Math.Min(TradeCompactHeight, heightLimit)),

            ActivityType.Mining when miningSurface != MiningSurface.Compact =>
                (FitFullSize(availableWidth * 0.82, MiningFullMinWidth, MiningFullMaxWidth, widthLimit),
                 FitFullSize(availableHeight * 0.80, MiningFullMinHeight, MiningFullMaxHeight, heightLimit)),

            ActivityType.Mining =>
                (Math.Min(MiningCompactWidth, widthLimit),
                 Math.Min(MiningCompactHeight, heightLimit)),

            _ => (0, 0)
        };
    }

    public async Task BeginCargoSaleFromMiningAsync()
    {
        SetActivity(ActivityType.Trade);
        ApplyJournalState(JournalMonitorService.Instance.Current);
        await tradeWorkspaceControl.BeginCargoSaleFromMiningAsync();
        ApplyVisibility();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double FitFullSize(
        double desired,
        double minimum,
        double maximum,
        double available) =>
        Math.Min(
            Math.Max(1, available),
            Math.Min(maximum, Math.Max(minimum, desired)));

    private void ApplyVisibility()
    {
        tradeWorkspaceControl.Visibility = Visibility.Collapsed;
        miningWorkspaceControl.Visibility = Visibility.Collapsed;
        miningAnalyticsWorkspaceControl.Visibility = Visibility.Collapsed;
        miningLocationWorkspaceControl.Visibility = Visibility.Collapsed;

        if (presentationEnabled)
        {
            switch (renderedActivity)
            {
                case ActivityType.Trade:
                    tradeWorkspaceControl.Visibility = Visibility.Visible;
                    break;

                case ActivityType.Mining:
                    switch (miningSurface)
                    {
                        case MiningSurface.Analytics:
                            miningAnalyticsWorkspaceControl.Visibility = Visibility.Visible;
                            break;
                        case MiningSurface.Locations:
                            miningLocationWorkspaceControl.Visibility = Visibility.Visible;
                            break;
                        default:
                            miningWorkspaceControl.Visibility = Visibility.Visible;
                            break;
                    }
                    break;
            }
        }

        UpdateExclusiveInteraction();
    }

    private void UpdateExclusiveInteraction()
    {
        bool shouldOwnExclusive =
            presentationEnabled
            && (renderedActivity == ActivityType.Trade && tradeWorkspaceControl.IsFullMode
                || renderedActivity == ActivityType.Mining && miningSurface != MiningSurface.Compact);

        controller.SetPinnedRouteSuppressedByTradeWorkspace(
            presentationEnabled
            && renderedActivity == ActivityType.Trade
            && tradeWorkspaceControl.IsFullMode);

        if (activityOwnsExclusiveInteraction == shouldOwnExclusive)
        {
            return;
        }

        activityOwnsExclusiveInteraction = shouldOwnExclusive;
        if (shouldOwnExclusive)
        {
            controller.BeginExclusiveOverlayInteraction();
        }
        else
        {
            controller.EndExclusiveOverlayInteraction();
        }
    }

    private void ApplyJournalState(GameStateSnapshot state)
    {
        tradeWorkspaceControl.UpdateJournalState(state);
        miningWorkspaceControl.UpdateJournalState(state);
        miningAnalyticsWorkspaceControl.UpdateJournalState(state);
        miningLocationWorkspaceControl.UpdateJournalState(state);
    }

    private void OnJournalStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyJournalState(e.State);
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => ApplyJournalState(e.State)));
    }

    private void CloseCurrentActivityRequested()
    {
        SetPresentationEnabled(false);
        controller.HideCompositeActivity();
    }

    private void CompactDragRequestedFromChild()
    {
        if (!IsFullMode)
        {
            CompactDragRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TradeViewModeChanged(bool full)
    {
        UpdateExclusiveInteraction();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PinTradeRouteRequested(TradeRouteCandidate candidate)
    {
        TradeRouteProgressTracker? tracker = controller.OnPinRouteRequested(
            TradeRoutePresentationAdapter.ToPresentation(candidate),
            keepTradeWorkspace: true);
        tradeWorkspaceControl.ActivatePinnedRoute(candidate, tracker);
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PinCargoSaleRouteRequested(CargoSaleCandidate candidate)
    {
        TradeRouteProgressTracker? tracker = controller.OnPinRouteRequested(
            TradeRoutePresentationAdapter.ToPresentation(
                candidate,
                JournalMonitorService.Instance.Current),
            keepTradeWorkspace: true);
        tradeWorkspaceControl.ActivatePinnedCargoSale(tracker);
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PinRoundTripRouteRequested(TradeRoundTripCandidate candidate)
    {
        TradeRouteProgressTracker? tracker = controller.OnPinRouteRequested(
            TradeRoutePresentationAdapter.ToPresentation(candidate),
            keepTradeWorkspace: true);
        tradeWorkspaceControl.ActivatePinnedRoute(candidate, tracker);
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePinnedTradeRouteRequested(TradeRouteCandidate candidate)
    {
        TradeRouteProgressTracker? tracker = controller.OnPinRouteRequested(
            TradeRoutePresentationAdapter.ToPresentation(candidate),
            keepTradeWorkspace: true,
            preserveExecution: true);
        tradeWorkspaceControl.AttachExecutionTracker(tracker);
    }

    private void UnpinTradeRouteRequested() => controller.UnpinRouteOverlay();

    private async void NavigateTradeSystemRequested(string targetSystem)
    {
        await NavigateSystemAsync(targetSystem, "Trade commodity");
    }

    private void OpenMiningAnalyticsRequested()
    {
        miningSurface = MiningSurface.Analytics;
        miningAnalyticsWorkspaceControl.ReloadHistory();
        miningAnalyticsWorkspaceControl.UpdateJournalState(JournalMonitorService.Instance.Current);
        ApplyVisibility();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseMiningAnalyticsRequested()
    {
        miningSurface = MiningSurface.Compact;
        miningWorkspaceControl.UpdateJournalState(JournalMonitorService.Instance.Current);
        ApplyVisibility();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OpenMiningLocationsRequested()
    {
        miningSurface = MiningSurface.Locations;
        miningLocationWorkspaceControl.UpdateJournalState(JournalMonitorService.Instance.Current);
        ApplyVisibility();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseMiningLocationsRequested()
    {
        miningSurface = MiningSurface.Compact;
        miningWorkspaceControl.UpdateJournalState(JournalMonitorService.Instance.Current);
        ApplyVisibility();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void NavigateMiningLocationRequested(string targetSystem)
    {
        await NavigateSystemAsync(targetSystem, "Mining location");
    }

    private async Task NavigateSystemAsync(string targetSystem, string source)
    {
        IntPtr targetWindow = controller.TargetWindowHandle;
        if (string.IsNullOrWhiteSpace(targetSystem)
            || targetWindow == IntPtr.Zero)
        {
            return;
        }

        bool automatic = SettingsService.Instance.Settings.EnableExperimentalRouteAutomation;
        EliteNavigationResult result = await EliteRouteNavigationService.Instance.PrepareAsync(
            targetSystem,
            targetWindow,
            automatic);

        if (result.Status == EliteNavigationStatus.Failed)
        {
            Logger.Logger.Warning(
                $"{source} navigation failed for {targetSystem}: {result.MessageKey} {result.Detail}");
        }
    }

    private async void SellMiningCargoRequested()
    {
        await controller.OpenTradeCargoSaleFromMiningAsync();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        JournalMonitorService.Instance.StateChanged -= OnJournalStateChanged;

        tradeWorkspaceControl.CloseRequested -= CloseCurrentActivityRequested;
        tradeWorkspaceControl.DragRequested -= CompactDragRequestedFromChild;
        tradeWorkspaceControl.ViewModeChanged -= TradeViewModeChanged;
        tradeWorkspaceControl.PinRequested -= PinTradeRouteRequested;
        tradeWorkspaceControl.CargoSalePinRequested -= PinCargoSaleRouteRequested;
        tradeWorkspaceControl.RoundTripPinRequested -= PinRoundTripRouteRequested;
        tradeWorkspaceControl.ReroutePinUpdateRequested -= UpdatePinnedTradeRouteRequested;
        tradeWorkspaceControl.UnpinRequested -= UnpinTradeRouteRequested;
        tradeWorkspaceControl.NavigateSystemRequested -= NavigateTradeSystemRequested;

        miningWorkspaceControl.CloseRequested -= CloseCurrentActivityRequested;
        miningWorkspaceControl.DragRequested -= CompactDragRequestedFromChild;
        miningWorkspaceControl.FullRequested -= OpenMiningAnalyticsRequested;
        miningWorkspaceControl.SellCargoRequested -= SellMiningCargoRequested;
        miningWorkspaceControl.LocationsRequested -= OpenMiningLocationsRequested;
        miningAnalyticsWorkspaceControl.BackRequested -= CloseMiningAnalyticsRequested;
        miningAnalyticsWorkspaceControl.CloseRequested -= CloseCurrentActivityRequested;
        miningLocationWorkspaceControl.BackRequested -= CloseMiningLocationsRequested;
        miningLocationWorkspaceControl.CloseRequested -= CloseCurrentActivityRequested;
        miningLocationWorkspaceControl.NavigateSystemRequested -= NavigateMiningLocationRequested;

        if (activityOwnsExclusiveInteraction)
        {
            activityOwnsExclusiveInteraction = false;
            controller.EndExclusiveOverlayInteraction();
        }

        controller.SetPinnedRouteSuppressedByTradeWorkspace(false);
        tradeWorkspaceControl.Dispose();
        miningWorkspaceControl.Dispose();
        miningAnalyticsWorkspaceControl.Dispose();
        miningLocationWorkspaceControl.Dispose();
    }
}

