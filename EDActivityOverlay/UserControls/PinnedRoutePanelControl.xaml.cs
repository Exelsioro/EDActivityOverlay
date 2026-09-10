using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models.Trading;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.UserControls;

/// <summary>
/// Read-only reusable presentation of the current pinned trade route. Route
/// execution still belongs to the legacy PinnedRouteOverlay during migration;
/// this control consumes the shared presentation snapshot so the VR composite
/// does not create a second TradeRouteProgressTracker.
/// </summary>
public partial class PinnedRoutePanelControl : UserControl, IDisposable
{
    private bool disposed;

    public event EventHandler? ContentChanged;

    public bool IsPinned { get; private set; }
    public bool IsSuppressedByTradeWorkspace { get; private set; }

    public double PreferredHeight => 184;

    public PinnedRoutePanelControl()
    {
        InitializeComponent();
        PinnedRoutePresentationService.Instance.Changed += OnPresentationChanged;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        RefreshFromState();
    }

    private void OnPresentationChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshFromState));
            return;
        }

        RefreshFromState();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => ApplySettings(e.Settings)));
            return;
        }

        ApplySettings(e.Settings);
    }

    private void ApplySettings(AppSettings settings) =>
        OverlayChromeHelper.Apply(
            OverlayFrame,
            settings.OverlayChromeStyle);

    public void RefreshLocalization() =>
        RefreshFromState();

    private void RefreshFromState()
    {
        PinnedRoutePresentationSnapshot snapshot =
            PinnedRoutePresentationService.Instance.Current;

        IsPinned = snapshot.IsPinned;
        IsSuppressedByTradeWorkspace = snapshot.SuppressedByTradeWorkspace;
        ApplySettings(SettingsService.Instance.Settings);

        if (snapshot.Route is not { } route)
        {
            ContentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        ApplyRoute(route, snapshot.Progress);
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

        if (route.IsCargoSaleOnly)
        {
            ProfitText.Text =
                progress.Stage == TradeRouteStage.Completed
                    ? Loc.Format(
                        "Loc_TRADE_CARGO_PIN_SOLD_VALUE",
                        progress.SaleRevenue)
                    : Loc.Format(
                        "Loc_TRADE_CARGO_PIN_PLANNED_VALUE",
                        route.PlannedSaleValue);
        }
        else
        {
            ProfitText.Text =
                progress.Stage == TradeRouteStage.Completed
                    ? Loc.Format(
                        "Loc_Actual_Profit_Format",
                        progress.ActualProfit)
                    : Loc.Format(
                        "Loc_Planned_Profit_Format",
                        route.TotalProfitPerTrip);
        }

        FromPointText.Text =
            FormatRoutePoint(
                route.CardHeader.FromStation.System,
                route.CardHeader.FromStation.Name);

        ToPointText.Text =
            FormatRoutePoint(
                route.CardHeader.ToStation.System,
                route.CardHeader.ToStation.Name);

        NoteText.Text = progress.Note;
        NoteText.Foreground =
            progress.IsInDanger
                ? (System.Windows.Media.Brush)FindResource("FailureColorBrush")
                : (System.Windows.Media.Brush)FindResource("MutedTextColorBrush");
    }

    private static string FormatRoutePoint(
        string system,
        string station)
    {
        string normalizedSystem =
            system.Trim().ToUpperInvariant();
        string normalizedStation =
            station.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedStation))
        {
            return normalizedSystem;
        }
        if (string.IsNullOrWhiteSpace(normalizedSystem))
        {
            return normalizedStation;
        }

        return $"{normalizedSystem}  /  {normalizedStation}";
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        PinnedRoutePresentationService.Instance.Changed -= OnPresentationChanged;
        SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
    }
}
