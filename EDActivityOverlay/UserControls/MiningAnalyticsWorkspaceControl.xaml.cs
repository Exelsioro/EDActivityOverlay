using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Mining;
using System.Windows.Threading;

namespace EDActivityOverlay.UserControls;

public partial class MiningAnalyticsWorkspaceControl : System.Windows.Controls.UserControl, IDisposable
{
    private sealed record YieldRow(string Label, int Count, double BarWidth);
    private sealed record RefinedRow(string Name, string Tons);
    private sealed record HistoryRow(
        string When,
        string Location,
        string Tons,
        string Rate,
        string Prospects);

    private GameStateSnapshot currentJournal = GameStateSnapshot.Empty;
    private MiningSessionSnapshot currentSession = MiningSessionSnapshot.Empty;
    private IReadOnlyList<MiningSessionSnapshot> recentSessions = Array.Empty<MiningSessionSnapshot>();
    private readonly DispatcherTimer refreshTimer;
    private bool disposed;

    public MiningAnalyticsWorkspaceControl()
    {
        InitializeComponent();
        currentJournal = JournalMonitorService.Instance.Current;
        currentSession = MiningSessionService.Instance.Current;
        MiningSessionService.Instance.Changed += OnMiningSessionChanged;
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        refreshTimer.Tick += RefreshTimer_Tick;
        refreshTimer.Start();
        ReloadHistory();
        RefreshPresentation();
    }

    public event Action? BackRequested;
    public event Action? CloseRequested;

    public void UpdateJournalState(GameStateSnapshot state)
    {
        currentJournal = state ?? GameStateSnapshot.Empty;
        RefreshPresentation();
    }

    public void RefreshLocalization()
    {
        ReloadHistory();
        RefreshPresentation();
    }

    public void ReloadHistory()
    {
        recentSessions = MiningSessionService.Instance.LoadRecentSessions(100);
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (IsVisible && currentSession.IsActive)
        {
            RefreshPresentation();
        }
    }

    private void OnMiningSessionChanged(object? sender, MiningSessionChangedEventArgs e)
    {
        void Apply()
        {
            currentSession = e.Current;
            if (e.CompletedSession is not null)
            {
                ReloadHistory();
            }
            RefreshPresentation();
        }

        if (Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(Apply));
        }
    }

    private void RefreshPresentation()
    {
        AppSettings settings = SettingsService.Instance.Settings;
        MiningSessionAnalyticsSnapshot analytics = MiningSessionAnalyticsCalculator.Calculate(
            currentSession,
            settings.MiningTargetCommodity,
            settings.MiningMinimumProportion);

        ContextText.Text = BuildContext();
        RateText.Text = analytics.RateReady
            ? Loc.Format("Loc_MINING_RATE_VALUE", analytics.TonsPerHour)
            : Loc.Get("Loc_MINING_RATE_WARMING");
        RateMetaText.Text = Loc.Format(
            "Loc_MINING_RATE_META",
            currentSession.RefinedTons,
            FormatDuration(analytics.Duration),
            analytics.RefinementsPerMinute);

        QualityText.Text = string.IsNullOrWhiteSpace(settings.MiningTargetCommodity)
            ? Loc.Get("Loc_MINING_TARGET_HINT")
            : Loc.Format(
                "Loc_MINING_QUALITY_VALUE",
                analytics.Target.HitRate * 100,
                analytics.Target.AcceptanceRate * 100);
        QualityMetaText.Text = string.IsNullOrWhiteSpace(settings.MiningTargetCommodity)
            ? string.Empty
            : Loc.Format(
                "Loc_MINING_QUALITY_META",
                analytics.Target.AverageProportion,
                analytics.Target.MedianProportion,
                analytics.TargetP75,
                analytics.Target.BestProportion);

        int capacity = currentSession.IsActive && currentSession.CargoCapacity > 0
            ? currentSession.CargoCapacity
            : currentJournal.CargoCapacity;
        int used = currentSession.IsActive
            ? currentSession.CargoUsed
            : currentJournal.CargoUsed;
        CargoText.Text = capacity > 0
            ? Loc.Format("Loc_MINING_ANALYTICS_CARGO", used, capacity, analytics.CargoFill * 100)
            : Loc.Get("Loc_cargo_unknown");
        EfficiencyText.Text = Loc.Format(
            "Loc_MINING_EFFICIENCY_META",
            analytics.ProspectsPerTon,
            analytics.ProspectorsPerTon,
            analytics.CoresPerHour,
            analytics.EstimatedTimeToFull is { } eta
                ? "~" + FormatDuration(eta)
                : "—");

        TargetText.Text = string.IsNullOrWhiteSpace(settings.MiningTargetCommodity)
            ? Loc.Get("Loc_MINING_TARGET_HINT")
            : Loc.Format(
                "Loc_MINING_TARGET_FORMAT",
                settings.MiningTargetCommodity,
                settings.MiningMinimumProportion);

        double maxShare = analytics.YieldBuckets.Count == 0
            ? 0
            : analytics.YieldBuckets.Max(item => item.Share);
        YieldItemsControl.ItemsSource = analytics.YieldBuckets
            .Select(item => new YieldRow(
                item.Label,
                item.Count,
                maxShare <= 0 ? 0 : Math.Round(170 * item.Share / maxShare)))
            .ToArray();

        RefinedItemsControl.ItemsSource = currentSession.Refinements
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.DisplayName)
                    ? item.CommodityId
                    : item.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => new RefinedRow(
                group.Key,
                Loc.Format("Loc_MINING_TONS_VALUE", group.Count())))
            .ToArray();

        RefreshHistory(settings);
        FooterText.Text = Loc.Get("Loc_MINING_ANALYTICS_LIMITATION");
    }

    private void RefreshHistory(AppSettings settings)
    {
        MiningHistoryAnalyticsSnapshot summary = MiningSessionAnalyticsCalculator.CalculateHistory(
            recentSessions,
            settings.MiningTargetCommodity,
            settings.MiningMinimumProportion);
        HistorySummaryText.Text = summary.Sessions == 0
            ? Loc.Get("Loc_MINING_HISTORY_EMPTY")
            : Loc.Format(
                "Loc_MINING_HISTORY_SUMMARY",
                summary.Sessions,
                summary.RefinedTons,
                FormatDuration(summary.TotalDuration),
                summary.AverageTonsPerHour,
                summary.BestTonsPerHour,
                string.IsNullOrWhiteSpace(summary.BestLocation) ? "—" : summary.BestLocation);

        HistoryGrid.ItemsSource = recentSessions
            .Take(100)
            .Select(session =>
            {
                MiningSessionAnalyticsSnapshot analytics = MiningSessionAnalyticsCalculator.Calculate(
                    session,
                    settings.MiningTargetCommodity,
                    settings.MiningMinimumProportion,
                    session.EndedUtc ?? session.LastActivityUtc);
                return new HistoryRow(
                    session.StartedUtc.ToLocalTime().ToString("g"),
                    BuildLocation(session),
                    Loc.Format("Loc_MINING_TONS_VALUE", session.RefinedTons),
                    analytics.RateReady
                        ? Loc.Format("Loc_MINING_RATE_VALUE", analytics.TonsPerHour)
                        : "—",
                    session.ProspectedAsteroids.ToString());
            })
            .ToArray();
    }

    private string BuildContext()
    {
        MiningSessionSnapshot session = currentSession;
        string system = session.IsActive && !string.IsNullOrWhiteSpace(session.SystemName)
            ? session.SystemName
            : currentJournal.StarSystem;
        string location = BuildLocation(session);
        if (string.IsNullOrWhiteSpace(location))
        {
            location = system;
        }

        return string.IsNullOrWhiteSpace(location)
            ? Loc.Get("Loc_waiting_for_location")
            : location;
    }

    private static string BuildLocation(MiningSessionSnapshot session)
    {
        return string.Join(
            " / ",
            new[] { session.SystemName, session.RingName, session.BodyName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}:{duration.Minutes:00}";
        }

        return $"{Math.Max(0, (int)Math.Ceiling(duration.TotalMinutes))}m";
    }

    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
        BackRequested?.Invoke();

    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
        CloseRequested?.Invoke();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        refreshTimer.Stop();
        refreshTimer.Tick -= RefreshTimer_Tick;
        MiningSessionService.Instance.Changed -= OnMiningSessionChanged;
    }
}
