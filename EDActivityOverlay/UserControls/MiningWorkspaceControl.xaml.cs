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
        MiningRingContextService.Instance.Start();
        MiningSessionService.Instance.Changed += OnMiningSessionChanged;
        MiningEngineeringMaterialTrackerService.Instance.Changed += OnMiningEngineeringMaterialsChanged;
        MiningRingContextService.Instance.Changed += OnMiningRingContextChanged;
        MiningMarketPriceService.Instance.Changed += OnMiningMarketPriceChanged;
        LoadTargetInputs();
        RefreshTargetInputEnabledState();
        RequestMarketRefresh();
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
        RequestMarketRefresh();
        RefreshPresentation();
    }

    public void RefreshLocalization()
    {
        LoadTargetInputs();
        RefreshTargetInputEnabledState();
        RefreshPresentation();
    }

    private void OnMiningSessionChanged(object? sender, MiningSessionChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            currentSession = e.Current;
            RequestMarketRefresh();
            RefreshPresentation();
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            currentSession = e.Current;
            RequestMarketRefresh();
            RefreshPresentation();
        }));
    }

    private void OnMiningEngineeringMaterialsChanged(
        object? sender,
        MiningEngineeringMaterialsChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshPresentation();
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(RefreshPresentation));
    }

    private void RefreshPresentation()
    {
        AppSettings settings = SettingsService.Instance.Settings;
        MiningRingContextSnapshot ringContext = CurrentRingContext();
        MiningMarketPriceSnapshot prices = MiningMarketPriceService.Instance.Current;
        MiningTargetSelection selection = CurrentTargetSelection(settings, ringContext);
        string intelligenceTarget = selection.CommodityIds.FirstOrDefault() ?? string.Empty;
        MiningIntelligenceSnapshot intelligence =
            MiningIntelligenceCalculator.Calculate(
                currentSession,
                MiningCollectorTrackerService.Instance.Current,
                intelligenceTarget,
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
        RingContextText.Text = BuildRingContextText(ringContext);
        RingContextText.Visibility = string.IsNullOrWhiteSpace(RingContextText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        MarketContextText.Text = BuildMarketContextText(ringContext, selection, prices);
        MarketContextText.Visibility = string.IsNullOrWhiteSpace(MarketContextText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        MiningProspectSnapshot? prospect = currentSession.Prospects.LastOrDefault();
        if (prospect is null)
        {
            ProspectMetaText.Text = Loc.Get("Loc_MINING_WAITING_PROSPECT");
            ProspectHeadlineText.Text = string.Empty;
            ProspectMaterialsText.Text = BuildTargetLabel(
                selection.CommodityIds,
                effectiveThreshold);
            DecisionText.Text = string.Empty;
            MethodText.Text = string.Empty;
        }
        else
        {
            MiningProspectAdvice advice = MiningProspectorAdvisor.Evaluate(
                prospect,
                selection.CommodityIds,
                effectiveThreshold);

            ProspectMetaText.Text = Loc.Format(
                "Loc_MINING_PROSPECT_META_FORMAT",
                string.IsNullOrWhiteSpace(prospect.Content) ? "—" : prospect.Content,
                prospect.Remaining);

            ProspectHeadlineText.Text = BuildProspectHeadline(prospect, advice);
            ProspectMaterialsText.Text = BuildMaterialsLine(prospect, prices);
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

        string engineeringMaterials = BuildEngineeringMaterialsText(
            MiningEngineeringMaterialTrackerService.Instance.Current);

        EngineeringMaterialsText.Text = engineeringMaterials;
        EngineeringMaterialsText.Visibility =
            string.IsNullOrWhiteSpace(engineeringMaterials)
                ? Visibility.Collapsed
                : Visibility.Visible;

        if (!currentSession.IsActive)
        {
            SessionText.Text = Loc.Get("Loc_MINING_SESSION_IDLE");
            TargetStatsText.Text = BuildTargetLabel(
                selection.CommodityIds,
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
                selection.CommodityIds,
                effectiveThreshold);

            TargetStatsText.Text = selection.CommodityIds.Count == 0
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
        MiningTargetOption[] targets = MiningTargetCatalog.GetLocalizedOptions()
            .Where(item => !string.IsNullOrWhiteSpace(item.CommodityId))
            .ToArray();
        IReadOnlyList<string> selectedTargets =
            MiningTargetSelector.NormalizeManualTargets(settings);

        TargetCommodityListBox.ItemsSource = targets;
        TargetCommodityListBox.SelectedItems.Clear();
        foreach (MiningTargetOption option in targets)
        {
            if (selectedTargets.Contains(
                    option.CommodityId,
                    StringComparer.OrdinalIgnoreCase))
            {
                TargetCommodityListBox.SelectedItems.Add(option);
            }
        }

        AutoTargetsCheckBox.IsChecked = settings.MiningAutoSelectTargets;
        MinimumProportionTextBox.Text = settings.MiningMinimumProportion.ToString(
            "0.#",
            CultureInfo.CurrentCulture);
        RefreshTargetInputEnabledState();
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
        string[] targets = TargetCommodityListBox.SelectedItems
            .Cast<MiningTargetOption>()
            .Select(item => item.CommodityId)
            .ToArray();
        bool automatic = AutoTargetsCheckBox.IsChecked == true;

        double threshold = ParseThreshold(
            MinimumProportionTextBox.Text,
            SettingsService.Instance.Settings.MiningMinimumProportion);

        SettingsService.Instance.SetMiningCopilotSettings(
            targets,
            automatic,
            threshold);
        LoadTargetInputs();
        RequestMarketRefresh();
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
        MiningEngineeringMaterialTrackerService.Instance.Changed -= OnMiningEngineeringMaterialsChanged;
    }
}
