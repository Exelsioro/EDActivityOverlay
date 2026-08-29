using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Ardent;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl : UserControl, IDisposable
{
    private sealed record TradeRow(
        TradeRouteCandidate Candidate,
        string Key,
        string HeldMarker,
        string Commodity,
        string Source,
        string Target,
        string ProfitPerTon,
        string ProfitPerTrip,
        string TradeLegDistance,
        string Age);

    private sealed class SessionState
    {
        public bool HasValues { get; set; }
        public string Anchor { get; set; } = string.Empty;
        public string Cargo { get; set; } = string.Empty;
        public int SourceRadius { get; set; } = 30;
        public int TargetRadius { get; set; } = 80;
        public int MaxAgeHours { get; set; } = 72;
        public bool IncludeCarriers { get; set; }
        public int MinPad { get; set; } = 3;
        public int MaxStationDistance { get; set; }
        public int MinSupply { get; set; } = 1;
        public int MinDemand { get; set; } = 1;
        public bool AdvancedOpen { get; set; }
        public string Sort { get; set; } = "profit";
    }

    private static readonly SessionState Session = new();

    private readonly TradeSearchService searchService = new();
    private CancellationTokenSource? searchCancellation;
    private List<TradeRouteCandidate> currentCandidates = new();
    private TradeRouteCandidate? selectedCandidate;
    private GameStateSnapshot currentJournal = new();
    private bool applyingJournal;
    private bool systemOverridden;
    private bool cargoOverridden;
    private bool disposed;

    public TradeWorkspaceControl()
    {
        InitializeComponent();

        applyingJournal = true;
        try
        {
            SourceRadiusComboBox.SelectedIndex = 2;
            TargetRadiusComboBox.SelectedIndex = 6;
            MaxAgeComboBox.SelectedIndex = 4;
            MinPadComboBox.SelectedIndex = 2;
            MaxStationDistanceComboBox.SelectedIndex = 0;
            MinSupplyComboBox.SelectedIndex = 0;
            MinDemandComboBox.SelectedIndex = 0;
            SortComboBox.SelectedIndex = 0;
        }
        finally
        {
            applyingJournal = false;
        }

        AnchorSystemTextBox.TextChanged += (_, _) =>
        {
            if (!applyingJournal)
            {
                systemOverridden = true;
                CaptureSession();
            }
        };

        CargoTextBox.TextChanged += (_, _) =>
        {
            if (!applyingJournal)
            {
                cargoOverridden = true;
                CaptureSession();
            }
        };

        SourceRadiusComboBox.SelectionChanged += (_, _) => CaptureSession();
        TargetRadiusComboBox.SelectionChanged += (_, _) => CaptureSession();
        MaxAgeComboBox.SelectionChanged += (_, _) => CaptureSession();
        MinPadComboBox.SelectionChanged += (_, _) => CaptureSession();
        MaxStationDistanceComboBox.SelectionChanged += (_, _) => CaptureSession();
        MinSupplyComboBox.SelectionChanged += (_, _) => CaptureSession();
        MinDemandComboBox.SelectionChanged += (_, _) => CaptureSession();
        FleetCarriersCheckBox.Checked += (_, _) => CaptureSession();
        FleetCarriersCheckBox.Unchecked += (_, _) => CaptureSession();
        AdvancedFiltersExpander.Expanded += (_, _) => CaptureSession();
        AdvancedFiltersExpander.Collapsed += (_, _) => CaptureSession();

        if (Session.HasValues)
        {
            ApplySession();
        }

        UpdateJournalState(JournalMonitorService.Instance.Current);
        RefreshFooter();
    }

    public event Action? CloseRequested;
    public event Action<TradeRouteCandidate>? PinRequested;

    public void UpdateJournalState(GameStateSnapshot state)
    {
        currentJournal = state;

        string ship =
            string.IsNullOrWhiteSpace(state.ShipName)
                ? state.Ship
                : state.ShipName;

        string location =
            string.IsNullOrWhiteSpace(state.StarSystem)
                ? Loc.Get("Loc_waiting_for_location")
                : state.StarSystem;

        string cargo =
            state.CargoCapacity > 0
                ? Loc.Format(
                    "Loc_Free_Cargo_Format",
                    state.FreeCargo,
                    state.CargoCapacity)
                : Loc.Get("Loc_cargo_unknown");

        JournalContextText.Text =
            $"{location}  •  "
            + $"{(string.IsNullOrWhiteSpace(ship) ? Loc.Get("Loc_ship_unknown") : ship)}"
            + $"  •  {cargo}";

        applyingJournal = true;
        try
        {
            if (!systemOverridden
                && !Session.HasValues
                && !string.IsNullOrWhiteSpace(state.StarSystem))
            {
                AnchorSystemTextBox.Text = state.StarSystem;
            }

            if (!cargoOverridden
                && !Session.HasValues
                && state.CargoCapacity > 0)
            {
                CargoTextBox.Text = state.FreeCargo.ToString(
                    CultureInfo.CurrentCulture);
            }
        }
        finally
        {
            applyingJournal = false;
        }
    }

    public void RefreshLocalization()
    {
        UpdateJournalState(currentJournal);
        ShowSelectedCandidate(selectedCandidate);
        RefreshFooter();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = null;
    }

    private void SyncJournalButton_Click(object sender, RoutedEventArgs e)
    {
        systemOverridden = false;
        cargoOverridden = false;
        Session.HasValues = false;

        applyingJournal = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(currentJournal.StarSystem))
            {
                AnchorSystemTextBox.Text = currentJournal.StarSystem;
            }

            if (currentJournal.CargoCapacity > 0)
            {
                CargoTextBox.Text =
                    currentJournal.FreeCargo.ToString(
                        CultureInfo.CurrentCulture);
            }
        }
        finally
        {
            applyingJournal = false;
        }

        CaptureSession();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (searchCancellation is not null)
        {
            searchCancellation.Cancel();
            SearchStatusText.Text =
                Loc.Get("Loc_TRADE_SEARCH_CANCELLING");
            return;
        }

        if (!TryBuildConstraints(
                out TradeSearchConstraints constraints,
                out string error))
        {
            SearchStatusText.Text = error;
            return;
        }

        CaptureSession();

        var cancellation = new CancellationTokenSource();
        searchCancellation = cancellation;
        currentCandidates = new List<TradeRouteCandidate>();
        selectedCandidate = null;
        RoutesGrid.ItemsSource = null;
        ShowSelectedCandidate(null);
        SetSearchRunning(true);

        try
        {
            await foreach (TradeSearchProgress progress
                           in searchService.SearchProgressAsync(
                               constraints,
                               cancellation.Token))
            {
                ApplyProgress(progress);
            }
        }
        catch (OperationCanceledException)
        {
            SearchStatusText.Text =
                Loc.Format(
                    "Loc_TRADE_SEARCH_CANCELLED",
                    currentCandidates.Count);
        }
        catch (ArdentApiException ex)
        {
            SearchStatusText.Text =
                Loc.Format(
                    "Loc_TRADE_SEARCH_ERROR",
                    $"{(int)ex.StatusCode} {ex.StatusCode}");

            Logger.Logger.Error(
                $"Unified Trade workspace Ardent search failed: {ex}");
        }
        catch (Exception ex)
        {
            SearchStatusText.Text =
                Loc.Format(
                    "Loc_TRADE_SEARCH_ERROR",
                    ex.Message);

            Logger.Logger.Error(
                $"Unified Trade workspace search failed: {ex}");
        }
        finally
        {
            if (ReferenceEquals(
                    searchCancellation,
                    cancellation))
            {
                searchCancellation = null;
            }

            cancellation.Dispose();
            SetSearchRunning(false);
            RefreshFooter();
        }
    }

    private void ApplyProgress(TradeSearchProgress progress)
    {
        switch (progress.Stage)
        {
            case TradeSearchStage.ResolvingOrigin:
                SearchStatusText.Text =
                    Loc.Get("Loc_TRADE_SEARCH_RESOLVING");
                return;

            case TradeSearchStage.LoadingCommodityReports:
                SearchStatusText.Text =
                    Loc.Get("Loc_TRADE_SEARCH_LOADING_MARKET");
                return;

            case TradeSearchStage.Searching:
                SearchStatusText.Text =
                    progress.FailedCommodities > 0
                        ? Loc.Format(
                            "Loc_TRADE_SEARCH_PROGRESS_FAILED",
                            progress.CompletedCommodities,
                            progress.TotalCommodities,
                            progress.BestCandidates.Count,
                            progress.FailedCommodities)
                        : Loc.Format(
                            "Loc_TRADE_SEARCH_PROGRESS",
                            progress.CompletedCommodities,
                            progress.TotalCommodities,
                            progress.BestCandidates.Count);

                if (progress.BestCandidates.Count > 0)
                {
                    ApplyCandidates(progress.BestCandidates);
                }

                FooterText.Text =
                    Loc.Format(
                        "Loc_TRADE_WORKSPACE_PROGRESS_FOOTER",
                        progress.CompletedCommodities,
                        progress.TotalCommodities,
                        progress.Elapsed.TotalSeconds);

                return;

            case TradeSearchStage.Completed:
                if (progress.BestCandidates.Count > 0)
                {
                    ApplyCandidates(progress.BestCandidates);
                }

                SearchStatusText.Text =
                    Loc.Format(
                        "Loc_TRADE_SEARCH_DONE",
                        currentCandidates.Count,
                        progress.Elapsed.TotalSeconds);

                RefreshFooter();
                return;
        }
    }

    private void ApplyCandidates(
        IReadOnlyList<TradeRouteCandidate> candidates)
    {
        string? selectedKey =
            selectedCandidate is null
                ? null
                : Key(selectedCandidate);

        currentCandidates =
            candidates.ToList();

        IEnumerable<TradeRouteCandidate> ordered =
            SortTag() switch
            {
                "perton" =>
                    currentCandidates
                        .OrderByDescending(item => item.ProfitPerTon)
                        .ThenByDescending(item => item.ProfitPerTrip)
                        .ThenBy(item => item.SourceToTargetDistanceLy),

                "distance" =>
                    currentCandidates
                        .OrderBy(item => item.SourceToTargetDistanceLy)
                        .ThenByDescending(item => item.ProfitPerTrip),

                _ =>
                    currentCandidates
                        .OrderByDescending(item => item.ProfitPerTrip)
                        .ThenByDescending(item => item.ProfitPerTon)
                        .ThenBy(item => item.SourceToTargetDistanceLy)
            };

        var displayed =
            ordered.ToList();

        bool heldSelection =
            selectedCandidate is not null
            && displayed.All(
                item =>
                    !Key(item).Equals(
                        selectedKey,
                        StringComparison.Ordinal));

        if (heldSelection
            && selectedCandidate is not null)
        {
            displayed.Add(selectedCandidate);
        }

        TradeRow[] rows =
            displayed
                .Select(
                    candidate =>
                        ToRow(
                            candidate,
                            heldSelection
                            && selectedCandidate is not null
                            && Key(candidate).Equals(
                                selectedKey,
                                StringComparison.Ordinal)))
                .ToArray();

        RoutesGrid.ItemsSource = rows;

        TradeRow? selection =
            selectedKey is null
                ? rows.FirstOrDefault()
                : rows.FirstOrDefault(
                    row =>
                        row.Key.Equals(
                            selectedKey,
                            StringComparison.Ordinal))
                  ?? rows.FirstOrDefault();

        RoutesGrid.SelectedItem = selection;

        if (selection is not null)
        {
            selectedCandidate = selection.Candidate;
            ShowSelectedCandidate(selectedCandidate);
        }
    }

    private static TradeRow ToRow(
        TradeRouteCandidate candidate,
        bool held) =>
        new(
            candidate,
            Key(candidate),
            held ? "★" : string.Empty,
            candidate.Source.CommodityName,
            $"{candidate.Source.SystemName} / {candidate.Source.StationName}",
            $"{candidate.Target.SystemName} / {candidate.Target.StationName}",
            $"{candidate.ProfitPerTon:N0}",
            $"{candidate.ProfitPerTrip:N0}",
            $"{candidate.SourceToTargetDistanceLy:0.0} LY",
            $"{candidate.WorstDataAge.TotalHours:0.#} h");

    private void RoutesGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (RoutesGrid.SelectedItem is TradeRow row)
        {
            selectedCandidate = row.Candidate;
            ShowSelectedCandidate(selectedCandidate);
        }
    }

    private void SortComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        CaptureSession();

        if (currentCandidates.Count > 0)
        {
            ApplyCandidates(currentCandidates);
        }
    }

    private void ShowSelectedCandidate(
        TradeRouteCandidate? candidate)
    {
        PinRouteButton.IsEnabled = candidate is not null;

        if (candidate is null)
        {
            SelectedCommodityText.Text =
                Loc.Get("Loc_TRADE_SELECT_ROUTE");

            SelectedProfitText.Text = string.Empty;
            SelectedSourceText.Text = string.Empty;
            SelectedSourceMetaText.Text = string.Empty;
            SelectedTargetText.Text = string.Empty;
            SelectedTargetMetaText.Text = string.Empty;
            SelectedRouteEconomicsText.Text = string.Empty;
            return;
        }

        SelectedCommodityText.Text =
            candidate.Source.CommodityName.ToUpperInvariant();

        SelectedProfitText.Text =
            Loc.Format(
                "Loc_TRADE_DETAIL_PROFIT",
                candidate.ProfitPerTrip,
                candidate.ProfitPerTon);

        SelectedSourceText.Text =
            $"{candidate.Source.SystemName}"
            + Environment.NewLine
            + candidate.Source.StationName;

        SelectedSourceMetaText.Text =
            BuildStationMeta(
                candidate.Source,
                candidate.SourceAge);

        SelectedTargetText.Text =
            $"{candidate.Target.SystemName}"
            + Environment.NewLine
            + candidate.Target.StationName;

        SelectedTargetMetaText.Text =
            BuildStationMeta(
                candidate.Target,
                candidate.TargetAge);

        string demand =
            candidate.Target.HasInfiniteDemand
                ? "∞"
                : candidate.Target.Demand.ToString("N0");

        SelectedRouteEconomicsText.Text =
            Loc.Format(
                "Loc_TRADE_DETAIL_ECONOMICS",
                candidate.Source.BuyFromStationPrice,
                candidate.Target.SellToStationPrice,
                candidate.Source.Stock,
                demand,
                candidate.TradableAmount,
                candidate.SourceToTargetDistanceLy);
    }

    private static string BuildStationMeta(
        TradeMarketOrder order,
        TimeSpan age)
    {
        string arrival =
            order.DistanceToArrivalLs is { } distance
                ? $"{distance:N0} ls"
                : "—";

        string pad =
            order.MaxLandingPadSize switch
            {
                3 => "L",
                2 => "M",
                1 => "S",
                _ => "?"
            };

        return
            $"{order.StationType}  •  {arrival}  •  pad {pad}"
            + $"  •  {age.TotalHours:0.#} h";
    }

    private void PinRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedCandidate is null)
        {
            return;
        }

        PinRequested?.Invoke(selectedCandidate);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke();

    private void TradeDragHandle_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Window? window =
            Window.GetWindow(this);

        if (window is null)
        {
            return;
        }

        try
        {
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool TryBuildConstraints(
        out TradeSearchConstraints constraints,
        out string error)
    {
        constraints = null!;
        error = string.Empty;

        string anchor =
            AnchorSystemTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(anchor))
        {
            error =
                Loc.Get("Loc_TRADE_VALIDATION_SYSTEM");
            return false;
        }

        if (!int.TryParse(
                CargoTextBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int cargo)
            || cargo < 1)
        {
            error =
                Loc.Get("Loc_TRADE_VALIDATION_CARGO");
            return false;
        }

        long address =
            currentJournal.SystemAddress != 0
            && currentJournal.StarSystem.Equals(
                anchor,
                StringComparison.OrdinalIgnoreCase)
                ? currentJournal.SystemAddress
                : 0;

        int stationDistance =
            SelectedInt(
                MaxStationDistanceComboBox,
                0);

        constraints =
            new TradeSearchConstraints
            {
                OriginSystemName = anchor,
                OriginSystemAddress = address,
                CargoCapacity = cargo,
                SourceSearchRadiusLy =
                    SelectedInt(SourceRadiusComboBox, 30),
                TargetSearchRadiusLy =
                    SelectedInt(TargetRadiusComboBox, 80),
                MaxDataAge =
                    TimeSpan.FromHours(
                        SelectedInt(MaxAgeComboBox, 72)),
                MinLandingPadSize =
                    SelectedInt(MinPadComboBox, 3),
                MaxStationDistanceLs =
                    stationDistance <= 0
                        ? null
                        : stationDistance,
                IncludeFleetCarriers =
                    FleetCarriersCheckBox.IsChecked == true,
                MinSupply =
                    SelectedLong(MinSupplyComboBox, 1),
                MinDemand =
                    SelectedLong(MinDemandComboBox, 1),
                MaxCommodityCandidates = 50,
                MaxResults = 50,
                MaxConcurrentCommoditySearches = 6
            };

        try
        {
            constraints.Validate();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            constraints = null!;
            return false;
        }
    }

    private void SetSearchRunning(bool running)
    {
        AnchorSystemTextBox.IsEnabled = !running;
        CargoTextBox.IsEnabled = !running;
        SourceRadiusComboBox.IsEnabled = !running;
        TargetRadiusComboBox.IsEnabled = !running;
        MaxAgeComboBox.IsEnabled = !running;
        FleetCarriersCheckBox.IsEnabled = !running;
        SyncJournalButton.IsEnabled = !running;
        AdvancedFiltersExpander.IsEnabled = !running;
        SortComboBox.IsEnabled = true;

        SearchButton.SetResourceReference(
            ContentControl.ContentProperty,
            running
                ? "Loc_TRADE_CANCEL"
                : "Loc_SEARCH_ROUTES");
    }

    private void CaptureSession()
    {
        if (applyingJournal
            || SourceRadiusComboBox is null
            || TargetRadiusComboBox is null)
        {
            return;
        }

        Session.HasValues = true;
        Session.Anchor = AnchorSystemTextBox.Text.Trim();
        Session.Cargo = CargoTextBox.Text.Trim();
        Session.SourceRadius = SelectedInt(SourceRadiusComboBox, 30);
        Session.TargetRadius = SelectedInt(TargetRadiusComboBox, 80);
        Session.MaxAgeHours = SelectedInt(MaxAgeComboBox, 72);
        Session.IncludeCarriers = FleetCarriersCheckBox.IsChecked == true;
        Session.MinPad = SelectedInt(MinPadComboBox, 3);
        Session.MaxStationDistance = SelectedInt(MaxStationDistanceComboBox, 0);
        Session.MinSupply = (int)SelectedLong(MinSupplyComboBox, 1);
        Session.MinDemand = (int)SelectedLong(MinDemandComboBox, 1);
        Session.AdvancedOpen = AdvancedFiltersExpander.IsExpanded;
        Session.Sort = SortTag();
    }

    private void ApplySession()
    {
        applyingJournal = true;
        try
        {
            AnchorSystemTextBox.Text = Session.Anchor;
            CargoTextBox.Text = Session.Cargo;
            SelectTag(SourceRadiusComboBox, Session.SourceRadius);
            SelectTag(TargetRadiusComboBox, Session.TargetRadius);
            SelectTag(MaxAgeComboBox, Session.MaxAgeHours);
            FleetCarriersCheckBox.IsChecked = Session.IncludeCarriers;
            SelectTag(MinPadComboBox, Session.MinPad);
            SelectTag(MaxStationDistanceComboBox, Session.MaxStationDistance);
            SelectTag(MinSupplyComboBox, Session.MinSupply);
            SelectTag(MinDemandComboBox, Session.MinDemand);
            AdvancedFiltersExpander.IsExpanded = Session.AdvancedOpen;
            SelectTag(SortComboBox, Session.Sort);
        }
        finally
        {
            applyingJournal = false;
        }

        systemOverridden =
            !string.IsNullOrWhiteSpace(Session.Anchor);

        cargoOverridden =
            !string.IsNullOrWhiteSpace(Session.Cargo);
    }

    private void RefreshFooter()
    {
        if (currentCandidates.Count == 0)
        {
            FooterText.Text =
                Loc.Get("Loc_TRADE_WORKSPACE_IDLE_FOOTER");
            return;
        }

        long best =
            currentCandidates.Max(
                item => item.ProfitPerTrip);

        FooterText.Text =
            Loc.Format(
                "Loc_TRADE_RESULTS_SUMMARY_LONG",
                currentCandidates.Count,
                best);
    }

    private string SortTag() =>
        (SortComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
        ?? "profit";

    private static int SelectedInt(
        ComboBox comboBox,
        int fallback) =>
        int.TryParse(
            (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
            ? value
            : fallback;

    private static long SelectedLong(
        ComboBox comboBox,
        long fallback) =>
        long.TryParse(
            (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long value)
            ? value
            : fallback;

    private static void SelectTag(
        ComboBox comboBox,
        object value)
    {
        string expected =
            Convert.ToString(
                value,
                CultureInfo.InvariantCulture)
            ?? string.Empty;

        for (int index = 0;
             index < comboBox.Items.Count;
             index++)
        {
            if (comboBox.Items[index] is ComboBoxItem item
                && string.Equals(
                    item.Tag?.ToString(),
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }
    }

    private static string Key(
        TradeRouteCandidate candidate) =>
        $"{candidate.Source.MarketId}:"
        + $"{candidate.Target.MarketId}:"
        + candidate.Source.CommodityName.ToLowerInvariant();
}
