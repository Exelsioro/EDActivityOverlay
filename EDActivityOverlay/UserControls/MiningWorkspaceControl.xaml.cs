using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Mining;
using EDActivityOverlay.Services.Trading;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.UserControls;

public partial class MiningWorkspaceControl : UserControl, IDisposable
{
    private GameStateSnapshot currentJournal = GameStateSnapshot.Empty;
    private MiningSessionSnapshot currentSession = MiningSessionSnapshot.Empty;
    private bool disposed;

    public MiningWorkspaceControl()
    {
        InitializeComponent();
        currentJournal = JournalMonitorService.Instance.Current;
        currentSession = MiningSessionService.Instance.Current;
        MiningSessionService.Instance.Changed += OnMiningSessionChanged;
        LoadTargetInputs();
        RefreshPresentation();
    }

    public event Action? CloseRequested;
    public event Action? DragRequested;
    public event Action? FullRequested;

    public void SetChromeStyle(string? style)
    {
        OverlayChromeHelper.Apply(
            CompactMiningPanel,
            OverlayChromeStyles.Normalize(style));
    }

    public void UpdateJournalState(GameStateSnapshot state)
    {
        currentJournal = state ?? GameStateSnapshot.Empty;
        RefreshPresentation();
    }

    public void RefreshLocalization()
    {
        RefreshPresentation();
    }

    private void OnMiningSessionChanged(object? sender, MiningSessionChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            currentSession = e.Current;
            RefreshPresentation();
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            currentSession = e.Current;
            RefreshPresentation();
        }));
    }

    private void RefreshPresentation()
    {
        AppSettings settings = SettingsService.Instance.Settings;
        MiningIntelligenceSnapshot intelligence =
            MiningIntelligenceCalculator.Calculate(
                currentSession,
                MiningCollectorTrackerService.Instance.Current,
                settings.MiningTargetCommodity,
                settings.MiningMinimumProportion);

        double effectiveThreshold =
            intelligence.AdaptiveThreshold.Ready
                ? intelligence.AdaptiveThreshold.Suggested
                : settings.MiningMinimumProportion;

        string location = currentSession.IsActive && !string.IsNullOrWhiteSpace(currentSession.SystemName)
            ? currentSession.SystemName
            : currentJournal.StarSystem;
        string ring = currentSession.IsActive
            ? currentSession.RingName
            : string.Empty;

        CompactJournalContextText.Text = string.Join(
            "  •  ",
            new[] { location, ring }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        MiningProspectSnapshot? prospect = currentSession.Prospects.LastOrDefault();
        if (prospect is null)
        {
            ProspectMetaText.Text = Loc.Get("Loc_MINING_WAITING_PROSPECT");
            ProspectHeadlineText.Text = string.Empty;
            ProspectMaterialsText.Text = Loc.Get("Loc_MINING_TARGET_HINT");
            DecisionText.Text = string.Empty;
            MethodText.Text = string.Empty;
        }
        else
        {
            MiningProspectAdvice advice = MiningProspectorAdvisor.Evaluate(
                prospect,
                settings.MiningTargetCommodity,
                effectiveThreshold);

            ProspectMetaText.Text = Loc.Format(
                "Loc_MINING_PROSPECT_META_FORMAT",
                string.IsNullOrWhiteSpace(prospect.Content) ? "—" : prospect.Content,
                prospect.Remaining);

            ProspectHeadlineText.Text = BuildProspectHeadline(prospect, advice);
            ProspectMaterialsText.Text = BuildMaterialsLine(prospect);
            DecisionText.Text = Loc.Get(advice.Decision switch
            {
                MiningProspectDecision.Mine => "Loc_MINING_DECISION_MINE",
                MiningProspectDecision.Skip => "Loc_MINING_DECISION_SKIP",
                MiningProspectDecision.Core => "Loc_MINING_DECISION_CORE",
                _ => "Loc_MINING_DECISION_NO_TARGET"
            });

            MiningExtractionMethod method = advice.RecommendedMethod;
            string methodLabel = Loc.Get(method switch
            {
                MiningExtractionMethod.Laser => "Loc_MINING_METHOD_LASER",
                MiningExtractionMethod.Core => "Loc_MINING_METHOD_CORE",
                _ => "Loc_MINING_METHOD_UNKNOWN"
            });

            string methodSubject = method == MiningExtractionMethod.Core
                ? prospect.MotherlodeDisplayName
                : string.Empty;

            MethodText.Text = string.IsNullOrWhiteSpace(methodSubject)
                ? Loc.Format("Loc_MINING_METHOD_FORMAT", methodLabel)
                : Loc.Format("Loc_MINING_METHOD_WITH_SUBJECT_FORMAT", methodLabel, methodSubject);
        }

        int cargoCapacity = currentSession.IsActive && currentSession.CargoCapacity > 0
            ? currentSession.CargoCapacity
            : currentJournal.CargoCapacity;
        int cargoUsed = currentSession.IsActive
            ? currentSession.CargoUsed
            : currentJournal.CargoUsed;
        int limpets = currentSession.IsActive
            ? currentSession.LimpetsRemaining
            : GetJournalLimpets(currentJournal);

        CargoText.Text = cargoCapacity > 0
            ? Loc.Format("Loc_MINING_CARGO_FORMAT", cargoUsed, cargoCapacity, limpets)
            : Loc.Format("Loc_MINING_CARGO_UNKNOWN_FORMAT", limpets);

        IntelligenceText.Text = BuildIntelligenceText(intelligence);

        if (!currentSession.IsActive)
        {
            SessionText.Text = Loc.Get("Loc_MINING_SESSION_IDLE");
            TargetStatsText.Text = string.IsNullOrWhiteSpace(settings.MiningTargetCommodity)
                ? Loc.Get("Loc_MINING_TARGET_HINT")
                : Loc.Format(
                    "Loc_MINING_TARGET_FORMAT",
                    settings.MiningTargetCommodity,
                    settings.MiningMinimumProportion);
        }
        else
        {
            TimeSpan duration = DateTimeOffset.UtcNow - currentSession.StartedUtc;
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            SessionText.Text = Loc.Format(
                "Loc_MINING_SESSION_META_FORMAT",
                FormatDuration(duration),
                currentSession.ProspectedAsteroids,
                currentSession.RefinedTons,
                currentSession.CrackedAsteroids,
                currentSession.ProspectorsLaunched,
                currentSession.CollectorsLaunched);

            MiningTargetStatistics stats = MiningTargetAnalytics.Calculate(
                currentSession,
                settings.MiningTargetCommodity,
                effectiveThreshold);

            TargetStatsText.Text = string.IsNullOrWhiteSpace(settings.MiningTargetCommodity)
                ? Loc.Get("Loc_MINING_TARGET_HINT")
                : Loc.Format(
                    "Loc_MINING_TARGET_STATS_FORMAT",
                    stats.HitRate * 100,
                    stats.AcceptanceRate * 100,
                    stats.AverageProportion,
                    stats.MedianProportion,
                    stats.BestProportion);
        }

        FooterText.Text = BuildLoadoutFooter();
    }

    private static string BuildProspectHeadline(
        MiningProspectSnapshot prospect,
        MiningProspectAdvice advice)
    {
        if (advice.TargetFound && !string.IsNullOrWhiteSpace(advice.MatchedDisplayName))
        {
            return advice.TargetProportion is { } proportion
                ? Loc.Format(
                    "Loc_MINING_PROSPECT_HEADLINE_FORMAT",
                    advice.MatchedDisplayName,
                    proportion)
                : advice.MatchedDisplayName;
        }

        if (prospect.HasMotherlode)
        {
            return Loc.Format(
                "Loc_MINING_PROSPECT_CORE_FORMAT",
                string.IsNullOrWhiteSpace(prospect.MotherlodeDisplayName)
                    ? prospect.MotherlodeCommodityId
                    : prospect.MotherlodeDisplayName);
        }

        MiningProspectMaterialSnapshot? leading = prospect.Materials.FirstOrDefault();
        return leading is null
            ? Loc.Get("Loc_No_prospect_data")
            : Loc.Format(
                "Loc_MINING_PROSPECT_HEADLINE_FORMAT",
                leading.DisplayName,
                leading.Proportion);
    }

    private static string BuildMaterialsLine(MiningProspectSnapshot prospect)
    {
        string materials = string.Join(
            "  •  ",
            prospect.Materials
                .Take(3)
                .Select(item => $"{item.DisplayName} {item.Proportion:0.#}%"));

        if (!prospect.HasMotherlode)
        {
            return string.IsNullOrWhiteSpace(materials)
                ? Loc.Get("Loc_No_prospect_data")
                : materials;
        }

        string core = Loc.Format(
            "Loc_MINING_CORE_MATERIAL_FORMAT",
            string.IsNullOrWhiteSpace(prospect.MotherlodeDisplayName)
                ? prospect.MotherlodeCommodityId
                : prospect.MotherlodeDisplayName);

        return string.IsNullOrWhiteSpace(materials)
            ? core
            : core + Environment.NewLine + materials;
    }

    private static int GetJournalLimpets(GameStateSnapshot state)
    {
        string droneId = CommodityIdentity.Normalize("drones");
        return state.CargoByCommodityId.TryGetValue(droneId, out CargoCommoditySnapshot? drones)
            ? Math.Max(0, drones.Count)
            : 0;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}:{duration.Minutes:00}";
        }

        return $"{Math.Max(0, (int)duration.TotalMinutes)}m";
    }

    private void LoadTargetInputs()
    {
        AppSettings settings = SettingsService.Instance.Settings;
        TargetCommodityTextBox.Text = settings.MiningTargetCommodity;
        MinimumProportionTextBox.Text = settings.MiningMinimumProportion.ToString(
            "0.#",
            CultureInfo.CurrentCulture);
    }

    private void ApplyTargetButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyTargetInputs();
    }

    private void TargetInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyTargetInputs();
        e.Handled = true;
    }

    private void ApplyTargetInputs()
    {
        string target = TargetCommodityTextBox.Text.Trim();
        double threshold = ParseThreshold(
            MinimumProportionTextBox.Text,
            SettingsService.Instance.Settings.MiningMinimumProportion);

        SettingsService.Instance.SetMiningCopilotSettings(target, threshold);
        LoadTargetInputs();
        RefreshPresentation();
    }

    private static double ParseThreshold(string value, double fallback)
    {
        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out double parsed)
            || double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed))
        {
            return Math.Clamp(parsed, 0, 100);
        }

        return Math.Clamp(fallback, 0, 100);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke();

    private void FullAnalyticsButton_Click(object sender, RoutedEventArgs e) =>
        FullRequested?.Invoke();

    private void CompactMiningDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragRequested?.Invoke();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        MiningSessionService.Instance.Changed -= OnMiningSessionChanged;
    }
}
