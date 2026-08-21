using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Shapes;
using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services;
using ED_Inara_Overlay.Services.Journal;
using ED_Inara_Overlay.Services.Exploration;
using ED_Inara_Overlay.Services.Navigation;
using ED_Inara_Overlay.Utils;

namespace ED_Inara_Overlay.Windows;

public partial class ActivityWorkspaceOverlayWindow : Window
{
    private readonly MainWindow? parentWindow;
    private readonly DispatcherTimer updateTimer;
    private readonly SpanshRouteClient spanshRouteClient = new();
    private IntPtr targetWindow;
    private ActivityType activity;
    private bool interactive;
    private bool showCursorWhenInteractive;
    private string placement = "MiddleRight";
    private string chromeStyle = OverlayChromeStyles.Compact;
    private bool hasManualPosition;
    private double manualXRatio;
    private double manualYRatio;
    private bool disposed;
    private bool fullExplorationVisible;
    private bool updatingDssTarget;
    private ExplorationSystemCatalog catalog = ExplorationSystemCatalog.Empty;
    private CancellationTokenSource? routeNavigationCancellation;

    private sealed record CatalogFilterOption(string Value, string LabelKey)
    {
        public string Label => Loc.Get(LabelKey);
    }

    private static readonly CatalogFilterOption[] CatalogFilters =
    [
        new("All", "Loc_FILTER_ALL_BODIES"),
        new("Notable", "Loc_FILTER_NOTABLE"),
        new("Valuable", "Loc_FILTER_VALUABLE"),
        new("Biological", "Loc_FILTER_BIOLOGICAL"),
        new("Unmapped", "Loc_FILTER_UNMAPPED"),
        new("Landable", "Loc_FILTER_LANDABLE")
    ];

    public ActivityWorkspaceOverlayWindow(ActivityType initialActivity) : this(null, initialActivity)
    {
    }

    public ActivityWorkspaceOverlayWindow(MainWindow? parentWindow, ActivityType initialActivity)
    {
        this.parentWindow = parentWindow;
        activity = initialActivity;
        InitializeComponent();
        DssTargetComboBox.ItemsSource = Enumerable.Range(
            DssProbePatternCatalog.MinimumTarget,
            DssProbePatternCatalog.MaximumTarget - DssProbePatternCatalog.MinimumTarget + 1);
        SetDssTarget(SettingsService.Instance.Settings.DssEfficiencyTarget);
        CatalogFilterComboBox.ItemsSource = CatalogFilters;
        CatalogFilterComboBox.SelectedIndex = 0;
        ApplyRoutePanelState();
        SetChromeStyle(Services.SettingsService.Instance.Settings.OverlayChromeStyle);
        Loaded += OnLoaded;
        Closed += OnClosed;
        JournalMonitorService.Instance.StateChanged += OnJournalStateChanged;
        ExplorationDataService.Instance.DataChanged += OnExplorationDataChanged;
        ExplorationHistoryService.Instance.HistoryChanged += OnExplorationHistoryChanged;
        ExplorationRouteService.Instance.RouteChanged += OnExplorationRouteChanged;
        ExplorationPoiService.Instance.PoiChanged += OnExplorationPoiChanged;
        ExplorationEarningsService.Instance.Changed += OnExplorationEarningsChanged;
        ExplorationLogService.Instance.Changed += OnExplorationLogChanged;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        updateTimer.Tick += UpdateTimer_Tick;
        updateTimer.Start();
        RefreshContent(JournalMonitorService.Instance.Current);
        RefreshExplorationLog();
    }

    public void SetActivity(ActivityType value)
    {
        if (value != ActivityType.Exploration && fullExplorationVisible)
        {
            CloseFullExplorationView();
        }
        activity = value;
        RefreshContent(JournalMonitorService.Instance.Current);
    }

    public void SetTargetWindow(IntPtr windowHandle)
    {
        targetWindow = windowHandle;
        PositionOverlay();
    }

    public void SetPlacement(string value)
    {
        placement = value;
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
        InteractionHint.Text = enabled ? Loc.Get("Loc_DRAG_TO_MOVE") : Loc.Get("Loc_CTRL_6_INTERACT");
        DragHandle.Cursor = enabled ? Cursors.SizeAll : Cursors.Arrow;
        if (enabled && showCursor && IsVisible)
        {
            WindowsAPI.EnsureCursorVisibleOnWindow(this);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsAPI.SetupOverlayWindow(this);
        ApplyInteractionMode(interactive, showCursorWhenInteractive);
        PositionOverlay();
    }

    private void OnJournalStateChanged(object? sender, GameStateChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => RefreshContent(e.State)));

    private void RefreshContent(GameStateSnapshot state)
    {
        bool exploration = activity == ActivityType.Exploration;
        TitleText.Text = exploration ? Loc.Get("Loc_EXPLORATION_AND_EXOBIOLOGY") : Loc.Get("Loc_MINING");
        ModuleStatusText.Text = state.IsLive ? Loc.Get("Loc_JOURNAL_LIVE_2") : Loc.Get("Loc_JOURNAL_ASSISTANT");
        LocationText.Text = string.IsNullOrWhiteSpace(state.StarSystem)
            ? Loc.Get("Loc_SYSTEM")
            : Loc.Format("Loc_System_Format", state.StarSystem.ToUpperInvariant());
        FlightStateText.Text = BuildFlightState(state);
        ExplorationDataState externalData = ExplorationDataService.Instance.Current;
        ExternalDataText.Visibility = Visibility.Collapsed;
        ExternalDataText.Text = string.Empty;
        if (exploration)
        {
            string progress = BuildExplorationProgress(state, externalData);
            long scanValue = state.ExplorationBodies.Sum(body => body.EstimatedScanValue);
            long mappedValue = state.ExplorationBodies
                .Where(body => body.IsMapped)
                .Sum(body => body.MappingEfficient
                    ? body.EstimatedEfficientMappingValue
                    : body.EstimatedMappingValue);
            PrimaryHintText.Text = scanValue > 0 || mappedValue > 0
                ? progress + Environment.NewLine + Loc.Format("Loc_Exploration_local_value_format", scanValue, mappedValue)
                : progress;
            PlannedFeaturesText.Text = BuildCompactExplorationTarget(state, externalData);
            SurfaceNavigationText.Text = BuildSurfaceNavigation(state);
            SurfaceNavigationPanel.Visibility = state.ActiveOrganic is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            FuelAdvicePanel.Visibility = Visibility.Collapsed;
            ExplorationPoiPanel.Visibility = Visibility.Collapsed;
            FooterHintText.Text = BuildExplorationFooter(state);
            OpenExplorationAssistantButton.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(SpanshSourceTextBox.Text) && !string.IsNullOrWhiteSpace(state.StarSystem))
                SpanshSourceTextBox.Text = state.StarSystem;
            if (fullExplorationVisible)
            {
                RefreshCatalog(state, externalData);
                FullOverviewText.Text = BuildFullOverview(state, externalData);
                RefreshExplorationLog();
            }
        }
        else
        {
            SurfaceNavigationPanel.Visibility = Visibility.Collapsed;
            FuelAdvicePanel.Visibility = Visibility.Collapsed;
            ExplorationPoiPanel.Visibility = Visibility.Collapsed;
            ProspectedAsteroidSnapshot? prospect = state.LastProspectedAsteroid;
            PrimaryHintText.Text = prospect is null
                ? Loc.Get("Loc_Mining_waiting_for_prospector")
                : Loc.Format(
                    prospect.HasMotherlode ? "Loc_Mining_core_prospect_format" : "Loc_Mining_prospect_format",
                    prospect.HasMotherlode ? prospect.MotherlodeMaterial : prospect.Content,
                    prospect.Remaining);
            string leadingMaterials = prospect is null
                ? Loc.Get("Loc_No_prospect_data")
                : string.Join(" · ", prospect.Materials.Take(3)
                    .Select(material => $"{material.Name} {material.Proportion:0.#}%"));
            PlannedFeaturesText.Text = Loc.Format(
                "Loc_Mining_session_format",
                state.RefinedMiningUnits,
                state.CrackedAsteroids,
                leadingMaterials);
            FooterHintText.Text = Loc.Get("Loc_Switch_activities_in_the_main_window");
            OpenExplorationAssistantButton.Visibility = Visibility.Collapsed;
        }
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
        CatalogSourceText.Text = sourceMode + Environment.NewLine + (import.IsRunning
            ? Loc.Format("Loc_Exploration_history_import_progress_format", import.ProcessedFiles, import.TotalFiles)
            : Loc.Format("Loc_Exploration_history_status_format", history.Bodies.Count));
        ExplorationRoutePlan route = ExplorationRouteService.Instance.Current;
        ExplorationRouteInfoText.Text = BuildFullRouteSummary(route);
        NextRouteSystemLink.Text = route.NextStop?.System ?? string.Empty;
        NextRouteSystemLink.Tag = route.NextStop?.System;
        NextRouteSystemLink.Visibility = route.NextStop is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshRouteStops(route);
        ExplorationPoiSnapshot? poi = ExplorationPoiService.Instance.Current.Closest;
        CopyPoiSystemButton.IsEnabled = !string.IsNullOrWhiteSpace(poi?.System);
        OpenPoiDetailsButton.IsEnabled = Uri.TryCreate(poi?.Url, UriKind.Absolute, out _);
        ApplyCatalogFilter();
    }

    private void ApplyCatalogFilter()
    {
        if (ExplorationBodiesGrid is null) return;
        string search = CatalogSearchTextBox?.Text.Trim() ?? string.Empty;
        string filter = (CatalogFilterComboBox?.SelectedItem as CatalogFilterOption)?.Value ?? "All";
        CatalogRow[] rows = catalog.Bodies
            .Where(body => string.IsNullOrWhiteSpace(search)
                           || body.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                           || body.Subtype.Contains(search, StringComparison.OrdinalIgnoreCase)
                           || body.Atmosphere.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(body => filter switch
            {
                "Notable" => body.IsNotable,
                "Valuable" => body.IsValuable,
                "Biological" => body.IsBiological,
                "Unmapped" => !body.MappedThisVisit && !body.MappedPreviously,
                "Landable" => body.Landable,
                _ => true
            })
            .OrderByDescending(body => body.IsBiological)
            .ThenByDescending(body => body.IsValuable)
            .ThenByDescending(body => body.EstimatedMappingValue)
            .ThenBy(body => body.DistanceFromArrivalLs)
            .Select(ToCatalogRow)
            .ToArray();
        ExplorationBodiesGrid.ItemsSource = rows;
        CatalogCountText.Text = Loc.Format("Loc_Exploration_catalog_count_format", rows.Length, catalog.Bodies.Count);
        if (rows.Length > 0)
        {
            ExplorationBodiesGrid.SelectedIndex = 0;
        }
        else
        {
            ShowSelectedBody(null);
        }
    }

    private static CatalogRow ToCatalogRow(ExplorationCatalogBody body) => new(
        body,
        body.Name,
        string.IsNullOrWhiteSpace(body.Subtype) ? body.Type : body.Subtype,
        BuildHighlightText(body),
        Loc.Format("Loc_Distance_Ls_Value", body.DistanceFromArrivalLs),
        body.EstimatedMappingValue > 0
            ? Loc.Format("Loc_Credits_Short_Format", body.EstimatedMappingValue)
            : Loc.Get("Loc_VALUE_UNKNOWN"),
        body.MappedThisVisit
            ? Loc.Get(body.EfficientlyMappedThisVisit ? "Loc_DSS_EFFICIENT" : "Loc_DSS_MAPPED")
            : body.MappedPreviously
                ? Loc.Get(body.EfficientlyMappedPreviously ? "Loc_HISTORY_DSS_EFFICIENT" : "Loc_HISTORY_DSS_MAPPED")
            : body.ScannedThisVisit
                ? Loc.Get("Loc_FSS_SCANNED")
                : body.ScannedPreviously
                    ? Loc.Get("Loc_HISTORY_SCANNED")
                : Loc.Get("Loc_COMMUNITY_DATA_ONLY"));

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
            SelectedBodyNameText.Text = Loc.Get("Loc_Select_a_body");
            SelectedBodyReasonText.Text = string.Empty;
            SelectedBodyDetailsText.Text = string.Empty;
            DssSelectedBodyText.Text = Loc.Get("Loc_Select_a_body");
            DssMappingResultText.Text = string.Empty;
            CopySelectedBodyButton.IsEnabled = false;
            BookmarkSelectedBodyButton.IsEnabled = false;
            return;
        }

        ExplorationCatalogBody body = row.Body;
        SelectedBodyNameText.Text = body.Name;
        DssSelectedBodyText.Text = body.Name;
        SelectedBodyReasonText.Text = row.Highlights;
        SelectedBodyDetailsText.Text = string.Join(Environment.NewLine,
            Loc.Format("Loc_BODY_TYPE_DETAIL", row.Type),
            Loc.Format("Loc_BODY_DISTANCE_DETAIL", body.DistanceFromArrivalLs),
            Loc.Format("Loc_BODY_SCAN_VALUE_DETAIL", body.EstimatedScanValue),
            Loc.Format("Loc_BODY_MAPPING_VALUE_DETAIL", body.EstimatedMappingValue),
            Loc.Format("Loc_BODY_GRAVITY_DETAIL", body.GravityG),
            Loc.Format("Loc_BODY_TEMPERATURE_DETAIL", body.SurfaceTemperatureKelvin),
            Loc.Format("Loc_BODY_PRESSURE_DETAIL", body.SurfacePressureAtmospheres),
            Loc.Format("Loc_BODY_ATMOSPHERE_DETAIL", EmptyAsUnknown(body.Atmosphere)),
            Loc.Format("Loc_BODY_VOLCANISM_DETAIL", EmptyAsUnknown(body.Volcanism)),
            Loc.Format("Loc_BODY_BIOLOGY_DETAIL", body.BiologicalSignals,
                body.Genuses.Count == 0 ? Loc.Get("Loc_VALUE_UNKNOWN") : string.Join(", ", body.Genuses)),
            Loc.Format("Loc_BODY_ORGANICS_HISTORY_DETAIL", body.CompletedOrganics),
            Loc.Format("Loc_BODY_SOURCE_DETAIL", LocalizeCatalogSource(body.Source)),
            BuildPredictionDetails(body));
        SetDssTarget(body.EfficiencyTarget > 0
            ? body.EfficiencyTarget
            : SettingsService.Instance.Settings.DssEfficiencyTarget);
        DssMappingResultText.Text = body.LastProbesUsed > 0 && body.EfficiencyTarget > 0
            ? Loc.Format(
                body.LastProbesUsed <= body.EfficiencyTarget
                    ? "Loc_DSS_RESULT_EFFICIENT"
                    : "Loc_DSS_RESULT_OVER_TARGET",
                body.LastProbesUsed,
                body.EfficiencyTarget)
            : Loc.Get("Loc_DSS_NO_RESULT_YET");
        CopySelectedBodyButton.IsEnabled = !string.IsNullOrWhiteSpace(body.Name);
        BookmarkSelectedBodyButton.IsEnabled = !string.IsNullOrWhiteSpace(body.Name);
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

    private void OpenExplorationAssistantButton_Click(object sender, RoutedEventArgs e)
    {
        if (activity != ActivityType.Exploration || fullExplorationVisible) return;
        fullExplorationVisible = true;
        RefreshContent(JournalMonitorService.Instance.Current);
        CompactPanel.Visibility = Visibility.Collapsed;
        FullExplorationPanel.Visibility = Visibility.Visible;
        MinWidth = 920;
        MinHeight = 620;
        PositionOverlay();
        parentWindow?.BeginExclusiveOverlayInteraction();
        Activate();
        WindowsAPI.TryActivateWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        WindowsAPI.EnsureCursorVisibleOnWindow(this);
    }

    private void CloseExplorationAssistantButton_Click(object sender, RoutedEventArgs e) => CloseFullExplorationView();

    private void CloseFullExplorationView()
    {
        if (!fullExplorationVisible) return;
        fullExplorationVisible = false;
        FullExplorationPanel.Visibility = Visibility.Collapsed;
        CompactPanel.Visibility = Visibility.Visible;
        MinWidth = 0;
        MinHeight = 0;
        Width = 400;
        Height = 330;
        parentWindow?.EndExclusiveOverlayInteraction();
        PositionOverlay();
    }

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
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ExplorationRouteService.Instance.Import(dialog.FileName);
            ShowImportedRoute();
            RefreshContent(JournalMonitorService.Instance.Current);
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

    private async Task NavigateToNextRouteSystemAsync(bool confirmAutomatically)
    {
        string target = ExplorationRouteService.Instance.Current.NextStop?.System ?? string.Empty;
        if (string.IsNullOrWhiteSpace(target))
        {
            RouteNavigationStatusText.Text = Loc.Get("Loc_NAVIGATION_NO_TARGET");
            return;
        }

        routeNavigationCancellation?.Cancel();
        routeNavigationCancellation?.Dispose();
        routeNavigationCancellation = new CancellationTokenSource();
        Clipboard.SetText(target);
        RouteNavigationStatusText.Text = Loc.Format("Loc_NAVIGATION_PREPARING", target);
        if (fullExplorationVisible) CloseFullExplorationView();
        WindowsAPI.SetClickThrough(this, true);
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            EliteNavigationResult result = await EliteRouteNavigationService.Instance.PrepareAsync(
                target, targetWindow, confirmAutomatically, routeNavigationCancellation.Token);
            RouteNavigationStatusText.Text = string.IsNullOrWhiteSpace(result.Detail)
                ? Loc.Format(result.MessageKey, result.TargetSystem)
                : Loc.Format(result.MessageKey, result.TargetSystem, result.Detail);
        }
        finally
        {
            ApplyInteractionMode(interactive, showCursorWhenInteractive);
        }
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
            RefreshContent(JournalMonitorService.Instance.Current);
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

    private sealed record CatalogRow(
        ExplorationCatalogBody Body,
        string Name,
        string Type,
        string Highlights,
        string Distance,
        string MappingValue,
        string Progress);

    private static string BuildExplorationProgress(GameStateSnapshot state, ExplorationDataState externalData)
    {
        int knownBodyCount = Math.Max(
            state.SystemBodyCount,
            externalData.System is { } system
                && string.Equals(system.SystemName, state.StarSystem, StringComparison.OrdinalIgnoreCase)
                    ? system.BodyCount
                    : 0);
        int resolvedBodyCount = knownBodyCount == 0
            ? state.ScannedBodies
            : Math.Clamp((int)Math.Round(knownBodyCount * state.FssProgress), 0, knownBodyCount);

        string progress = Loc.Format(
            "Loc_Exploration_progress_detailed_format",
            Math.Round(state.FssProgress * 100),
            resolvedBodyCount,
            knownBodyCount,
            state.ScannedBodies,
            state.MappedBodies,
            state.EfficientMappings,
            state.BiologicalSignals,
            state.BiologicalBodies);

        if (state.FssProgress >= 0.999 && state.ScannedBodies == 0 && knownBodyCount > 0)
        {
            string source = externalData.System is { } current
                && string.Equals(current.SystemName, state.StarSystem, StringComparison.OrdinalIgnoreCase)
                    ? current.Source
                    : Loc.Get("Loc_Exploration_journal_source");
            progress += Environment.NewLine + Loc.Format("Loc_Exploration_no_scan_events_explanation", source);
        }

        return progress;
    }

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
        FuelRouteAssessment fuel = FuelRouteAdvisor.Evaluate(state);
        var parts = new List<string>
        {
            BuildExplorationProgress(state, externalData),
            BuildEarningsSummary(ExplorationEarningsService.Instance.Current),
            BuildRouteSummary(ExplorationRouteService.Instance.Current).Trim(),
            BuildExplorationTargets(state, externalData)
        };
        string fuelText = BuildFuelAdvice(fuel);
        if (!string.IsNullOrWhiteSpace(fuelText)) parts.Add(fuelText);
        string poi = BuildPoiSummary(ExplorationPoiService.Instance.Current);
        if (!string.IsNullOrWhiteSpace(poi)) parts.Add(poi);
        return string.Join(Environment.NewLine, parts.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildExobiologySummary(GameStateSnapshot state)
    {
        string organicName = !string.IsNullOrWhiteSpace(state.LastOrganicVariant)
            ? state.LastOrganicVariant
            : state.LastOrganicSpecies;
        ExplorationBodySnapshot[] targets = state.ExplorationBodies
            .Where(body => body.BiologicalSignals > 0)
            .OrderByDescending(body => body.BiologicalSignals)
            .Take(4)
            .ToArray();
        string targetSummary = targets.Length == 0
            ? Loc.Get(state.FssProgress >= 0.999
                ? "Loc_Exobiology_no_signals_after_fss"
                : "Loc_Exobiology_no_signals")
            : Loc.Format(
                "Loc_Exobiology_targets_format",
                string.Join(Environment.NewLine, targets.Select(FormatBiologyTarget)));

        return state.CompletedOrganicSamples > 0
            ? Loc.Format("Loc_Exobiology_completed_format", state.CompletedOrganicSamples, organicName) + Environment.NewLine + targetSummary
            : targetSummary;
    }

    private static string BuildCompactExplorationTarget(GameStateSnapshot state, ExplorationDataState externalData)
    {
        if (state.ActiveOrganic is not null)
        {
            return string.Empty;
        }

        ExplorationBodySnapshot? biology = state.ExplorationBodies
            .Where(body => body.BiologicalSignals > 0)
            .OrderByDescending(body => body.BiologicalSignals)
            .ThenByDescending(body => body.MaximumBiologyValue)
            .FirstOrDefault();
        if (biology is not null)
        {
            return Loc.Format("Loc_COMPACT_BIO_TARGET_FORMAT", biology.Name, biology.BiologicalSignals);
        }

        HashSet<int> mappedIds = state.ExplorationBodies.Where(body => body.IsMapped).Select(body => body.BodyId).ToHashSet();
        HashSet<string> mappedNames = state.ExplorationBodies.Where(body => body.IsMapped).Select(body => body.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<MappingTarget> local = state.ExplorationBodies
            .Where(body => body.BodyType.Equals("Planet", StringComparison.OrdinalIgnoreCase)
                           && !body.IsMapped && body.EstimatedEfficientMappingValue >= 100_000)
            .Select(body => new MappingTarget(body.BodyId, body.Name, body.EstimatedEfficientMappingValue,
                body.DistanceFromArrivalLs, true));
        IEnumerable<MappingTarget> external = externalData.System is { } system
            && string.Equals(system.SystemName, state.StarSystem, StringComparison.OrdinalIgnoreCase)
            ? system.Bodies
                .Where(body => body.Type.Equals("Planet", StringComparison.OrdinalIgnoreCase)
                               && body.EstimatedMappingValue >= 100_000
                               && !mappedIds.Contains(body.BodyId) && !mappedNames.Contains(body.Name))
                .Select(body => new MappingTarget(body.BodyId, body.Name, body.EstimatedMappingValue,
                    body.DistanceFromArrivalLs, false))
            : Array.Empty<MappingTarget>();
        MappingTarget? best = local.Concat(external)
            .GroupBy(body => body.BodyId >= 0 ? $"id:{body.BodyId}" : $"name:{body.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(body => body.IsLocal).ThenByDescending(body => body.Value).First())
            .OrderByDescending(body => body.Value)
            .FirstOrDefault();
        if (best is not null)
        {
            return Loc.Format("Loc_COMPACT_MAPPING_TARGET_FORMAT", best.Name, best.Value, best.DistanceFromArrivalLs);
        }

        return state.FssProgress < 0.999
            ? Loc.Get("Loc_COMPACT_FSS_HINT")
            : Loc.Get("Loc_COMPACT_SYSTEM_COMPLETE");
    }

    private static string BuildExplorationTargets(GameStateSnapshot state, ExplorationDataState externalData)
    {
        string biology = BuildExobiologySummary(state);
        ExplorationSystemDataSnapshot? system = externalData.System;

        HashSet<int> mappedIds = state.ExplorationBodies
            .Where(body => body.IsMapped)
            .Select(body => body.BodyId)
            .ToHashSet();
        HashSet<string> mappedNames = state.ExplorationBodies
            .Where(body => body.IsMapped)
            .Select(body => body.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<MappingTarget> localTargets = state.ExplorationBodies
            .Where(body => body.BodyType.Equals("Planet", StringComparison.OrdinalIgnoreCase))
            .Where(body => body.EstimatedEfficientMappingValue >= 100_000)
            .Where(body => !body.IsMapped)
            .Select(body => new MappingTarget(
                body.BodyId,
                body.Name,
                body.EstimatedEfficientMappingValue,
                body.DistanceFromArrivalLs,
                true));
        IEnumerable<MappingTarget> externalTargets = system is not null
            && string.Equals(system.SystemName, state.StarSystem, StringComparison.OrdinalIgnoreCase)
            ? system.Bodies
                .Where(body => body.Type.Equals("Planet", StringComparison.OrdinalIgnoreCase))
                .Where(body => body.EstimatedMappingValue >= 100_000)
                .Where(body => !mappedIds.Contains(body.BodyId) && !mappedNames.Contains(body.Name))
                .Select(body => new MappingTarget(
                    body.BodyId,
                    body.Name,
                    body.EstimatedMappingValue,
                    body.DistanceFromArrivalLs,
                    false))
            : Array.Empty<MappingTarget>();
        MappingTarget[] valuable = localTargets
            .Concat(externalTargets)
            .GroupBy(body => body.BodyId >= 0 ? $"id:{body.BodyId}" : $"name:{body.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(body => body.IsLocal).ThenByDescending(body => body.Value).First())
            .OrderByDescending(body => body.Value)
            .Take(3)
            .ToArray();
        if (valuable.Length == 0) return biology;
        string lines = string.Join(Environment.NewLine, valuable.Select(body => Loc.Format(
            "Loc_Exploration_external_target_line_format",
            body.Name,
            body.Value,
            body.DistanceFromArrivalLs)));
        return Loc.Format("Loc_Exploration_external_targets_format", lines)
               + Environment.NewLine + Environment.NewLine + biology;
    }

    private static string BuildRouteSummary(ExplorationRoutePlan route)
    {
        if (route.Stops.Count == 0)
            return Loc.Get("Loc_NO_EXPLORATION_ROUTE") + Environment.NewLine + Environment.NewLine;
        ExplorationRouteStop? current = route.CurrentStop;
        ExplorationRouteStop? next = route.NextStop;
        string currentTargets = current is { Bodies.Count: > 0 }
            ? Loc.Format("Loc_ROUTE_CURRENT_TARGETS_FORMAT", current.System,
                string.Join(", ", current.Bodies.Select(body => body.Name)), current.EstimatedValue)
            : string.Empty;
        string nextLine = next is null
            ? Loc.Get("Loc_EXPLORATION_ROUTE_COMPLETE")
            : Loc.Format("Loc_ROUTE_NEXT_COMPACT_FORMAT", next.System, route.CurrentIndex + 2, route.Stops.Count);
        return Loc.Get("Loc_SPANSH_ROUTE") + Environment.NewLine
               + (string.IsNullOrWhiteSpace(currentTargets) ? string.Empty : currentTargets + Environment.NewLine)
               + nextLine + Environment.NewLine + Environment.NewLine;
    }

    private static string BuildFullRouteSummary(ExplorationRoutePlan route)
    {
        if (route.Stops.Count == 0) return Loc.Get("Loc_NO_EXPLORATION_ROUTE");
        return route.NextStop is { } next
            ? Loc.Format("Loc_ROUTE_FULL_STATUS_FORMAT", route.SourceFile, route.CurrentIndex + 1,
                route.Stops.Count, next.System)
            : Loc.Format("Loc_ROUTE_FULL_COMPLETE_FORMAT", route.SourceFile, route.Stops.Count);
    }

    private static string BuildPoiSummary(ExplorationPoiState state)
    {
        if (state.Status == ExplorationPoiStatus.Loading && state.Nearest is null)
            return Loc.Get("Loc_POI_LOADING");
        if (state.Status == ExplorationPoiStatus.Unavailable && state.Nearest is null)
            return Loc.Get("Loc_POI_UNAVAILABLE");
        var lines = new List<string>();
        if (state.Nearest is { } poi)
        {
            string summary = Loc.Format("Loc_POI_NEAREST_FORMAT", poi.Name, poi.System, poi.DistanceLy, poi.Rating,
                poi.Category, poi.Region);
            lines.Add(string.IsNullOrWhiteSpace(poi.Summary) ? summary : summary + Environment.NewLine + poi.Summary);
        }
        if (state.NearestCanonn is { } canonn)
        {
            lines.Add(Loc.Format("Loc_CANONN_POI_NEAREST_FORMAT", canonn.Category, canonn.System, canonn.DistanceLy,
                canonn.Summary));
        }
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private sealed record MappingTarget(
        int BodyId,
        string Name,
        long Value,
        double DistanceFromArrivalLs,
        bool IsLocal);

    private static string BuildExternalDataStatus(GameStateSnapshot state, ExplorationDataState externalData)
    {
        return externalData.Status switch
        {
            ExplorationDataStatus.Disabled => Loc.Get("Loc_Exploration_online_disabled"),
            ExplorationDataStatus.Loading => Loc.Get("Loc_Exploration_online_loading"),
            ExplorationDataStatus.Unavailable => Loc.Get("Loc_Exploration_online_unavailable"),
            ExplorationDataStatus.Available when externalData.System is { } system
                && string.Equals(system.SystemName, state.StarSystem, StringComparison.OrdinalIgnoreCase) => Loc.Format(
                    system.FromCache ? "Loc_Exploration_online_cache_format" : "Loc_Exploration_online_source_format",
                    system.Source,
                    system.BodyCount,
                    system.IsStale ? Loc.Get("Loc_Exploration_data_stale") : string.Empty),
            _ => Loc.Get("Loc_Exploration_online_waiting")
        };
    }

    private static string FormatBiologyTarget(ExplorationBodySnapshot body)
    {
        string genuses = body.Genuses.Count == 0
            ? Loc.Get("Loc_Exobiology_unknown_genus")
            : string.Join(", ", body.Genuses.Take(3));
        string value = body.MaximumBiologyValue > 0
            ? Loc.Format("Loc_Exobiology_value_range_format", body.MinimumBiologyValue, body.MaximumBiologyValue)
            : Loc.Get("Loc_Exobiology_value_unknown");
        string line = Loc.Format(
            "Loc_Exobiology_target_line_format",
            body.Name,
            body.BiologicalSignals,
            genuses,
            value,
            body.GravityG);
        ExobiologyPrediction? prediction = ExobiologyPredictionService.Instance.Predict(body, 1).FirstOrDefault();
        return prediction is null
            ? line
            : line + Environment.NewLine + Loc.Format(
                "Loc_BIO_PREDICTION_COMPACT_FORMAT", prediction.Species,
                prediction.RelativeProbability * 100, prediction.BaseValue);
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

    private string BuildSurfaceNavigation(GameStateSnapshot state)
    {
        OrganicScanProgressSnapshot? active = state.ActiveOrganic;
        if (active is null) return string.Empty;
        string name = !string.IsNullOrWhiteSpace(active.Variant) ? active.Variant : active.Species;
        SurfaceNavigationResult? navigation = SurfaceNavigationCalculator.Calculate(
            state.Latitude,
            state.Longitude,
            state.HeadingDegrees,
            state.PlanetRadiusMeters,
            active.LastSampleLatitude,
            active.LastSampleLongitude);
        if (navigation is null || active.ColonyRangeMeters <= 0)
        {
            return Loc.Format("Loc_Exobiology_sample_requirement_format", name, active.Stage, active.ColonyRangeMeters);
        }
        double remaining = Math.Max(0, active.ColonyRangeMeters - navigation.DistanceMeters);
        SurfaceEscapeArrowTransform.Angle = navigation.EscapeRelativeTurnDegrees;
        return navigation.IsFarEnough(active.ColonyRangeMeters)
            ? Loc.Format("Loc_Exobiology_distance_ready_format", name, active.Stage, navigation.DistanceMeters)
            : Loc.Format(
                "Loc_Exobiology_distance_remaining_format",
                name,
                active.Stage,
                navigation.DistanceMeters,
                remaining,
                active.ColonyRangeMeters)
              + Environment.NewLine
              + Loc.Format("Loc_EXOBIO_ESCAPE_DIRECTION_FORMAT",
                  navigation.EscapeBearingDegrees, FormatRelativeTurn(navigation.EscapeRelativeTurnDegrees));
    }

    private static string FormatRelativeTurn(double degrees)
    {
        if (Math.Abs(degrees) < 5) return Loc.Get("Loc_STRAIGHT_AHEAD");
        return Loc.Format(degrees < 0 ? "Loc_TURN_LEFT_FORMAT" : "Loc_TURN_RIGHT_FORMAT", Math.Abs(degrees));
    }

    private static string BuildExplorationFooter(GameStateSnapshot state)
    {
        ExplorationBodySnapshot? notable = state.ExplorationBodies.LastOrDefault(body => body.IsNotable);
        if (notable is not null)
        {
            return Loc.Format(
                "Loc_Exploration_notable_format",
                notable.Name,
                Loc.Get(notable.Interest switch
                {
                    ExplorationInterest.EarthLike => "Loc_Interest_EarthLike",
                    ExplorationInterest.WaterWorld => "Loc_Interest_WaterWorld",
                    ExplorationInterest.AmmoniaWorld => "Loc_Interest_AmmoniaWorld",
                    ExplorationInterest.NeutronStar => "Loc_Interest_NeutronStar",
                    ExplorationInterest.BlackHole => "Loc_Interest_BlackHole",
                    _ => "Loc_Interest_Terraformable"
                }),
                notable.DistanceFromArrivalLs);
        }
        NavRouteStar? hazardous = state.NavRoute.Skip(1).FirstOrDefault(star => star.IsNeutron || star.IsWhiteDwarf);
        if (hazardous is not null)
        {
            return Loc.Format("Loc_Exploration_route_hazard_format", hazardous.System, hazardous.StarClass);
        }
        if (state.NavRoute.Count > 2 && state.NavRoute.Skip(1).Take(state.NavRoute.Count - 2).All(star => !star.IsScoopable))
        {
            return Loc.Get("Loc_Exploration_route_no_scoopable_stars");
        }
        return state.NewCodexEntries > 0
            ? Loc.Format("Loc_Exploration_codex_format", state.NewCodexEntries)
            : Loc.Get("Loc_Exploration_scan_hint");
    }

    private static string BuildFuelAdvice(FuelRouteAssessment fuel)
    {
        string status = Loc.Get(fuel.Severity switch
        {
            FuelRouteSeverity.Critical => "Loc_FUEL_CRITICAL",
            FuelRouteSeverity.Caution => "Loc_FUEL_CAUTION",
            FuelRouteSeverity.Safe => "Loc_FUEL_SAFE",
            _ => "Loc_FUEL_UNKNOWN"
        });
        string line = Loc.Format("Loc_FUEL_ROUTE_STATUS_FORMAT", status, fuel.FuelPercent, fuel.RemainingJumps);
        if (fuel.JumpsToNextScoopable is not { } jumps)
        {
            return fuel.RemainingJumps > 0
                ? line + Environment.NewLine + Loc.Get("Loc_FUEL_NO_SCOOPABLE_ON_ROUTE")
                : line;
        }
        string next = Loc.Format("Loc_FUEL_NEXT_SCOOPABLE_FORMAT", fuel.NextScoopableSystem, jumps);
        if (fuel.EstimatedFuelToNextScoopable is not { } needed) return line + Environment.NewLine + next;
        return line + Environment.NewLine + next + Environment.NewLine
               + Loc.Format("Loc_FUEL_ESTIMATE_FORMAT", needed, fuel.EmergencyReserve);
    }

    public void RefreshLocalization() => RefreshContent(JournalMonitorService.Instance.Current);

    private static string BuildFlightState(GameStateSnapshot state)
    {
        if (!state.JournalAvailable) return Loc.Get("Loc_Waiting_for_Elite_Dangerous_journal");
        if (state.Docked && !string.IsNullOrWhiteSpace(state.Station)) return Loc.Format("Loc_Docked_Format", state.Station);
        if (state.InSupercruise) return Loc.Get("Loc_IN_SUPERCRUISE");
        if (!string.IsNullOrWhiteSpace(state.Destination)) return Loc.Format("Loc_Destination_Format", state.Destination);
        return string.IsNullOrWhiteSpace(state.Ship) ? Loc.Get("Loc_JOURNAL_CONNECTED_2") : Loc.Format("Loc_Ship_Format", state.Ship);
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (disposed || targetWindow == IntPtr.Zero) return;
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
        if (targetWindow == IntPtr.Zero || !WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect)) return;
        double targetWidth = rect.Right - rect.Left;
        double targetHeight = rect.Bottom - rect.Top;
        if (fullExplorationVisible)
        {
            Width = Math.Min(1180, Math.Max(MinWidth, targetWidth - 64));
            Height = Math.Min(760, Math.Max(MinHeight, targetHeight - 64));
            Left = rect.Left + (targetWidth - Width) / 2.0;
            Top = rect.Top + (targetHeight - Height) / 2.0;
            return;
        }
        Rect workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
        double left;
        double top;
        if (hasManualPosition)
        {
            left = rect.Left + (Math.Max(0, rect.Right - rect.Left - Width) * manualXRatio);
            top = rect.Top + (Math.Max(0, rect.Bottom - rect.Top - Height) * manualYRatio);
        }
        else
        {
            (left, top) = OverlayLayoutHelper.GetPinnedPosition(rect, Width, Height, placement, 16);
        }
        OverlayLayoutHelper.ClampPosition(ref left, ref top, Width, Height, workArea, 10, 10);
        Left = left;
        Top = top;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!interactive || e.LeftButton != MouseButtonState.Pressed) return;
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
        catch (InvalidOperationException) { }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (disposed) return;
        disposed = true;
        if (fullExplorationVisible) parentWindow?.EndExclusiveOverlayInteraction();
        updateTimer.Stop();
        updateTimer.Tick -= UpdateTimer_Tick;
        JournalMonitorService.Instance.StateChanged -= OnJournalStateChanged;
        ExplorationDataService.Instance.DataChanged -= OnExplorationDataChanged;
        ExplorationHistoryService.Instance.HistoryChanged -= OnExplorationHistoryChanged;
        ExplorationRouteService.Instance.RouteChanged -= OnExplorationRouteChanged;
        ExplorationPoiService.Instance.PoiChanged -= OnExplorationPoiChanged;
        ExplorationEarningsService.Instance.Changed -= OnExplorationEarningsChanged;
        ExplorationLogService.Instance.Changed -= OnExplorationLogChanged;
        SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
        routeNavigationCancellation?.Cancel();
        routeNavigationCancellation?.Dispose();
        spanshRouteClient.Dispose();
    }

    private void OnExplorationDataChanged(object? sender, ExplorationDataChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => RefreshContent(JournalMonitorService.Instance.Current)));

    private void OnExplorationHistoryChanged(object? sender, ExplorationHistoryChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => RefreshContent(JournalMonitorService.Instance.Current)));

    private void OnExplorationRouteChanged(object? sender, ExplorationRouteChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => RefreshContent(JournalMonitorService.Instance.Current)));

    private void OnExplorationPoiChanged(object? sender, ExplorationPoiChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => RefreshContent(JournalMonitorService.Instance.Current)));

    private void OnExplorationEarningsChanged(object? sender, ExplorationEarningsChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => RefreshContent(JournalMonitorService.Instance.Current)));

    private void OnExplorationLogChanged(object? sender, ExplorationLogChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(RefreshExplorationLog));

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SetChromeStyle(e.Settings.OverlayChromeStyle);
            ApplyRoutePanelState();
            RefreshContent(JournalMonitorService.Instance.Current);
        }));
}
