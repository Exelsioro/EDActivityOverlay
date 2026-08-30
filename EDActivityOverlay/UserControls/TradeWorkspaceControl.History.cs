using System.Windows;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl
{
    private sealed record TradeHistoryRow(
        string Time,
        string Commodity,
        string Route,
        string Cargo,
        string Profit,
        string Duration,
        string ProfitPerHour,
        string PlanVariance,
        string Meta);

    private readonly TradeHistoryService tradeHistoryService =
        TradeHistoryService.Instance;

    private bool historyVisible;

    private void InitializeTradeHistory()
    {
        tradeHistoryService.HistoryChanged +=
            TradeHistoryService_HistoryChanged;

        RefreshTradeHistory();
    }

    private void DisposeTradeHistory()
    {
        tradeHistoryService.HistoryChanged -=
            TradeHistoryService_HistoryChanged;
    }

    private void TradeHistoryService_HistoryChanged(
        object? sender,
        EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshTradeHistory();
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(
                RefreshTradeHistory));
    }

    private void HistoryButton_Click(
        object sender,
        RoutedEventArgs e) =>
        SetHistoryVisible(
            !historyVisible);

    private void CloseHistoryButton_Click(
        object sender,
        RoutedEventArgs e) =>
        SetHistoryVisible(
            false);

    private void SetHistoryVisible(
        bool visible)
    {
        historyVisible =
            visible;

        TradeHistoryPanel.Visibility =
            visible
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (visible)
        {
            RefreshTradeHistory();
        }
    }

    private void RefreshTradeHistory()
    {
        TradeHistorySnapshot snapshot =
            tradeHistoryService.Snapshot(
                recentLimit:
                    250);

        HistorySessionProfitText.Text =
            snapshot.Session.Profit.ToString(
                "N0");

        HistorySessionTradesText.Text =
            snapshot.Session.Trades.ToString(
                "N0");

        HistoryAllProfitText.Text =
            snapshot.AllTime.Profit.ToString(
                "N0");

        HistoryAverageRateText.Text =
            snapshot.AllTime.ProfitPerHour.ToString(
                "N0");

        HistorySummaryMetaText.Text =
            Loc.Format(
                "Loc_TRADE_HISTORY_SUMMARY_META",
                snapshot.AllTime.TotalCargoSold,
                FormatHistoryDuration(
                    snapshot.AllTime.Duration),
                snapshot.AllTime.BestTradeProfit);

        TradeHistoryList.ItemsSource =
            snapshot.Recent
                .Select(ToHistoryRow)
                .ToArray();

        HistoryEmptyText.Visibility =
            snapshot.Recent.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private static TradeHistoryRow ToHistoryRow(
        TradeHistoryRecord record)
    {
        long sold =
            record.Legs.Sum(leg =>
                (long)Math.Max(
                    0,
                    leg.SoldQuantity));

        string meta =
            record.RerouteCount > 0
                ? Loc.Format(
                    "Loc_TRADE_HISTORY_REROUTES",
                    record.RerouteCount)
                : record.RouteKind.Equals(
                    "roundtrip",
                    StringComparison.OrdinalIgnoreCase)
                    ? Loc.Get(
                        "Loc_TRADE_MODE_ROUND_TRIP")
                    : Loc.Get(
                        "Loc_TRADE_MODE_ONE_WAY");

        return new TradeHistoryRow(
            record.CompletedAtUtc
                .ToLocalTime()
                .ToString(
                    "dd.MM HH:mm"),
            string.IsNullOrWhiteSpace(
                record.CommoditySummary)
                ? "—"
                : record.CommoditySummary.ToUpperInvariant(),
            string.IsNullOrWhiteSpace(
                record.RouteSummary)
                ? "—"
                : record.RouteSummary,
            Loc.Format(
                "Loc_TRADE_HISTORY_CARGO_FORMAT",
                sold),
            Loc.Format(
                "Loc_Credits_Format",
                record.ActualProfit),
            FormatHistoryDuration(
                record.Duration),
            Loc.Format(
                "Loc_TRADE_CRH_RAW",
                record.ActualProfitPerHour),
            $"{record.VariancePercent:+0.0;-0.0;0.0}%",
            meta);
    }

    private static string FormatHistoryDuration(
        TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}";
        }

        return $"{value.Minutes}:{value.Seconds:00}";
    }
}
