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
        string HeldLabel,
        string Confidence,
        string ConfidenceLevel,
        string Commodity,
        string Source,
        string Target,
        string ProfitPerTon,
        string ProfitPerTrip,
        string TradeLegDistance,
        string TravelTime,
        string CreditsPerHour,
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
        public string RouteMode { get; set; } = "oneway";
    }

    private const int PageSize = 10;
    private const int SearchResultPoolSize = 100;

    private static readonly SessionState Session = new();

    private readonly TradeSearchService searchService = new();
    private readonly TradeTravelTimeEstimator travelTimeEstimator = new();
    private CancellationTokenSource? searchCancellation;
    private List<TradeRouteCandidate> currentCandidates = new();
    private int currentPage;
    private TradeRouteCandidate? selectedCandidate;
    private GameStateSnapshot currentJournal = new();
    private bool applyingJournal;
    private bool systemOverridden;
    private bool cargoOverridden;
    private bool advancedFiltersOpen;
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
            RouteModeComboBox.SelectedIndex = 0;
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
                MarkSearchInputsDirty();
            }
        };

        CargoTextBox.TextChanged += (_, _) =>
        {
            if (!applyingJournal)
            {
                cargoOverridden = true;
                MarkSearchInputsDirty();
            }
        };

        SourceRadiusComboBox.SelectionChanged += TradeFilter_SelectionChanged;
        TargetRadiusComboBox.SelectionChanged += TradeFilter_SelectionChanged;
        MaxAgeComboBox.SelectionChanged += TradeFilter_SelectionChanged;
        MinPadComboBox.SelectionChanged += TradeFilter_SelectionChanged;
        MaxStationDistanceComboBox.SelectionChanged += TradeFilter_SelectionChanged;
        MinSupplyComboBox.SelectionChanged += TradeFilter_SelectionChanged;
        MinDemandComboBox.SelectionChanged += TradeFilter_SelectionChanged;
        FleetCarriersCheckBox.Checked += TradeFilter_CheckChanged;
        FleetCarriersCheckBox.Unchecked += TradeFilter_CheckChanged;

        if (Session.HasValues)
        {
            ApplySession();
        }

        SetFullMode(
            false,
            raiseEvent: false);

        UpdateJournalState(
            JournalMonitorService.Instance.Current);

        UpdateAdvancedFiltersUi();
        UpdateRouteModeUi();
        UpdatePaginationUi();
        RestoreResultSnapshot();
        RefreshFooter();
        RefreshCompactPresentation();
    }

    public bool IsFullMode { get; private set; }

    public event Action? CloseRequested;
    public event Action? DragRequested;
    public event Action<bool>? ViewModeChanged;
    public event Action<TradeRouteCandidate>? PinRequested;

    public void UpdateJournalState(
        GameStateSnapshot state)
    {
        currentJournal = state;
        RefreshTravelProfileIfChanged(
            state);

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

        string balance =
            state.JournalAvailable
                ? $"  •  {Math.Max(0, state.Balance):N0} CR"
                : string.Empty;

        string journalLine =
            $"{location}  •  "
            + $"{(string.IsNullOrWhiteSpace(ship) ? Loc.Get("Loc_ship_unknown") : ship)}"
            + $"  •  {cargo}"
            + balance;
        JournalContextText.Text =
            journalLine;

        CompactJournalContextText.Text =
            journalLine;

        applyingJournal = true;
        try
        {
            if (!systemOverridden
                && !Session.HasValues
                && !string.IsNullOrWhiteSpace(state.StarSystem))
            {
                AnchorSystemTextBox.Text =
                    state.StarSystem;
            }

            if (!cargoOverridden
                && !Session.HasValues
                && state.CargoCapacity > 0)
            {
                CargoTextBox.Text =
                    state.FreeCargo.ToString(
                        CultureInfo.CurrentCulture);
            }
        }
        finally
        {
            applyingJournal = false;
        }

        RefreshActiveTradeState(
            state);
        RefreshCompactPresentation();
    }

    public void RefreshLocalization()
    {
        UpdateJournalState(
            currentJournal);

        if (IsCargoSaleMode)
        {
            ShowSelectedCargoSaleCandidate(
                selectedCargoSaleCandidate);
        }
        else
        {
            ShowSelectedCandidate(
                selectedCandidate);
        }

        UpdateAdvancedFiltersUi();
        UpdateRouteModeUi();
        RefreshFooter();
        RefreshCompactPresentation();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        CaptureSession();
        CaptureResultSnapshot();
        DisposeContinuousPlanning();
        DetachExecutionTracker();

        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = null;
    }

    private void OpenFullButton_Click(
        object sender,
        RoutedEventArgs e) =>
        SetFullMode(
            true);

    private void CollapseButton_Click(
        object sender,
        RoutedEventArgs e) =>
        SetFullMode(
            false);

    private void SetFullMode(
        bool full,
        bool raiseEvent = true)
    {
        if (IsFullMode == full
            && CompactTradePanel.Visibility
               == (full
                    ? Visibility.Collapsed
                    : Visibility.Visible))
        {
            return;
        }

        IsFullMode =
            full;

        CompactTradePanel.Visibility =
            full
                ? Visibility.Collapsed
                : Visibility.Visible;

        FullTradePanel.Visibility =
            full
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (full)
        {
            UpdateAdvancedFiltersUi();
        }
        else
        {
            RefreshCompactPresentation();
        }

        if (raiseEvent)
        {
            ViewModeChanged?.Invoke(
                full);
        }
    }

    private void AdvancedFiltersButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        advancedFiltersOpen =
            !advancedFiltersOpen;

        ApplyAdvancedFiltersVisibility();
        CaptureSession();
    }

    private void ApplyAdvancedFiltersVisibility()
    {
        AdvancedFiltersPanel.Visibility =
            advancedFiltersOpen
                ? Visibility.Visible
                : Visibility.Collapsed;

        AdvancedFiltersArrowText.Text =
            advancedFiltersOpen
                ? "▲"
                : "▼";

        UpdateAdvancedFiltersUi();
    }

    private void UpdateAdvancedFiltersUi()
    {
        int active =
            CountActiveAdvancedFilters();

        AdvancedFiltersButtonText.Text =
            Loc.Format(
                "Loc_TRADE_FILTERS_FORMAT",
                active);
    }
    private int CountActiveAdvancedFilters()
    {
        int count = 0;

        if (SelectedInt(
                MinPadComboBox,
                1) > 1)
        {
            count++;
        }

        if (SelectedInt(
                MaxStationDistanceComboBox,
                0) > 0)
        {
            count++;
        }

        if (SelectedLong(
                MinSupplyComboBox,
                1) > 1)
        {
            count++;
        }

        if (SelectedLong(
                MinDemandComboBox,
                1) > 1)
        {
            count++;
        }

        if (FleetCarriersCheckBox.IsChecked == true)
        {
            count++;
        }

        return
            count;
    }
    private void TradeFilter_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (applyingJournal)
        {
            return;
        }

        UpdateAdvancedFiltersUi();
        MarkSearchInputsDirty();
    }

    private void TradeFilter_CheckChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (applyingJournal)
        {
            return;
        }

        MarkSearchInputsDirty();
    }

    private void SyncJournalButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        systemOverridden = false;
        cargoOverridden = false;
        Session.HasValues = false;

        applyingJournal = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(
                    currentJournal.StarSystem))
            {
                AnchorSystemTextBox.Text =
                    currentJournal.StarSystem;
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
        RefreshCompactPresentation();
    }

    private async void SearchButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await StartOrCancelSearchAsync();


    private async Task StartOrCancelSearchAsync()
    {
        if (searchCancellation is not null)
        {
            searchCancellation.Cancel();

            string cancelling =
                Loc.Get(
                    "Loc_TRADE_SEARCH_CANCELLING");

            SearchStatusText.Text =
                cancelling;

            CompactStatusText.Text =
                cancelling;

            return;
        }

        if (!TryBuildConstraints(
                out TradeSearchConstraints constraints,
                out string error))
        {
            SearchStatusText.Text =
                error;

            CompactStatusText.Text =
                error;

            return;
        }

        CaptureSession();
        RememberSearchConstraints(
            constraints);

        var cancellation =
            new CancellationTokenSource();

        searchCancellation =
            cancellation;

        currentCandidates =
            new List<TradeRouteCandidate>();

        roundTripByOutboundKey.Clear();
        ResetCargoSaleResults();
        ResetContinuousResults();
        ClearResultSnapshot();

        currentPage =
            0;

        selectedCandidate =
            null;

        RoutesList.ItemsSource =
            null;

        UpdatePaginationUi();

        ShowSelectedCandidate(
            null);

        SetSearchRunning(
            true);

        RefreshCompactPresentation();

        try
        {
            await RunSelectedSearchModeAsync(
                constraints,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            string cancelled =
                Loc.Format(
                    "Loc_TRADE_SEARCH_CANCELLED",
                    currentCandidates.Count);

            SearchStatusText.Text =
                cancelled;

            CompactStatusText.Text =
                cancelled;
        }
        catch (ArdentApiException ex)
        {
            string message =
                Loc.Format(
                    "Loc_TRADE_SEARCH_ERROR",
                    $"{(int)ex.StatusCode} {ex.StatusCode}");

            SearchStatusText.Text =
                message;

            CompactStatusText.Text =
                message;

            Logger.Logger.Error(
                $"Unified Trade workspace Ardent search failed: {ex}");
        }
        catch (Exception ex)
        {
            string message =
                Loc.Format(
                    "Loc_TRADE_SEARCH_ERROR",
                    ex.Message);

            SearchStatusText.Text =
                message;

            CompactStatusText.Text =
                message;

            Logger.Logger.Error(
                $"Unified Trade workspace search failed: {ex}");
        }
        finally
        {
            if (ReferenceEquals(
                    searchCancellation,
                    cancellation))
            {
                searchCancellation =
                    null;
            }

            cancellation.Dispose();

            SetSearchRunning(
                false);

            RefreshFooter();
            RefreshCompactPresentation();
        }
    }

    private void ApplyProgress(
        TradeSearchProgress progress)
    {
        string status;

        switch (progress.Stage)
        {
            case TradeSearchStage.ResolvingOrigin:
                status =
                    Loc.Get(
                        "Loc_TRADE_SEARCH_RESOLVING");

                SearchStatusText.Text =
                    status;

                CompactStatusText.Text =
                    status;

                return;

            case TradeSearchStage.LoadingCommodityReports:
                status =
                    Loc.Get(
                        "Loc_TRADE_SEARCH_LOADING_MARKET");

                SearchStatusText.Text =
                    status;

                CompactStatusText.Text =
                    status;

                return;

            case TradeSearchStage.Searching:
                status =
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

                SearchStatusText.Text =
                    status;

                CompactStatusText.Text =
                    status;

                if (progress.BestCandidates.Count > 0)
                {
                    ApplyCandidates(
                        progress.BestCandidates);
                }

                FooterText.Text =
                    Loc.Format(
                        "Loc_TRADE_WORKSPACE_PROGRESS_FOOTER",
                        progress.CompletedCommodities,
                        progress.TotalCommodities,
                        progress.Elapsed.TotalSeconds);

                CompactFooterText.Text =
                    Loc.Format(
                        "Loc_TRADE_COMPACT_PROGRESS",
                        progress.CompletedCommodities,
                        progress.TotalCommodities,
                        progress.Elapsed.TotalSeconds);

                RefreshCompactPresentation(
                    preserveStatus: true);

                return;

            case TradeSearchStage.Completed:
                if (progress.BestCandidates.Count > 0)
                {
                    ApplyCandidates(
                        progress.BestCandidates);
                }

                status =
                    Loc.Format(
                        "Loc_TRADE_SEARCH_DONE",
                        currentCandidates.Count,
                        progress.Elapsed.TotalSeconds);

                SearchStatusText.Text =
                    status;

                CompactStatusText.Text =
                    status;

                RefreshFooter();
                RefreshCompactPresentation(
                    preserveStatus: true);

                return;
        }
    }

    private void ApplyCandidates(
        IReadOnlyList<TradeRouteCandidate> candidates)
    {
        string? selectedKey =
            selectedCandidate is null
                ? null
                : Key(
                    selectedCandidate);

        currentCandidates =
            candidates
                .Take(
                    SearchResultPoolSize)
                .ToList();

        if (selectedKey is not null)
        {
            TradeRouteCandidate? updatedSelection =
                currentCandidates.FirstOrDefault(
                    candidate =>
                        Key(candidate).Equals(
                            selectedKey,
                            StringComparison.Ordinal));

            if (updatedSelection is not null)
            {
                selectedCandidate =
                    updatedSelection;
            }
        }

        RefreshCurrentPage(
            selectFirstWhenEmpty: false);

        CaptureResultSnapshot(
            freshResults: true);

        RefreshCompactPresentation();
    }

    private IEnumerable<TradeRouteCandidate> SortedCandidates() =>
        SortTag() switch
        {
            "perhour" =>
                currentCandidates
                    .OrderByDescending(
                        item =>
                            EstimatedProfitPerHour(
                                item))
                    .ThenByDescending(
                        item =>
                            item.ProfitPerTrip)
                    .ThenBy(
                        item =>
                            item.SourceToTargetDistanceLy),

            "time" =>
                currentCandidates
                    .OrderBy(
                        item =>
                            EstimatedTravelSeconds(
                                item))
                    .ThenByDescending(
                        item =>
                            item.ProfitPerTrip),

            "confidence" =>
                currentCandidates
                    .OrderByDescending(
                        item =>
                            ConfidenceScore(
                                item))
                    .ThenByDescending(
                        item =>
                            item.ProfitPerTrip)
                    .ThenBy(
                        item =>
                            item.WorstDataAge),

            "freshness" =>
                currentCandidates
                    .OrderBy(
                        item =>
                            item.WorstDataAge)
                    .ThenByDescending(
                        item =>
                            item.ProfitPerTrip),

            "perton" =>
                currentCandidates
                    .OrderByDescending(
                        item =>
                            item.ProfitPerTon)
                    .ThenByDescending(
                        item =>
                            item.ProfitPerTrip)
                    .ThenBy(
                        item =>
                            item.SourceToTargetDistanceLy),

            "distance" =>
                currentCandidates
                    .OrderBy(
                        item =>
                            item.SourceToTargetDistanceLy)
                    .ThenByDescending(
                        item =>
                            item.ProfitPerTrip),

            _ =>
                currentCandidates
                    .OrderByDescending(
                        item =>
                            item.ProfitPerTrip)
                    .ThenByDescending(
                        item =>
                            ConfidenceScore(
                                item))
                    .ThenByDescending(
                        item =>
                            item.ProfitPerTon)
                    .ThenBy(
                        item =>
                            item.SourceToTargetDistanceLy)
        };

    private void RefreshCurrentPage(
        bool selectFirstWhenEmpty = false)
    {
        if (IsCargoSaleMode)
        {
            RefreshCargoSalePage(
                selectFirstWhenEmpty);
            return;
        }

        List<TradeRouteCandidate> sorted =
            (IsContinuousMode
                ? SortedContinuousCandidates()
                : SortedCandidates())
            .ToList();

        int pageCount =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    sorted.Count
                    / (double)PageSize));

        currentPage =
            Math.Clamp(
                currentPage,
                0,
                pageCount - 1);

        int naturalStart =
            currentPage
            * PageSize;

        List<TradeRouteCandidate> page =
            sorted
                .Skip(
                    naturalStart)
                .Take(
                    PageSize)
                .ToList();

        string? selectedKey =
            selectedCandidate is null
                ? null
                : Key(
                    selectedCandidate);

        bool heldSelection =
            selectedCandidate is not null
            && page.All(
                candidate =>
                    !Key(candidate).Equals(
                        selectedKey,
                        StringComparison.Ordinal));

        if (heldSelection
            && selectedCandidate is not null)
        {
            if (page.Count >= PageSize)
            {
                page.RemoveAt(
                    page.Count - 1);
            }

            page.Insert(
                0,
                selectedCandidate);
        }

        TradeRow[] rows =
            page
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

        RoutesList.ItemsSource =
            rows;

        TradeRow? selection =
            selectedKey is null
                ? null
                : rows.FirstOrDefault(
                    row =>
                        row.Key.Equals(
                            selectedKey,
                            StringComparison.Ordinal));

        if (selection is null
            && selectFirstWhenEmpty)
        {
            selection =
                rows.FirstOrDefault();
        }

        RoutesList.SelectedItem =
            selection;

        if (selection is not null)
        {
            selectedCandidate =
                selection.Candidate;

            ShowSelectedCandidate(
                selectedCandidate);
        }
        else
        {
            selectedCandidate =
                null;

            ShowSelectedCandidate(
                null);
        }

        int firstRank =
            sorted.Count == 0
                ? 0
                : naturalStart + 1;

        int lastRank =
            Math.Min(
                naturalStart + PageSize,
                sorted.Count);

        RoutesSummaryText.Text =
            Loc.Format(
                "Loc_TRADE_RESULTS_PAGING_SUMMARY",
                sorted.Count,
                firstRank,
                lastRank);

        PageIndicatorText.Text =
            $"{currentPage + 1} / {pageCount}";

        PreviousPageButton.IsEnabled =
            currentPage > 0;

        NextPageButton.IsEnabled =
            currentPage + 1 < pageCount;
    }

    private void UpdatePaginationUi()
    {
        if (IsCargoSaleMode)
        {
            UpdateCargoSalePaginationUi();
            return;
        }

        int pageCount =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    currentCandidates.Count
                    / (double)PageSize));

        PageIndicatorText.Text =
            $"{Math.Min(currentPage + 1, pageCount)} / {pageCount}";

        RoutesSummaryText.Text =
            Loc.Format(
                "Loc_TRADE_RESULTS_PAGING_SUMMARY",
                currentCandidates.Count,
                0,
                0);

        PreviousPageButton.IsEnabled =
            false;

        NextPageButton.IsEnabled =
            false;
    }

    private TradeRow ToRow(
        TradeRouteCandidate candidate,
        bool held)
    {
        if (TryBuildContinuousRow(
                candidate,
                held,
                out TradeRow continuousRow))
        {
            return
                continuousRow;
        }

        if (TryBuildRoundTripRow(
                candidate,
                held,
                out TradeRow roundTripRow))
        {
            return
                roundTripRow;
        }

        TradeRouteConfidence confidence =
            ConfidenceFor(
                candidate);

        return
            new TradeRow(
                candidate,
                Key(
                    candidate),
                held
                    ? Loc.Get(
                        "Loc_TRADE_HELD_SELECTION")
                    : string.Empty,
                ConfidenceBadge(
                    confidence),
                confidence.Level.ToString(),
                candidate.Source.CommodityName.ToUpperInvariant(),
                $"{candidate.Source.SystemName} / {candidate.Source.StationName}",
                $"→ {candidate.Target.SystemName} / {candidate.Target.StationName}",
                Loc.Format(
                    "Loc_TRADE_ROW_PROFIT_T_FORMAT",
                    candidate.ProfitPerTon),
                Loc.Format(
                    "Loc_TRADE_ROW_PROFIT_TRIP_FORMAT",
                    candidate.ProfitPerTrip),
                Loc.Format(
                    "Loc_TRADE_ROW_DISTANCE_FORMAT",
                    candidate.SourceToTargetDistanceLy),
                FormatEstimatedTravelTime(
                    candidate),
                FormatCreditsPerHour(
                    EstimatedProfitPerHour(
                        candidate)),
                Loc.Format(
                    "Loc_TRADE_ROW_AGE_FORMAT",
                    candidate.WorstDataAge.TotalHours));
    }

    private void RoutesList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (RoutesList.SelectedItem
            is CargoSaleRow cargoRow)
        {
            selectedCandidate =
                null;
            selectedCargoSaleCandidate =
                cargoRow.Candidate;

            ShowSelectedCargoSaleCandidate(
                cargoRow.Candidate);

            CaptureResultSnapshot();
            return;
        }

        if (RoutesList.SelectedItem
            is TradeRow row)
        {
            selectedCargoSaleCandidate =
                null;
            selectedCandidate =
                row.Candidate;

            ShowSelectedCandidate(
                selectedCandidate);

            CaptureResultSnapshot();
        }
    }
    private void PreviousPageButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (currentPage <= 0)
        {
            return;
        }

        currentPage--;
        selectedCandidate =
            null;
        selectedCargoSaleCandidate =
            null;

        RefreshCurrentPage(
            selectFirstWhenEmpty: false);

        CaptureResultSnapshot();
    }
    private void NextPageButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        int pageCount =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    ActiveResultCount
                    / (double)PageSize));

        if (currentPage + 1 >= pageCount)
        {
            return;
        }

        currentPage++;
        selectedCandidate =
            null;
        selectedCargoSaleCandidate =
            null;

        RefreshCurrentPage(
            selectFirstWhenEmpty: false);

        CaptureResultSnapshot();
    }
    private void SortComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (applyingJournal)
        {
            return;
        }

        CaptureSession();

        if (ActiveResultCount > 0)
        {
            currentPage =
                0;

            RefreshCurrentPage(
                selectFirstWhenEmpty: false);

            CaptureResultSnapshot();
        }
    }
    private void ShowSelectedCandidate(
        TradeRouteCandidate? candidate)
    {
        PinRouteButton.IsEnabled =
            candidate is not null;

        if (candidate is null)
        {
            SelectedCommodityText.Text =
                Loc.Get(
                    "Loc_TRADE_SELECT_ROUTE");

            SelectedProfitText.Text =
                string.Empty;

            SelectedSourceText.Text =
                string.Empty;

            SelectedSourceMetaText.Text =
                string.Empty;

            SelectedTargetText.Text =
                string.Empty;

            SelectedTargetMetaText.Text =
                string.Empty;

            SelectedRouteEconomicsText.Text =
                string.Empty;

            SelectedTravelEstimateText.Text =
                string.Empty;

            ClearConfidence();

            return;
        }

        if (TryShowContinuousCandidate(
                candidate))
        {
            return;
        }

        if (TryShowRoundTripCandidate(
                candidate))
        {
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
                : candidate.Target.Demand.ToString(
                    "N0");

        SelectedRouteEconomicsText.Text =
            Loc.Format(
                "Loc_TRADE_DETAIL_ECONOMICS",
                candidate.Source.BuyFromStationPrice,
                candidate.Target.SellToStationPrice,
                candidate.Source.Stock,
                demand,
                candidate.TradableAmount,
                candidate.SourceToTargetDistanceLy);

        SelectedTravelEstimateText.Text =
            FormatTravelDetail(
                candidate);

        ShowConfidence(
            candidate);
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

    private void PinRouteButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (selectedCandidate is null)
        {
            return;
        }

        PinSelectedCandidate();
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e) =>
        CloseRequested?.Invoke();

    private void CompactTradeDragHandle_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton
            == MouseButtonState.Pressed)
        {
            DragRequested?.Invoke();
        }
    }

    private bool TryBuildConstraints(
        out TradeSearchConstraints constraints,
        out string error)
    {
        if (IsContinuousMode)
        {
            return TryBuildContinuousConstraints(
                out constraints,
                out error);
        }

        if (IsCargoSaleMode)
        {
            return TryBuildCargoSaleConstraints(
                out constraints,
                out error);
        }

        constraints =
            null!;

        error =
            string.Empty;

        string anchor =
            AnchorSystemTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                anchor))
        {
            error =
                Loc.Get(
                    "Loc_TRADE_VALIDATION_SYSTEM");

            return
                false;
        }

        if (!int.TryParse(
                CargoTextBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int cargo)
            || cargo < 1)
        {
            error =
                Loc.Get(
                    "Loc_TRADE_VALIDATION_CARGO");

            return
                false;
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
                OriginSystemName =
                    anchor,
                OriginSystemAddress =
                    address,
                CargoCapacity =
                    cargo,
                AvailableCredits =
                    currentJournal.JournalAvailable
                        ? Math.Max(
                            0,
                            currentJournal.Balance)
                        : null,
                DiversifyCandidatePool =
                    true,
                SourceSearchRadiusLy =
                    SelectedInt(
                        SourceRadiusComboBox,
                        30),
                TargetSearchRadiusLy =
                    SelectedInt(
                        TargetRadiusComboBox,
                        80),
                MaxDataAge =
                    TimeSpan.FromHours(
                        SelectedInt(
                            MaxAgeComboBox,
                            72)),
                MinLandingPadSize =
                    SelectedInt(
                        MinPadComboBox,
                        3),
                MaxStationDistanceLs =
                    stationDistance <= 0
                        ? null
                        : stationDistance,
                IncludeFleetCarriers =
                    FleetCarriersCheckBox.IsChecked
                    == true,
                MinSupply =
                    SelectedLong(
                        MinSupplyComboBox,
                        1),
                MinDemand =
                    SelectedLong(
                        MinDemandComboBox,
                        1),
                MaxCommodityCandidates =
                    50,
                MaxResults =
                    SearchResultPoolSize,
                MaxConcurrentCommoditySearches =
                    6
            };

        try
        {
            constraints.Validate();

            return
                true;
        }
        catch (Exception ex)
        {
            error =
                ex.Message;

            constraints =
                null!;

            return
                false;
        }
    }

    private void SetSearchRunning(
        bool running)
    {
        AnchorSystemTextBox.IsEnabled =
            !running;

        CargoTextBox.IsEnabled =
            !running;

        SourceRadiusComboBox.IsEnabled =
            !running;

        TargetRadiusComboBox.IsEnabled =
            !running;

        MaxAgeComboBox.IsEnabled =
            !running;

        FleetCarriersCheckBox.IsEnabled =
            !running;

        RouteModeComboBox.IsEnabled =
            !running;

        SyncJournalButton.IsEnabled =
            !running;

        AdvancedFiltersButton.IsEnabled =
            !running;

        AdvancedFiltersPanel.IsEnabled =
            !running;

        SortComboBox.IsEnabled =
            true;

        ApplyCargoSaleControlAvailability(
            running);
        ApplyContinuousControlAvailability(
            running);

        SearchButton.SetResourceReference(
            ContentControl.ContentProperty,
            running
                ? "Loc_TRADE_CANCEL"
                : SearchIdleResourceKey());

        CompactActionButton.SetResourceReference(
            ContentControl.ContentProperty,
            running
                ? "Loc_TRADE_CANCEL"
                : SearchIdleResourceKey());

        UpdateCompactModeButtons();
    }

    private void CaptureSession()
    {
        if (applyingJournal
            || SourceRadiusComboBox is null
            || TargetRadiusComboBox is null)
        {
            return;
        }

        Session.HasValues =
            true;

        Session.Anchor =
            AnchorSystemTextBox.Text.Trim();

        Session.Cargo =
            CargoTextBox.Text.Trim();

        Session.SourceRadius =
            SelectedInt(
                SourceRadiusComboBox,
                30);

        Session.TargetRadius =
            SelectedInt(
                TargetRadiusComboBox,
                80);

        Session.MaxAgeHours =
            SelectedInt(
                MaxAgeComboBox,
                72);

        Session.IncludeCarriers =
            FleetCarriersCheckBox.IsChecked
            == true;

        Session.MinPad =
            SelectedInt(
                MinPadComboBox,
                3);

        Session.MaxStationDistance =
            SelectedInt(
                MaxStationDistanceComboBox,
                0);

        Session.MinSupply =
            (int)SelectedLong(
                MinSupplyComboBox,
                1);

        Session.MinDemand =
            (int)SelectedLong(
                MinDemandComboBox,
                1);

        Session.AdvancedOpen =
            advancedFiltersOpen;

        Session.Sort =
            SortTag();

        Session.RouteMode =
            RouteModeTag();
    }

    private void ApplySession()
    {
        applyingJournal =
            true;

        try
        {
            AnchorSystemTextBox.Text =
                Session.Anchor;

            CargoTextBox.Text =
                Session.Cargo;

            SelectTag(
                SourceRadiusComboBox,
                Session.SourceRadius);

            SelectTag(
                TargetRadiusComboBox,
                Session.TargetRadius);

            SelectTag(
                MaxAgeComboBox,
                Session.MaxAgeHours);

            FleetCarriersCheckBox.IsChecked =
                Session.IncludeCarriers;

            SelectTag(
                MinPadComboBox,
                Session.MinPad);

            SelectTag(
                MaxStationDistanceComboBox,
                Session.MaxStationDistance);

            SelectTag(
                MinSupplyComboBox,
                Session.MinSupply);

            SelectTag(
                MinDemandComboBox,
                Session.MinDemand);

            SelectTag(
                SortComboBox,
                Session.Sort);

            SelectTag(
                RouteModeComboBox,
                Session.RouteMode);

            advancedFiltersOpen =
                Session.AdvancedOpen;

            ApplyAdvancedFiltersVisibility();
        }
        finally
        {
            applyingJournal =
                false;
        }

        systemOverridden =
            !string.IsNullOrWhiteSpace(
                Session.Anchor);

        cargoOverridden =
            !string.IsNullOrWhiteSpace(
                Session.Cargo);

        UpdateRouteModeUi();
    }

    private void RefreshFooter()
    {
        if (IsContinuousMode)
        {
            RefreshContinuousFooter();
            return;
        }

        if (IsCargoSaleMode)
        {
            RefreshCargoSaleFooter();
            return;
        }

        if (currentCandidates.Count == 0)
        {
            FooterText.Text =
                Loc.Get(
                    "Loc_TRADE_WORKSPACE_IDLE_FOOTER");

            return;
        }

        long best =
            currentCandidates.Max(
                item =>
                    item.ProfitPerTrip);

        FooterText.Text =
            Loc.Format(
                "Loc_TRADE_RESULTS_SUMMARY_LONG",
                currentCandidates.Count,
                best);
    }

    private void RefreshCompactPresentation(
        bool preserveStatus = false)
    {
        if (HasActiveTradeRoute)
        {
            RefreshActiveTradeCompact();
            return;
        }

        if (IsCargoSaleMode)
        {
            RefreshCargoSaleCompact(
                preserveStatus);
            return;
        }

        if (IsContinuousMode)
        {
            RefreshContinuousCompact(
                preserveStatus);
            return;
        }

        string anchor =
            string.IsNullOrWhiteSpace(
                AnchorSystemTextBox.Text)
                ? "—"
                : AnchorSystemTextBox.Text.Trim();

        CompactFiltersText.Text =
            Loc.Format(
                "Loc_TRADE_COMPACT_FILTERS_FORMAT",
                anchor,
                SelectedInt(
                    SourceRadiusComboBox,
                    30),
                SelectedInt(
                    TargetRadiusComboBox,
                    80),
                CountActiveAdvancedFilters());

        TradeRouteCandidate? best =
            currentCandidates
                .OrderByDescending(
                    item =>
                        item.ProfitPerTrip)
                .ThenByDescending(
                    item =>
                        item.ProfitPerTon)
                .ThenBy(
                    item =>
                        item.SourceToTargetDistanceLy)
                .FirstOrDefault();

        if (best is null)
        {
            CompactBestRouteText.Text =
                Loc.Get(
                    "Loc_TRADE_COMPACT_NO_RESULTS");

            if (!preserveStatus
                && searchCancellation is null)
            {
                CompactStatusText.Text =
                    Loc.Get(
                        "Loc_TRADE_COMPACT_READY");
            }

            CompactFooterText.Text =
                Loc.Get(
                    "Loc_TRADE_WORKSPACE_IDLE_FOOTER");

            return;
        }

        if (TryRenderRoundTripCompact(
                best,
                preserveStatus))
        {
            return;
        }

        CompactBestRouteText.Text =
            Loc.Format(
                "Loc_TRADE_COMPACT_BEST_FORMAT",
                best.Source.CommodityName.ToUpperInvariant(),
                best.Source.SystemName,
                best.Target.SystemName,
                best.ProfitPerTrip,
                best.SourceToTargetDistanceLy);

        if (!preserveStatus
            && searchCancellation is null)
        {
            CompactStatusText.Text =
                Loc.Format(
                    "Loc_TRADE_COMPACT_RESULTS_FORMAT",
                    currentCandidates.Count);
        }

        CompactFooterText.Text =
            Loc.Format(
                "Loc_TRADE_COMPACT_BEST_META",
                best.ProfitPerTon,
                best.TradableAmount,
                best.WorstDataAge.TotalHours)
            + Environment.NewLine
            + FormatCompactTravel(
                best);
    }

    private string SortTag() =>
        (SortComboBox.SelectedItem
            as ComboBoxItem)?.Tag?.ToString()
        ?? "profit";

    private static int SelectedInt(
        ComboBox comboBox,
        int fallback) =>
        int.TryParse(
            (comboBox.SelectedItem
                as ComboBoxItem)?.Tag?.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
                ? value
                : fallback;

    private static long SelectedLong(
        ComboBox comboBox,
        long fallback) =>
        long.TryParse(
            (comboBox.SelectedItem
                as ComboBoxItem)?.Tag?.ToString(),
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
            if (comboBox.Items[index]
                is ComboBoxItem item
                && string.Equals(
                    item.Tag?.ToString(),
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex =
                    index;

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