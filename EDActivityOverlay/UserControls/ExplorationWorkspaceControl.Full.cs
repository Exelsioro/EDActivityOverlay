using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Exploration;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Navigation;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.UserControls;

public partial class ExplorationWorkspaceControl
{
    private readonly SpanshRouteClient spanshRouteClient = new();
    private bool fullExplorationVisible;
    private bool updatingDssTarget;
    private bool externalOverlayActive;
    private ExplorationSystemCatalog catalog =
        ExplorationSystemCatalog.Empty;
    private CancellationTokenSource? routeNavigationCancellation;

    private sealed record CatalogFilterOption(
        string Value,
        string LabelKey)
    {
        public string Label => Loc.Get(LabelKey);
    }

    private static readonly CatalogFilterOption[] CatalogFilters =
    [
        new("All", "Loc_FILTER_ALL_BODIES"),
        new("Notable", "Loc_FILTER_NOTABLE"),
        new("Valuable", "Loc_FILTER_VALUABLE"),
        new("Biological", "Loc_FILTER_BIOLOGICAL"),
        new("Remaining", "Loc_FILTER_REMAINING"),
        new("Deferred", "Loc_FILTER_DEFERRED"),
        new("Completed", "Loc_FILTER_COMPLETED"),
        new("Unmapped", "Loc_FILTER_UNMAPPED"),
        new("FirstDiscovery", "Loc_FILTER_FIRST_DISCOVERY"),
        new("FirstMapping", "Loc_FILTER_FIRST_MAPPING"),
        new("Landable", "Loc_FILTER_LANDABLE")
    ];

    public event Action<bool>? ViewModeChanged;

    public bool IsFullMode => fullExplorationVisible;

    public Func<
        string,
        bool,
        CancellationToken,
        Task<EliteNavigationResult>>? NavigateAsync { get; set; }

    private void InitializeFullWorkspace()
    {
        DssTargetComboBox.ItemsSource =
            Enumerable.Range(
                DssProbePatternCatalog.MinimumTarget,
                DssProbePatternCatalog.MaximumTarget
                - DssProbePatternCatalog.MinimumTarget
                + 1);

        SetDssTarget(
            SettingsService.Instance.Settings.DssEfficiencyTarget);

        CatalogFilterComboBox.ItemsSource =
            CatalogFilters;
        CatalogFilterComboBox.SelectedIndex =
            0;

        ApplyRoutePanelState();
        ApplySurfaceVisibility();

        ExplorationPoiService.Instance.PoiChanged +=
            OnFullExplorationPoiChanged;
        ExplorationEarningsService.Instance.Changed +=
            OnFullExplorationEarningsChanged;
        ExplorationLogService.Instance.Changed +=
            OnFullExplorationLogChanged;
    }

    private void DisposeFullWorkspace()
    {
        ExplorationPoiService.Instance.PoiChanged -=
            OnFullExplorationPoiChanged;
        ExplorationEarningsService.Instance.Changed -=
            OnFullExplorationEarningsChanged;
        ExplorationLogService.Instance.Changed -=
            OnFullExplorationLogChanged;

        routeNavigationCancellation?.Cancel();
        routeNavigationCancellation?.Dispose();
        routeNavigationCancellation = null;

        spanshRouteClient.Dispose();
    }

    private void RefreshFullWorkspace(
        GameStateSnapshot state)
    {
        ExplorationDataState externalData =
            ExplorationDataService.Instance.Current;

        if (string.IsNullOrWhiteSpace(
                SpanshSourceTextBox.Text)
            && !string.IsNullOrWhiteSpace(
                state.StarSystem))
        {
            SpanshSourceTextBox.Text =
                state.StarSystem;
        }

        RefreshCatalog(
            state,
            externalData);

        FullOverviewText.Text =
            BuildFullOverview(
                state,
                externalData);

        RefreshExplorationLog();
    }

    private void RefreshFullLocalization()
    {
        if (CatalogFilterComboBox is null)
        {
            return;
        }

        string selectedFilter =
            (CatalogFilterComboBox.SelectedItem
                as CatalogFilterOption)?.Value
            ?? "All";

        CatalogFilterComboBox.ItemsSource =
            null;
        CatalogFilterComboBox.ItemsSource =
            CatalogFilters;
        CatalogFilterComboBox.SelectedItem =
            CatalogFilters.FirstOrDefault(
                item =>
                    item.Value
                    == selectedFilter)
            ?? CatalogFilters[0];

        ApplyRoutePanelState();
    }

    private void ApplyFullWorkspaceSettings(
        AppSettings settings)
    {
        ApplyRoutePanelState();
    }

    public void SetExternalOverlayContent(
        FrameworkElement? content)
    {
        ExternalOverlayHost.Content =
            content;
    }

    public void SetExternalOverlayActive(
        bool active)
    {
        externalOverlayActive =
            active
            && !fullExplorationVisible;

        ApplySurfaceVisibility();
    }

    private void ApplySurfaceVisibility()
    {
        if (fullExplorationVisible)
        {
            CompactExplorationPanel.Visibility =
                Visibility.Collapsed;
            FullExplorationPanel.Visibility =
                Visibility.Visible;
            ExternalOverlayHost.Visibility =
                Visibility.Collapsed;
            return;
        }

        FullExplorationPanel.Visibility =
            Visibility.Collapsed;

        CompactExplorationPanel.Visibility =
            externalOverlayActive
                ? Visibility.Collapsed
                : Visibility.Visible;

        ExternalOverlayHost.Visibility =
            externalOverlayActive
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void OnFullExplorationPoiChanged(
        object? sender,
        ExplorationPoiChangedEventArgs e) =>
        DispatchRefresh();

    private void OnFullExplorationEarningsChanged(
        object? sender,
        ExplorationEarningsChangedEventArgs e) =>
        DispatchRefresh();

    private void OnFullExplorationLogChanged(
        object? sender,
        ExplorationLogChangedEventArgs e)
    {
        if (!fullExplorationVisible)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            RefreshExplorationLog();
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(
                RefreshExplorationLog));
    }

    private void RefreshCatalog(GameStateSnapshot state, ExplorationDataState externalData)
    {
        ExplorationSystemHistorySnapshot history = ExplorationHistoryService.Instance.LoadSystem(state);
        catalog = ExplorationSystemCatalogBuilder.Build(
            state,
            externalData,
            SettingsService.Instance.Settings.ExplorationSpoilerMode,
            history);
        FullSystemSummaryText.Text = Loc.Format(
            "Loc_Exploration_full_summary_format",
            string.IsNullOrWhiteSpace(catalog.SystemName) ? Loc.Get("Loc_SYSTEM") : catalog.SystemName,
            catalog.KnownBodyCount,
            catalog.Bodies.Count(body => body.IsNotable));
        CurrentSystemLinkText.Text = catalog.SystemName;
        CurrentSystemLinkText.IsEnabled = !string.IsNullOrWhiteSpace(catalog.SystemName);
        string sourceMode = Loc.Format(
            "Loc_Exploration_catalog_source_format",
            Loc.Get(catalog.SpoilerMode switch
            {
                ExplorationSpoilerModes.JournalOnly => "Loc_Exploration_spoilers_journal_only",
                ExplorationSpoilerModes.FullCatalog => "Loc_Exploration_spoilers_full_catalog",
                _ => "Loc_Exploration_spoilers_enrich_scanned"
            }));
        ExplorationHistoryImportState import = ExplorationHistoryService.Instance.ImportState;
        ExplorationVisitQueueSnapshot visitQueue =
            ExplorationVisitStateService.Instance.Current;

        string queueSummary = QueueMatchesSystem(
            visitQueue,
            state)
            ? Loc.Format(
                "Loc_EXPLORATION_QUEUE_FULL_FORMAT",
                visitQueue.RemainingCount,
                visitQueue.DeferredCount,
                visitQueue.CompletedCount)
            : string.Empty;

        CatalogSourceText.Text =
            sourceMode
            + Environment.NewLine
            + (import.IsRunning
                ? Loc.Format(
                    "Loc_Exploration_history_import_progress_format",
                    import.ProcessedFiles,
                    import.TotalFiles)
                : Loc.Format(
                    "Loc_Exploration_history_status_format",
                    history.Bodies.Count))
            + (string.IsNullOrWhiteSpace(queueSummary)
                ? string.Empty
                : Environment.NewLine + queueSummary);
        ExplorationRoutePlan route = ExplorationRouteService.Instance.Current;
        ExplorationRouteInfoText.Text = BuildFullRouteSummary(route);
        NextRouteSystemLink.Text = route.NextStop?.System ?? string.Empty;
        NextRouteSystemLink.Tag = route.NextStop?.System;
        NextRouteSystemLink.Visibility = route.NextStop is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshRouteStops(route);
        ExplorationPoiSnapshot? poi = ExplorationPoiService.Instance.Current.Closest;
        FullPoiPanel.Visibility = poi is null ? Visibility.Collapsed : Visibility.Visible;
        FullPoiTitleText.Text = poi?.Name ?? string.Empty;
        FullPoiMetaText.Text = poi is null
            ? string.Empty
            : string.Join("  •  ", new[]
            {
                poi.System,
                poi.DistanceLy > 0 ? $"{poi.DistanceLy:0.#} ly" : string.Empty,
                poi.Category,
                poi.Region
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        FullPoiSummaryText.Text = poi?.Summary ?? string.Empty;
        CopyPoiSystemButton.IsEnabled = !string.IsNullOrWhiteSpace(poi?.System);
        PlotPoiRouteButton.IsEnabled = !string.IsNullOrWhiteSpace(poi?.System);
        OpenPoiDetailsButton.IsEnabled = Uri.TryCreate(poi?.Url, UriKind.Absolute, out _);
        ApplyCatalogFilter();
    }

    private void ApplyCatalogFilter()
    {
        if (ExplorationBodiesGrid is null)
        {
            return;
        }

        string search =
            CatalogSearchTextBox?.Text.Trim()
            ?? string.Empty;

        string filter =
            (CatalogFilterComboBox?.SelectedItem
                as CatalogFilterOption)?.Value
            ?? "All";

        GameStateSnapshot state =
            JournalMonitorService.Instance.Current;

        ExplorationVisitQueueSnapshot queue =
            ExplorationVisitStateService.Instance.Current;

        Dictionary<int, ExplorationVisitDisposition> dispositions =
            BuildVisitDispositionMap(
                state,
                queue);

        CatalogRow[] rows = catalog.Bodies
            .Where(body =>
                string.IsNullOrWhiteSpace(search)
                || body.Name.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase)
                || body.Subtype.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase)
                || body.Atmosphere.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase))
            .Where(body =>
            {
                dispositions.TryGetValue(
                    body.BodyId,
                    out ExplorationVisitDisposition disposition);

                bool hasVisitState =
                    dispositions.ContainsKey(body.BodyId);

                return filter switch
                {
                    "Notable" => body.IsNotable,
                    "Valuable" => body.IsValuable,
                    "Biological" => body.IsBiological,
                    "Remaining" =>
                        hasVisitState
                        && disposition
                            is ExplorationVisitDisposition.Active
                            or ExplorationVisitDisposition.Recommended,
                    "Deferred" =>
                        hasVisitState
                        && disposition
                            == ExplorationVisitDisposition.Deferred,
                    "Completed" =>
                        hasVisitState
                        && disposition
                            == ExplorationVisitDisposition.Complete,
                    "Unmapped" =>
                        !body.MappedThisVisit
                        && !body.MappedPreviously,
                    "FirstDiscovery" =>
                        ExplorationDiscoveryStatus.IsFirstDiscoveryCandidate(body),
                    "FirstMapping" =>
                        ExplorationDiscoveryStatus.IsFirstMappingCandidate(body),
                    "Landable" => body.Landable,
                    _ => true
                };
            })
            .OrderBy(body =>
                VisitSortOrder(
                    dispositions.TryGetValue(
                        body.BodyId,
                        out ExplorationVisitDisposition disposition)
                        ? disposition
                        : null))
            .ThenByDescending(body => body.IsBiological)
            .ThenByDescending(body => body.IsValuable)
            .ThenByDescending(
                body => body.EstimatedMappingValue)
            .ThenBy(
                body => body.DistanceFromArrivalLs)
            .Select(body =>
                ToCatalogRow(
                    body,
                    dispositions.TryGetValue(
                        body.BodyId,
                        out ExplorationVisitDisposition disposition)
                        ? disposition
                        : null))
            .ToArray();

        CatalogRow? previousSelection =
            ExplorationBodiesGrid.SelectedItem
            as CatalogRow;

        ExplorationBodiesGrid.ItemsSource =
            rows;

        CatalogCountText.Text =
            Loc.Format(
                "Loc_Exploration_catalog_count_format",
                rows.Length,
                catalog.Bodies.Count);

        CatalogRow? preservedSelection =
            previousSelection is null
                ? null
                : rows.FirstOrDefault(
                    row =>
                        previousSelection.Body.BodyId >= 0
                            ? row.Body.BodyId
                              == previousSelection.Body.BodyId
                            : row.Name.Equals(
                                previousSelection.Name,
                                StringComparison.OrdinalIgnoreCase));

        if (preservedSelection is not null)
        {
            ExplorationBodiesGrid.SelectedItem =
                preservedSelection;
        }
        else if (rows.Length > 0)
        {
            ExplorationBodiesGrid.SelectedIndex =
                0;
        }
        else
        {
            ShowSelectedBody(
                null);
        }
    }
    private static Dictionary<int, ExplorationVisitDisposition>
        BuildVisitDispositionMap(
            GameStateSnapshot state,
            ExplorationVisitQueueSnapshot queue)
    {
        var result =
            new Dictionary<int, ExplorationVisitDisposition>();

        if (!QueueMatchesSystem(queue, state))
        {
            return result;
        }

        if (queue.Active is { } active)
        {
            result[active.BodyId] =
                ExplorationVisitDisposition.Active;
        }

        foreach (ExplorationVisitBodyState item
                 in queue.Recommended)
        {
            result[item.BodyId] =
                ExplorationVisitDisposition.Recommended;
        }

        foreach (ExplorationVisitBodyState item
                 in queue.Deferred)
        {
            result[item.BodyId] =
                ExplorationVisitDisposition.Deferred;
        }

        foreach (ExplorationVisitBodyState item
                 in queue.Completed)
        {
            result[item.BodyId] =
                ExplorationVisitDisposition.Complete;
        }

        return result;
    }

    private static int VisitSortOrder(
        ExplorationVisitDisposition? disposition) =>
        disposition switch
        {
            ExplorationVisitDisposition.Active => 0,
            ExplorationVisitDisposition.Recommended => 1,
            ExplorationVisitDisposition.Deferred => 2,
            ExplorationVisitDisposition.Complete => 3,
            _ => 4
        };
    private static CatalogRow ToCatalogRow(
        ExplorationCatalogBody body,
        ExplorationVisitDisposition? disposition)
    {
        string progress = body.MappedThisVisit
            ? Loc.Get(
                body.EfficientlyMappedThisVisit
                    ? "Loc_DSS_EFFICIENT"
                    : "Loc_DSS_MAPPED")
            : body.MappedPreviously
                ? Loc.Get(
                    body.EfficientlyMappedPreviously
                        ? "Loc_HISTORY_DSS_EFFICIENT"
                        : "Loc_HISTORY_DSS_MAPPED")
                : body.ScannedThisVisit
                    ? Loc.Get("Loc_FSS_SCANNED")
                    : body.ScannedPreviously
                        ? Loc.Get("Loc_HISTORY_SCANNED")
                        : Loc.Get("Loc_COMMUNITY_DATA_ONLY");

        string[] discoveryBadges = BuildDiscoveryBadges(body);
        if (discoveryBadges.Length > 0)
        {
            progress += "  •  " + string.Join(" / ", discoveryBadges);
        }

        return new CatalogRow(
            body,
            body.Name,
            BuildVisitMarker(disposition),
            string.IsNullOrWhiteSpace(body.Subtype)
                ? body.Type
                : body.Subtype,
            BuildCompactHighlightText(body),
            BuildHighlightText(body),
            Loc.Format(
                "Loc_Distance_Ls_Value",
                body.DistanceFromArrivalLs),
            body.EstimatedMappingValue > 0
                ? Loc.Format(
                    "Loc_Credits_Short_Format",
                    body.EstimatedMappingValue)
                : Loc.Get("Loc_VALUE_UNKNOWN"),
            progress,
            disposition,
            BuildVisitStateLabel(disposition));
    }

    private static string BuildVisitMarker(
        ExplorationVisitDisposition? disposition) =>
        disposition switch
        {
            ExplorationVisitDisposition.Active => "●",
            ExplorationVisitDisposition.Recommended => "›",
            ExplorationVisitDisposition.Deferred => "↷",
            ExplorationVisitDisposition.Complete => "✓",
            _ => string.Empty
        };
    private static string BuildVisitStateLabel(
        ExplorationVisitDisposition? disposition) =>
        disposition switch
        {
            ExplorationVisitDisposition.Active =>
                Loc.Get("Loc_EXPLORATION_STATE_ACTIVE"),
            ExplorationVisitDisposition.Recommended =>
                Loc.Get("Loc_EXPLORATION_STATE_RECOMMENDED"),
            ExplorationVisitDisposition.Deferred =>
                Loc.Get("Loc_EXPLORATION_STATE_DEFERRED"),
            ExplorationVisitDisposition.Complete =>
                Loc.Get("Loc_EXPLORATION_STATE_COMPLETE"),
            _ => "—"
        };
    private static string BuildCompactHighlightText(
        ExplorationCatalogBody body)
    {
        var values = new List<string>();

        void Add(
            ExplorationBodyHighlights flag,
            string key)
        {
            if (body.Highlights.HasFlag(flag))
            {
                values.Add(Loc.Get(key));
            }
        }

        Add(ExplorationBodyHighlights.EarthLike, "Loc_EXPLORATION_INTEREST_ELW_SHORT");
        Add(ExplorationBodyHighlights.WaterWorld, "Loc_EXPLORATION_INTEREST_WW_SHORT");
        Add(ExplorationBodyHighlights.AmmoniaWorld, "Loc_EXPLORATION_INTEREST_AW_SHORT");
        Add(ExplorationBodyHighlights.Terraformable, "Loc_EXPLORATION_INTEREST_TERRAFORMABLE_SHORT");
        Add(ExplorationBodyHighlights.Biological, "Loc_EXPLORATION_INTEREST_BIO_SHORT");
        Add(ExplorationBodyHighlights.Valuable, "Loc_EXPLORATION_INTEREST_VALUE_SHORT");
        Add(ExplorationBodyHighlights.NeutronStar, "Loc_EXPLORATION_INTEREST_NEUTRON_SHORT");
        Add(ExplorationBodyHighlights.BlackHole, "Loc_EXPLORATION_INTEREST_BLACK_HOLE_SHORT");

        return values.Count == 0
            ? "—"
            : string.Join(
                Environment.NewLine,
                values.Take(3));
    }
    private static string BuildHighlightText(ExplorationCatalogBody body)
    {
        var values = new List<string>();
        void Add(ExplorationBodyHighlights flag, string key)
        {
            if (body.Highlights.HasFlag(flag)) values.Add(Loc.Get(key));
        }
        Add(ExplorationBodyHighlights.EarthLike, "Loc_Interest_EarthLike");
        Add(ExplorationBodyHighlights.WaterWorld, "Loc_Interest_WaterWorld");
        Add(ExplorationBodyHighlights.AmmoniaWorld, "Loc_Interest_AmmoniaWorld");
        Add(ExplorationBodyHighlights.Terraformable, "Loc_Interest_Terraformable");
        Add(ExplorationBodyHighlights.NeutronStar, "Loc_Interest_NeutronStar");
        Add(ExplorationBodyHighlights.BlackHole, "Loc_Interest_BlackHole");
        Add(ExplorationBodyHighlights.Biological, "Loc_BIOLOGICAL_SIGNALS_SHORT");
        Add(ExplorationBodyHighlights.Valuable, "Loc_HIGH_VALUE_SHORT");
        return values.Count == 0 ? Loc.Get("Loc_NO_SPECIAL_FEATURES") : string.Join(" · ", values);
    }

    private void CatalogFilterChanged(object sender, EventArgs e) => ApplyCatalogFilter();

    private void ExplorationBodiesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowSelectedBody(ExplorationBodiesGrid.SelectedItem as CatalogRow);

    private void ShowSelectedBody(CatalogRow? row)
    {
        if (row is null)
        {
            SelectedBodyNameText.Text =
                Loc.Get("Loc_Select_a_body");
            SelectedBodyReasonText.Text =
                string.Empty;
            SelectedBodyDetailsText.Text = string.Empty;
            SelectedBodyProgressText.Text = string.Empty;
            SelectedBodyPhysicalText.Text = string.Empty;
            SelectedBodyValueText.Text = string.Empty;
            SelectedBodyBiologyText.Text = string.Empty;
            SelectedBodyBiologyPanel.Visibility = Visibility.Collapsed;
            SelectedBodySourceText.Text = string.Empty;

            DssSelectedBodyText.Text =
                Loc.Get("Loc_Select_a_body");
            DssMappingResultText.Text =
                string.Empty;

            CopySelectedBodyButton.IsEnabled = false;
            BookmarkSelectedBodyButton.IsEnabled = false;

            DeferSelectedBodyButton.Visibility =
                Visibility.Collapsed;
            ResumeSelectedBodyButton.Visibility =
                Visibility.Collapsed;

            return;
        }

        ExplorationCatalogBody body = row.Body;

        SelectedBodyNameText.Text = body.Name;
        DssSelectedBodyText.Text = body.Name;
        SelectedBodyReasonText.Text = row.HighlightsTooltip;

        ExplorationVisitBodyState? visit =
            FindVisitBodyState(body.BodyId);

        var detailParts = new List<string>();

        string visitDetails =
            BuildSelectedBodyVisitDetails(
                visit);

        string discoveryDetails =
            BuildDiscoveryStatusDetails(body);

        if (!string.IsNullOrWhiteSpace(visitDetails))
        {
            detailParts.Add(visitDetails);
        }

        if (!string.IsNullOrWhiteSpace(discoveryDetails))
        {
            detailParts.Add(discoveryDetails);
        }

        SelectedBodyProgressText.Text = string.Join(
            Environment.NewLine,
            new[] { visitDetails, discoveryDetails }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        SelectedBodyPhysicalText.Text = string.Join(
            Environment.NewLine,
            Loc.Format("Loc_BODY_TYPE_DETAIL", row.Type),
            Loc.Format("Loc_BODY_DISTANCE_DETAIL", body.DistanceFromArrivalLs),
            Loc.Format("Loc_BODY_GRAVITY_DETAIL", body.GravityG),
            Loc.Format("Loc_BODY_TEMPERATURE_DETAIL", body.SurfaceTemperatureKelvin),
            Loc.Format("Loc_BODY_PRESSURE_DETAIL", body.SurfacePressureAtmospheres),
            Loc.Format("Loc_BODY_ATMOSPHERE_DETAIL", EmptyAsUnknown(body.Atmosphere)),
            Loc.Format("Loc_BODY_VOLCANISM_DETAIL", EmptyAsUnknown(body.Volcanism)));
        SelectedBodyValueText.Text = string.Join(
            Environment.NewLine,
            Loc.Format("Loc_BODY_SCAN_VALUE_DETAIL", body.EstimatedScanValue),
            Loc.Format("Loc_BODY_MAPPING_VALUE_DETAIL", body.EstimatedMappingValue));
        SelectedBodySourceText.Text =
            Loc.Format("Loc_BODY_SOURCE_DETAIL", LocalizeCatalogSource(body.Source));


        detailParts.AddRange(
        [
            Loc.Format(
                "Loc_BODY_TYPE_DETAIL",
                row.Type),
            Loc.Format(
                "Loc_BODY_DISTANCE_DETAIL",
                body.DistanceFromArrivalLs),
            Loc.Format(
                "Loc_BODY_SCAN_VALUE_DETAIL",
                body.EstimatedScanValue),
            Loc.Format(
                "Loc_BODY_MAPPING_VALUE_DETAIL",
                body.EstimatedMappingValue),
            Loc.Format(
                "Loc_BODY_GRAVITY_DETAIL",
                body.GravityG),
            Loc.Format(
                "Loc_BODY_TEMPERATURE_DETAIL",
                body.SurfaceTemperatureKelvin),
            Loc.Format(
                "Loc_BODY_PRESSURE_DETAIL",
                body.SurfacePressureAtmospheres),
            Loc.Format(
                "Loc_BODY_ATMOSPHERE_DETAIL",
                EmptyAsUnknown(body.Atmosphere)),
            Loc.Format(
                "Loc_BODY_VOLCANISM_DETAIL",
                EmptyAsUnknown(body.Volcanism)),
            Loc.Format(
                "Loc_BODY_BIOLOGY_DETAIL",
                body.BiologicalSignals,
                body.Genuses.Count == 0
                    ? Loc.Get("Loc_VALUE_UNKNOWN")
                    : string.Join(", ", body.Genuses)),
            Loc.Format(
                "Loc_BODY_ORGANICS_HISTORY_DETAIL",
                body.CompletedOrganics),
            Loc.Format(
                "Loc_BODY_SOURCE_DETAIL",
                LocalizeCatalogSource(body.Source))
        ]);

        string bioGuidance =
            BuildSelectedBodyBioGuidance(
                body,
                visit,
                JournalMonitorService.Instance.Current);

        if (!string.IsNullOrWhiteSpace(bioGuidance))
        {
            detailParts.Add(bioGuidance);
        }
        else
        {
            detailParts.Add(
                BuildPredictionDetails(body));
        }

        string biologyPresentation = !string.IsNullOrWhiteSpace(bioGuidance)
            ? bioGuidance
            : BuildPredictionDetails(body);
        SelectedBodyBiologyText.Text = biologyPresentation;
        SelectedBodyBiologyPanel.Visibility =
            body.IsBiological && !string.IsNullOrWhiteSpace(biologyPresentation)
                ? Visibility.Visible
                : Visibility.Collapsed;

        SelectedBodyDetailsText.Text =
            string.Join(
                Environment.NewLine,
                detailParts.Where(
                    value =>
                        !string.IsNullOrWhiteSpace(value)));

        SetDssTarget(
            body.EfficiencyTarget > 0
                ? body.EfficiencyTarget
                : SettingsService.Instance.Settings
                    .DssEfficiencyTarget);

        DssMappingResultText.Text =
            body.LastProbesUsed > 0
            && body.EfficiencyTarget > 0
                ? Loc.Format(
                    body.LastProbesUsed
                        <= body.EfficiencyTarget
                            ? "Loc_DSS_RESULT_EFFICIENT"
                            : "Loc_DSS_RESULT_OVER_TARGET",
                    body.LastProbesUsed,
                    body.EfficiencyTarget)
                : Loc.Get(
                    "Loc_DSS_NO_RESULT_YET");

        CopySelectedBodyButton.IsEnabled =
            !string.IsNullOrWhiteSpace(body.Name);

        BookmarkSelectedBodyButton.IsEnabled =
            !string.IsNullOrWhiteSpace(body.Name);

        DeferSelectedBodyButton.Visibility =
            visit is not null
            && !visit.IsComplete
            && visit.Disposition
                is ExplorationVisitDisposition.Active
                or ExplorationVisitDisposition.Recommended
                ? Visibility.Visible
                : Visibility.Collapsed;

        ResumeSelectedBodyButton.Visibility =
            visit?.Disposition
                == ExplorationVisitDisposition.Deferred
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private static ExplorationVisitBodyState? FindVisitBodyState(
        int bodyId)
    {
        ExplorationVisitQueueSnapshot queue =
            ExplorationVisitStateService.Instance.Current;

        if (queue.Active?.BodyId == bodyId)
        {
            return queue.Active;
        }

        return queue.Recommended
            .Concat(queue.Deferred)
            .Concat(queue.Completed)
            .FirstOrDefault(
                item => item.BodyId == bodyId);
    }

    private static string BuildSelectedBodyVisitDetails(
        ExplorationVisitBodyState? visit)
    {
        if (visit is null)
        {
            return string.Empty;
        }

        string fss = visit.Progress.FssScanned
            ? "FSS ✓"
            : "FSS ○";

        string dss = !visit.DssRequired
            ? "DSS —"
            : visit.Progress.DssMapped
                ? visit.Progress.DssEfficient
                    ? "DSS ◎"
                    : "DSS ✓"
                : "DSS ○";

        string bio = !visit.BiologyRequired
            ? "BIO —"
            : $"BIO {visit.Progress.CompletedBiologicalSignals}/{visit.Progress.BiologicalSignals}";

        return Loc.Format(
            "Loc_EXPLORATION_SELECTED_PROGRESS_FORMAT",
            BuildVisitStateLabel(visit.Disposition),
            string.Join(
                "  •  ",
                fss,
                dss,
                bio));
    }

    private static string BuildDiscoveryStatusDetails(
        ExplorationCatalogBody body)
    {
        var parts = new List<string>();

        if (body.DiscoveryStatusKnown)
        {
            parts.Add(Loc.Get(
                body.WasDiscovered
                    ? "Loc_EXPLORATION_DISCOVERY_CONFIRMED"
                    : "Loc_EXPLORATION_FIRST_DISCOVERY_CANDIDATE"));
        }

        if (body.MappingStatusKnown)
        {
            parts.Add(Loc.Get(
                body.WasMapped
                    ? "Loc_EXPLORATION_MAPPING_CONFIRMED"
                    : "Loc_EXPLORATION_FIRST_MAPPING_CANDIDATE"));
        }

        return string.Join("  •  ", parts);
    }

    private static string BuildSelectedBodyBioGuidance(
        ExplorationCatalogBody body,
        ExplorationVisitBodyState? visit,
        GameStateSnapshot state)
    {
        if (!body.IsBiological
            || body.BiologicalSignals <= 0)
        {
            return string.Empty;
        }

        var lines = new List<string>
        {
            Loc.Get("Loc_EXPLORATION_BIO_GUIDANCE_HEADER")
        };

        BodyExplorationProgress? progress =
            visit?.Progress;

        if (progress is not null)
        {
            lines.Add(
                Loc.Format(
                    "Loc_EXPLORATION_BIO_BODY_PROGRESS_FORMAT",
                    progress.CompletedBiologicalSignals,
                    progress.BiologicalSignals));

            if (progress.BiologyComplete)
            {
                lines.Add(
                    Loc.Get(
                        "Loc_EXPLORATION_BIO_COMPLETE_GUIDANCE"));

                return string.Join(
                    Environment.NewLine,
                    lines);
            }

            if (progress.MissingGenuses.Count > 0)
            {
                lines.Add(
                    Loc.Format(
                        "Loc_EXPLORATION_MISSING_GENUSES_FORMAT",
                        string.Join(
                            " · ",
                            progress.MissingGenuses)));
            }

            int unnamedRemaining = Math.Max(
                0,
                progress.RemainingBiologicalSignals
                    - progress.MissingGenuses.Count);

            if (unnamedRemaining > 0)
            {
                lines.Add(
                    Loc.Format(
                        "Loc_EXPLORATION_UNKNOWN_GENUSES_FORMAT",
                        unnamedRemaining));
            }
        }

        OrganicScanProgressSnapshot? activeOrganic =
            state.GetActiveOrganicForBody(body.BodyId);

        if (activeOrganic is not null)
        {
            string organism =
                !string.IsNullOrWhiteSpace(
                    activeOrganic.Variant)
                    ? activeOrganic.Variant
                    : activeOrganic.Species;

            lines.Add(
                Loc.Format(
                    "Loc_EXPLORATION_ACTIVE_SAMPLE_FORMAT",
                    organism,
                    activeOrganic.Stage,
                    activeOrganic.ColonyRangeMeters));

            SurfaceNavigationResult? navigation =
                SurfaceNavigationCalculator.Calculate(
                    state.Latitude,
                    state.Longitude,
                    state.HeadingDegrees,
                    state.PlanetRadiusMeters,
                    activeOrganic.LastSampleLatitude,
                    activeOrganic.LastSampleLongitude);

            if (navigation is not null
                && activeOrganic.ColonyRangeMeters > 0)
            {
                double remaining = Math.Max(
                    0,
                    activeOrganic.ColonyRangeMeters
                        - navigation.DistanceMeters);

                lines.Add(
                    navigation.IsFarEnough(
                        activeOrganic.ColonyRangeMeters)
                        ? Loc.Format(
                            "Loc_EXPLORATION_SAMPLE_RANGE_READY_FORMAT",
                            navigation.DistanceMeters)
                        : Loc.Format(
                            "Loc_EXPLORATION_SAMPLE_RANGE_REMAINING_FORMAT",
                            navigation.DistanceMeters,
                            remaining,
                            activeOrganic.ColonyRangeMeters,
                            navigation.EscapeBearingDegrees));
            }
        }

        IReadOnlyList<ExobiologyPrediction> predictions =
            ExobiologyPredictionService.Instance.Predict(
                body,
                12);

        if (progress is not null
            && (progress.MissingGenusKeys.Count > 0
                || progress.MissingGenuses.Count > 0))
        {
            IReadOnlyList<string> missingIdentity =
                progress.MissingGenusKeys.Count > 0
                    ? progress.MissingGenusKeys
                    : progress.MissingGenuses;

            predictions = predictions
                .Where(prediction =>
                    missingIdentity.Any(
                        genus =>
                            GenusMatches(
                                genus,
                                prediction.Genus)))
                .ToArray();
        }

        ExobiologyPrediction[] likely =
            predictions
                .GroupBy(
                    prediction => prediction.Genus,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(
                            item =>
                                item.RelativeProbability)
                        .ThenByDescending(
                            item =>
                                item.ObservationCount)
                        .First())
                .OrderByDescending(
                    item => item.RelativeProbability)
                .Take(4)
                .ToArray();

        if (likely.Length > 0)
        {
            lines.Add(
                Loc.Get(
                    "Loc_EXPLORATION_LIKELY_SPECIES_HEADER"));

            foreach (ExobiologyPrediction prediction
                     in likely)
            {
                lines.Add(
                    Loc.Format(
                        "Loc_EXPLORATION_LIKELY_SPECIES_LINE_FORMAT",
                        prediction.Genus,
                        prediction.Species,
                        prediction.RelativeProbability * 100,
                        prediction.ColonyRangeMeters,
                        prediction.BaseValue));
            }
        }

        lines.Add(
            Loc.Get(
                "Loc_EXPLORATION_BIO_LOCATION_LIMITATION"));

        return string.Join(
            Environment.NewLine,
            lines);
    }

    private static bool GenusMatches(
        string expected,
        string actual) =>
        string.Equals(
            ExobiologyPredictionService.NormalizeGenusIdentity(expected),
            ExobiologyPredictionService.NormalizeGenusIdentity(actual),
            StringComparison.OrdinalIgnoreCase);
    private void DeferSelectedBodyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ExplorationBodiesGrid.SelectedItem
            is not CatalogRow row)
        {
            return;
        }

        if (ExplorationVisitStateService.Instance
            .DeferBody(row.Body.BodyId))
        {
            UpdateJournalState(
                JournalMonitorService.Instance.Current);
        }
    }

    private void ResumeSelectedBodyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ExplorationBodiesGrid.SelectedItem
            is not CatalogRow row)
        {
            return;
        }

        if (ExplorationVisitStateService.Instance
            .ResumeBody(row.Body.BodyId))
        {
            UpdateJournalState(
                JournalMonitorService.Instance.Current);
        }
    }

    private void SetDssTarget(int target)
    {
        target = Math.Clamp(target, DssProbePatternCatalog.MinimumTarget, DssProbePatternCatalog.MaximumTarget);
        updatingDssTarget = true;
        DssTargetComboBox.SelectedItem = target;
        updatingDssTarget = false;
        DrawDssPattern(target);
    }

    private void DssTargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DssTargetComboBox.SelectedItem is not int target) return;
        DrawDssPattern(target);
        if (!updatingDssTarget)
        {
            SettingsService.Instance.SetDssGuidanceSettings(target);
        }
    }

    private void DrawDssPattern(int target)
    {
        if (DssPatternCanvas is null) return;
        while (DssPatternCanvas.Children.Count > 2)
        {
            DssPatternCanvas.Children.RemoveAt(DssPatternCanvas.Children.Count - 1);
        }

        DssProbePattern pattern = DssProbePatternCatalog.Get(target);
        const double center = 230;
        const double discRadius = 180;
        foreach (DssAimPoint point in pattern.Points.OrderBy(point => point.Sequence))
        {
            double x = center + point.X * discRadius;
            double y = center + point.Y * discRadius;
            Brush fill = (Brush)FindResource(point.Zone switch
            {
                DssAimZone.FarSide => "FailureColorBrush",
                DssAimZone.Limb => "AccentColorBrush",
                _ => "PrimaryTextColorBrush"
            });
            var marker = new Ellipse
            {
                Width = 38,
                Height = 38,
                Fill = fill,
                Stroke = (Brush)FindResource("PrimaryBackgroundColorBrush"),
                StrokeThickness = 3,
                ToolTip = Loc.Get(point.Zone switch
                {
                    DssAimZone.FarSide => "Loc_DSS_ZONE_FAR_SIDE",
                    DssAimZone.Limb => "Loc_DSS_ZONE_LIMB",
                    _ => "Loc_DSS_ZONE_DISC"
                })
            };
            Canvas.SetLeft(marker, x - 19);
            Canvas.SetTop(marker, y - 19);
            DssPatternCanvas.Children.Add(marker);

            var number = new TextBlock
            {
                Text = point.Sequence.ToString(),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("PrimaryBackgroundColorBrush"),
                IsHitTestVisible = false
            };
            number.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(number, x - number.DesiredSize.Width / 2);
            Canvas.SetTop(number, y - number.DesiredSize.Height / 2);
            DssPatternCanvas.Children.Add(number);
        }

        DssPatternText.Text = Loc.Get(pattern.StrategyKey) + Environment.NewLine
                              + Loc.Get(pattern.AdjustmentKey);
    }

    private static string EmptyAsUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) ? Loc.Get("Loc_VALUE_UNKNOWN") : value;

    private static string LocalizeCatalogSource(string source)
    {
        const string journal = "Journal";
        if (source.Equals(journal, StringComparison.OrdinalIgnoreCase))
        {
            return Loc.Get("Loc_JOURNAL_DATA_SOURCE");
        }
        if (source.StartsWith(journal + " + ", StringComparison.OrdinalIgnoreCase))
        {
            return Loc.Get("Loc_JOURNAL_DATA_SOURCE") + source[journal.Length..];
        }
        return source;
    }

    private void OpenExplorationAssistantButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (fullExplorationVisible)
        {
            return;
        }

        externalOverlayActive = false;
        fullExplorationVisible = true;
        ApplySurfaceVisibility();
        RefreshFullWorkspace(currentJournal);
        ViewModeChanged?.Invoke(true);
    }

    private void CloseExplorationAssistantButton_Click(
        object sender,
        RoutedEventArgs e) =>
        CloseFullView();

    public void CloseFullView()
    {
        if (!fullExplorationVisible)
        {
            return;
        }

        fullExplorationVisible = false;
        ApplySurfaceVisibility();
        RefreshPresentation();
        ViewModeChanged?.Invoke(false);
    }

    private void CloseFullExplorationView() =>
        CloseFullView();

    private void CopySelectedBodyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExplorationBodiesGrid.SelectedItem is CatalogRow row && !string.IsNullOrWhiteSpace(row.Body.Name))
        {
            Clipboard.SetText(row.Body.Name);
        }
    }

    private void BookmarkSelectedBodyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExplorationBodiesGrid.SelectedItem is not CatalogRow row) return;
        ExplorationLogService.Instance.AddManualFinding(
            catalog.SystemName, row.Body.Name, row.Highlights);
    }

    private void CopyCurrentSystemText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(catalog.SystemName)) Clipboard.SetText(catalog.SystemName);
    }

    private void RefreshExplorationCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        ExplorationDataService.Instance.Refresh();
        ExplorationPoiService.Instance.Refresh();
    }

    private void ImportExplorationRouteButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.Get("Loc_IMPORT_ROUTE"),
            Filter = Loc.Get("Loc_SPANSH_ROUTE_FILE_FILTER"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            ExplorationRouteService.Instance.Import(dialog.FileName);
            ShowImportedRoute();
            UpdateJournalState(JournalMonitorService.Instance.Current);
        }
        catch (Exception ex)
        {
            CatalogSourceText.Text = Loc.Format("Loc_ROUTE_IMPORT_FAILED_FORMAT", ex.Message);
        }
    }

    private void OpenSpanshButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://spansh.co.uk/riches") { UseShellExecute = true });
    }

    private void CopySystemText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string system } && !string.IsNullOrWhiteSpace(system))
            Clipboard.SetText(system);
    }

    private sealed record RouteStopRow(
        string Position,
        string State,
        string System,
        string Targets,
        string EstimatedValue,
        bool IsCurrent,
        bool IsNext);

    private void RefreshRouteStops(ExplorationRoutePlan route)
    {
        if (RouteStopsItemsControl is null || EmptyRouteStopsText is null) return;
        RouteStopRow[] rows = route.Stops.Select((stop, index) =>
        {
            string state = Loc.Get(index < route.CurrentIndex
                ? "Loc_ROUTE_STOP_COMPLETED"
                : index == route.CurrentIndex
                    ? "Loc_ROUTE_STOP_CURRENT"
                    : index == route.CurrentIndex + 1
                        ? "Loc_ROUTE_STOP_NEXT"
                        : "Loc_ROUTE_STOP_PLANNED");
            string targets = stop.Bodies.Count == 0
                ? Loc.Get("Loc_ROUTE_NO_TARGETS")
                : string.Join(Environment.NewLine, stop.Bodies.Select(body => Loc.Format(
                    "Loc_ROUTE_BODY_TARGET_FORMAT", body.Name, body.ScanValue, body.MappingValue)));
            string value = stop.EstimatedValue > 0
                ? Loc.Format("Loc_ROUTE_VALUE_FORMAT", stop.EstimatedValue)
                : Loc.Get("Loc_VALUE_UNKNOWN");
            return new RouteStopRow(
                $"{index + 1}/{route.Stops.Count}", state, stop.System, targets, value,
                index == route.CurrentIndex, index == route.CurrentIndex + 1);
        }).ToArray();
        RouteStopsItemsControl.ItemsSource = rows;
        EmptyRouteStopsText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        string nextSystem = route.NextStop?.System ?? string.Empty;
        RouteNavigationPanel.Visibility = string.IsNullOrWhiteSpace(nextSystem) ? Visibility.Collapsed : Visibility.Visible;
        NavigationTargetSystemLink.Text = nextSystem;
        NavigationTargetSystemLink.Tag = nextSystem;
        bool automaticEnabled = SettingsService.Instance.Settings.EnableExperimentalRouteAutomation;
        AutomaticRouteNavigationButton.IsEnabled = automaticEnabled;
        AutomaticRouteNavigationButton.ToolTip = automaticEnabled ? null : Loc.Get("Loc_NAVIGATION_AUTO_DISABLED");
    }

    private async void PrepareRouteNavigationButton_Click(object sender, RoutedEventArgs e) =>
        await NavigateToNextRouteSystemAsync(confirmAutomatically: false);

    private async void AutomaticRouteNavigationButton_Click(object sender, RoutedEventArgs e) =>
        await NavigateToNextRouteSystemAsync(confirmAutomatically: true);

    private async Task NavigateToNextRouteSystemAsync(
        bool confirmAutomatically)
    {
        string target =
            ExplorationRouteService.Instance.Current.NextStop?.System
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(target))
        {
            RouteNavigationStatusText.Text =
                Loc.Get("Loc_NAVIGATION_NO_TARGET");
            return;
        }

        routeNavigationCancellation?.Cancel();
        routeNavigationCancellation?.Dispose();
        routeNavigationCancellation =
            new CancellationTokenSource();

        Clipboard.SetText(target);
        RouteNavigationStatusText.Text =
            Loc.Format(
                "Loc_NAVIGATION_PREPARING",
                target);

        if (fullExplorationVisible)
        {
            CloseFullExplorationView();
        }

        if (NavigateAsync is null)
        {
            RouteNavigationStatusText.Text =
                Loc.Get("Loc_NAVIGATION_GAME_NOT_FOUND");
            return;
        }

        await Dispatcher.Yield(
            DispatcherPriority.Background);

        EliteNavigationResult result =
            await NavigateAsync(
                target,
                confirmAutomatically,
                routeNavigationCancellation.Token);

        RouteNavigationStatusText.Text =
            string.IsNullOrWhiteSpace(result.Detail)
                ? Loc.Format(
                    result.MessageKey,
                    result.TargetSystem)
                : Loc.Format(
                    result.MessageKey,
                    result.TargetSystem,
                    result.Detail);
    }

    private void ToggleRouteFormButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings settings = SettingsService.Instance.Settings;
        SettingsService.Instance.SetExplorationRoutePanelState(
            !settings.ExplorationRouteFormCollapsed, settings.ExplorationRouteListCollapsed);
        ApplyRoutePanelState();
    }

    private void ToggleRouteListButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings settings = SettingsService.Instance.Settings;
        SettingsService.Instance.SetExplorationRoutePanelState(
            settings.ExplorationRouteFormCollapsed, !settings.ExplorationRouteListCollapsed);
        ApplyRoutePanelState();
    }

    private void ApplyRoutePanelState()
    {
        if (RouteFormPanel is null || RouteStopsPanel is null) return;
        AppSettings settings = SettingsService.Instance.Settings;
        RouteFormPanel.Visibility = settings.ExplorationRouteFormCollapsed ? Visibility.Collapsed : Visibility.Visible;
        RouteStopsPanel.Visibility = settings.ExplorationRouteListCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ToggleRouteFormButton.Content = Loc.Get(settings.ExplorationRouteFormCollapsed ? "Loc_EXPAND" : "Loc_COLLAPSE");
        ToggleRouteListButton.Content = Loc.Get(settings.ExplorationRouteListCollapsed ? "Loc_EXPAND" : "Loc_COLLAPSE");
    }

    private void ShowImportedRoute()
    {
        SettingsService.Instance.SetExplorationRoutePanelState(formCollapsed: true, routeCollapsed: false);
        ApplyRoutePanelState();
        Dispatcher.BeginInvoke(new Action(() => RouteTabScrollViewer.ScrollToTop()), DispatcherPriority.Loaded);
    }

    private sealed record LogRow(
        ExplorationLogEntry Entry,
        string Time,
        string Kind,
        string System,
        string Body,
        string Detail,
        string Bookmark);

    private void RefreshExplorationLog()
    {
        if (ExplorationLogGrid is null) return;
        IEnumerable<ExplorationLogEntry> source = ExplorationLogService.Instance.Entries;
        if (BookmarkedOnlyCheckBox?.IsChecked == true) source = source.Where(item => item.Bookmarked);
        ExplorationLogGrid.ItemsSource = source.Select(ToLogRow).ToArray();
    }

    private static LogRow ToLogRow(ExplorationLogEntry entry)
    {
        string kind = Loc.Get(entry.Kind switch
        {
            ExplorationLogKind.Visit => "Loc_LOG_VISIT",
            ExplorationLogKind.NotableBody => "Loc_LOG_NOTABLE_BODY",
            ExplorationLogKind.Mapping => "Loc_LOG_MAPPING",
            ExplorationLogKind.Biology => "Loc_LOG_BIOLOGY",
            ExplorationLogKind.Codex => "Loc_LOG_CODEX",
            _ => "Loc_LOG_MANUAL"
        });
        string detail = entry.Kind switch
        {
            ExplorationLogKind.Mapping when entry.Subject == "efficient" => Loc.Format("Loc_LOG_DSS_EFFICIENT_FORMAT", entry.Detail),
            ExplorationLogKind.Mapping => Loc.Format("Loc_LOG_DSS_FORMAT", entry.Detail),
            ExplorationLogKind.Biology when entry.Subject == "signals" => Loc.Format("Loc_LOG_BIO_SIGNALS_FORMAT", entry.Detail),
            ExplorationLogKind.Biology => Loc.Format("Loc_LOG_BIO_COMPLETE_FORMAT", entry.Subject),
            ExplorationLogKind.NotableBody when entry.Detail == "terraformable" => Loc.Format("Loc_LOG_TERRAFORMABLE_FORMAT", entry.Subject),
            ExplorationLogKind.NotableBody => entry.Subject,
            ExplorationLogKind.Codex => Loc.Format("Loc_LOG_CODEX_FORMAT", entry.Subject, entry.Detail),
            ExplorationLogKind.Manual => entry.Detail,
            _ => entry.Detail
        };
        return new LogRow(entry, entry.TimestampUtc.ToLocalTime().ToString("g"), kind,
            entry.System, entry.Body, detail, entry.Bookmarked ? "★" : string.Empty);
    }

    private void ExplorationLogFilterChanged(object sender, RoutedEventArgs e) => RefreshExplorationLog();

    private void ToggleLogBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExplorationLogGrid.SelectedItem is LogRow row) ExplorationLogService.Instance.ToggleBookmark(row.Entry.Id);
    }

    private async void CalculateSpanshRouteButton_Click(object sender, RoutedEventArgs e)
    {
        CalculateSpanshRouteButton.IsEnabled = false;
        SpanshCalculationStatusText.Text = Loc.Get("Loc_SPANSH_VALIDATING");
        try
        {
            var request = new SpanshRoadToRichesRequest(
                SpanshSourceTextBox.Text.Trim(), SpanshDestinationTextBox.Text.Trim(),
                ParseDouble(SpanshJumpRangeTextBox.Text, 50), ParseInt(SpanshRadiusTextBox.Text, 25),
                ParseInt(SpanshMaxSystemsTextBox.Text, 25), ParseInt(SpanshMaxDistanceTextBox.Text, 1000),
                ParseLong(SpanshMinValueTextBox.Text, 500_000), SpanshMappingValueCheckBox.IsChecked == true,
                SpanshLoopCheckBox.IsChecked == true, SpanshAvoidThargoidsCheckBox.IsChecked == true);
            SpanshCalculationStatusText.Text = Loc.Get("Loc_SPANSH_CALCULATING");
            ExplorationRoutePlan plan = await spanshRouteClient.CalculateRoadToRichesAsync(request);
            ExplorationRouteService.Instance.SetPlan(plan);
            ShowImportedRoute();
            SpanshCalculationStatusText.Text = Loc.Format("Loc_SPANSH_IMPORTED_FORMAT", plan.Stops.Count);
            UpdateJournalState(JournalMonitorService.Instance.Current);
        }
        catch (Exception ex)
        {
            SpanshCalculationStatusText.Text = Loc.Format("Loc_SPANSH_ROUTE_FAILED_FORMAT", ex.Message);
        }
        finally
        {
            CalculateSpanshRouteButton.IsEnabled = true;
        }
    }

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value.Trim(), out int result) ? result : fallback;
    private static long ParseLong(string value, long fallback) =>
        long.TryParse(value.Trim(), out long result) ? result : fallback;
    private static double ParseDouble(string value, double fallback) =>
        double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.CurrentCulture, out double result)
            || double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result) ? result : fallback;

    private void CopyPoiSystemButton_Click(object sender, RoutedEventArgs e)
    {
        string? system = ExplorationPoiService.Instance.Current.Closest?.System;
        if (!string.IsNullOrWhiteSpace(system)) Clipboard.SetText(system);
    }

    private void OpenPoiDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        string? url = ExplorationPoiService.Instance.Current.Closest?.Url;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? target))
            Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true });
    }

    private async void PlotPoiRouteButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string target =
            ExplorationPoiService.Instance.Current.Closest?.System
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(target)
            || NavigateAsync is null)
        {
            return;
        }

        routeNavigationCancellation?.Cancel();
        routeNavigationCancellation?.Dispose();
        routeNavigationCancellation =
            new CancellationTokenSource();

        Clipboard.SetText(target);

        if (fullExplorationVisible)
        {
            CloseFullExplorationView();
        }

        await Dispatcher.Yield(
            DispatcherPriority.Background);

        EliteNavigationResult result =
            await NavigateAsync(
                target,
                false,
                routeNavigationCancellation.Token);

        if (result.Status == EliteNavigationStatus.Failed)
        {
            Logger.Logger.Warning(
                $"Exploration POI navigation failed for {target}: "
                + $"{result.MessageKey} {result.Detail}");
        }
    }

    private sealed record CatalogRow(
        ExplorationCatalogBody Body,
        string Name,
        string RowMarker,
        string Type,
        string Highlights,
        string HighlightsTooltip,
        string Distance,
        string MappingValue,
        string Progress,
        ExplorationVisitDisposition? Disposition,
        string VisitState);


    private static string BuildEarningsSummary(ExplorationEarningsState earnings)
    {
        string rebuilding = earnings.IsRebuilding ? Loc.Get("Loc_ESTIMATE_REBUILDING_SUFFIX") : string.Empty;
        return Loc.Format("Loc_UNSOLD_EXPLORATION_ESTIMATE_FORMAT",
            earnings.UniversalCartographicsEstimate,
            earnings.ExobiologyMinimumEstimate,
            earnings.ExobiologyMaximumEstimate,
            rebuilding);
    }

    private static string BuildFullOverview(GameStateSnapshot state, ExplorationDataState externalData)
    {
        ExplorationVisitQueueSnapshot queue = ExplorationVisitStateService.Instance.Current;
        var parts = new List<string>
        {
            BuildAdaptiveExplorationHeader(state, externalData, queue),
            BuildEarningsSummary(ExplorationEarningsService.Instance.Current)
        };

        if (QueueMatchesSystem(queue, state))
        {
            parts.Add(Loc.Format(
                "Loc_EXPLORATION_QUEUE_FULL_FORMAT",
                queue.RemainingCount,
                queue.DeferredCount,
                queue.CompletedCount));
        }

        string destinationStatus = BuildDestinationVisitStatus(state);
        if (!string.IsNullOrWhiteSpace(destinationStatus))
        {
            parts.Add(destinationStatus);
        }

        string alert = BuildCompactRouteOrAlert(
            state,
            QueueMatchesSystem(queue, state) ? queue : null);
        if (!string.IsNullOrWhiteSpace(alert)) parts.Add(alert);

        return string.Join(Environment.NewLine, parts.Where(value => !string.IsNullOrWhiteSpace(value)));
    }



    private static string BuildFullRouteSummary(ExplorationRoutePlan route)
    {
        if (route.Stops.Count == 0) return Loc.Get("Loc_NO_EXPLORATION_ROUTE");
        return route.NextStop is { } next
            ? Loc.Format("Loc_ROUTE_FULL_STATUS_FORMAT", route.SourceFile, route.CurrentIndex + 1,
                route.Stops.Count, next.System)
            : Loc.Format("Loc_ROUTE_FULL_COMPLETE_FORMAT", route.SourceFile, route.Stops.Count);
    }



    private static string BuildPredictionDetails(ExplorationCatalogBody body)
    {
        IReadOnlyList<ExobiologyPrediction> predictions = ExobiologyPredictionService.Instance.Predict(body);
        if (predictions.Count == 0) return Loc.Get("Loc_BIO_PREDICTION_UNAVAILABLE");
        string lines = string.Join(Environment.NewLine, predictions.Select(item => Loc.Format(
            "Loc_BIO_PREDICTION_LINE_FORMAT", item.Species, item.Variant,
            item.RelativeProbability * 100, item.BaseValue, item.ColonyRangeMeters)));
        return Loc.Get("Loc_BIO_PREDICTION_HEADER") + Environment.NewLine + lines
               + Environment.NewLine + Loc.Get("Loc_BIO_PREDICTION_DISCLAIMER");
    }


}