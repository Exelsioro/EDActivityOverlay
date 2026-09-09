using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl
{
    private sealed record CargoSaleRow(
        CargoSaleCandidate Candidate,
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

    private readonly CargoSaleSearchService cargoSaleSearchService =
        new();

    private List<CargoSaleCandidate> currentCargoSaleCandidates =
        new();

    private CargoSaleCandidate? selectedCargoSaleCandidate;

    public event Action<CargoSaleCandidate>? CargoSalePinRequested;

    public void ActivatePinnedCargoSale(
        TradeRouteProgressTracker? tracker)
    {
        AttachExecutionTracker(
            tracker);

        SetFullMode(
            false);

        RefreshCompactPresentation();
    }

    private bool IsCargoSaleMode =>
        string.Equals(
            RouteModeTag(),
            "cargo",
            StringComparison.OrdinalIgnoreCase);

    private async Task RunCargoSaleSearchAsync(
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken)
    {
        string searching =
            Loc.Format(
                "Loc_TRADE_CARGO_SEARCHING",
                currentJournal.CargoByCommodityId.Count,
                currentJournal.CargoByCommodityId.Values.Sum(item => item.Count));

        SearchStatusText.Text =
            searching;

        CompactStatusText.Text =
            searching;

        DateTimeOffset started =
            DateTimeOffset.UtcNow;

        IReadOnlyList<CargoSaleCandidate> candidates =
            await cargoSaleSearchService.SearchAsync(
                    currentJournal,
                    constraints,
                    cancellationToken)
                .ConfigureAwait(true);

        ApplyCargoSaleCandidates(
            candidates);

        double seconds =
            (DateTimeOffset.UtcNow - started)
            .TotalSeconds;

        string completed =
            candidates.Count == 0
                ? Loc.Get(
                    "Loc_TRADE_CARGO_NO_RESULTS")
                : Loc.Format(
                    "Loc_TRADE_CARGO_DONE",
                    candidates.Count,
                    seconds);

        SearchStatusText.Text =
            completed;

        CompactStatusText.Text =
            completed;

        RefreshCargoSaleFooter();
        RefreshCargoSaleCompact(
            preserveStatus: true);
    }

    private void ApplyCargoSaleCandidates(
        IReadOnlyList<CargoSaleCandidate> candidates)
    {
        currentCargoSaleCandidates =
            candidates
                .Take(
                    SearchResultPoolSize)
                .ToList();

        currentCandidates.Clear();
        roundTripByOutboundKey.Clear();
        currentPage =
            0;
        selectedCandidate =
            null;
        selectedCargoSaleCandidate =
            null;

        RefreshCargoSalePage(
            selectFirstWhenEmpty: false);

        CaptureResultSnapshot(
            freshResults: true);
    }

    private IEnumerable<CargoSaleCandidate> SortedCargoSaleCandidates() =>
        SortTag() switch
        {
            "perhour" =>
                currentCargoSaleCandidates
                    .OrderByDescending(
                        CargoSaleSortValuePerHour)
                    .ThenByDescending(item =>
                        item.TotalRevenue),

            "time" =>
                currentCargoSaleCandidates
                    .OrderBy(
                        CargoSaleTravelSeconds)
                    .ThenByDescending(item =>
                        item.TotalRevenue),

            "freshness" =>
                currentCargoSaleCandidates
                    .OrderBy(item =>
                        item.WorstDataAge)
                    .ThenByDescending(item =>
                        item.TotalRevenue),

            "perton" =>
                currentCargoSaleCandidates
                    .OrderByDescending(item =>
                        item.AverageValuePerTon)
                    .ThenByDescending(item =>
                        item.TotalRevenue),

            "distance" =>
                currentCargoSaleCandidates
                    .OrderBy(item =>
                        item.DistanceLy)
                    .ThenByDescending(item =>
                        item.TotalRevenue),

            _ =>
                currentCargoSaleCandidates
                    .OrderByDescending(item =>
                        item.TotalRevenue)
                    .ThenByDescending(item =>
                        item.CoverageRatio)
                    .ThenBy(item =>
                        item.DistanceLy)
        };

    private void RefreshCargoSalePage(
        bool selectFirstWhenEmpty)
    {
        List<CargoSaleCandidate> sorted =
            SortedCargoSaleCandidates()
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

        CargoSaleRow[] rows =
            sorted
                .Skip(
                    naturalStart)
                .Take(
                    PageSize)
                .Select(ToCargoSaleRow)
                .ToArray();

        RoutesList.ItemsSource =
            rows;

        string? selectedKey =
            selectedCargoSaleCandidate is null
                ? null
                : CargoSaleKey(
                    selectedCargoSaleCandidate);

        CargoSaleRow? selection =
            selectedKey is null
                ? null
                : rows.FirstOrDefault(row =>
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
            selectedCargoSaleCandidate =
                selection.Candidate;

            ShowSelectedCargoSaleCandidate(
                selection.Candidate);
        }
        else
        {
            ShowSelectedCargoSaleCandidate(
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

    private void UpdateCargoSalePaginationUi()
    {
        int pageCount =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    currentCargoSaleCandidates.Count
                    / (double)PageSize));

        PageIndicatorText.Text =
            $"{Math.Min(currentPage + 1, pageCount)} / {pageCount}";

        RoutesSummaryText.Text =
            Loc.Format(
                "Loc_TRADE_RESULTS_PAGING_SUMMARY",
                currentCargoSaleCandidates.Count,
                0,
                0);

        PreviousPageButton.IsEnabled =
            false;

        NextPageButton.IsEnabled =
            false;
    }

    private CargoSaleRow ToCargoSaleRow(
        CargoSaleCandidate candidate)
    {
        string commodity =
            candidate.Lines.Count == 1
                ? candidate.Lines[0].DisplayName.ToUpperInvariant()
                : Loc.Format(
                    "Loc_TRADE_CARGO_ROW_MIXED",
                    candidate.Lines.Count);

        string travel =
            candidate.IsCurrentMarket
                ? Loc.Get(
                    "Loc_TRADE_CARGO_NOW")
                : FormatTravelTime(
                    EstimateCargoSaleTravel(
                        candidate)
                    .TotalTime);

        string valueRate =
            candidate.IsCurrentMarket
                ? Loc.Get(
                    "Loc_TRADE_CARGO_LIVE_BADGE")
                : FormatCreditsPerHour(
                    CargoSaleRevenuePerHour(
                        candidate));

        return new CargoSaleRow(
            candidate,
            CargoSaleKey(
                candidate),
            Loc.Format(
                "Loc_TRADE_CARGO_ROW_COVERAGE",
                candidate.SellableUnits,
                candidate.TotalCargoUnits,
                candidate.CoverageRatio * 100),
            string.Empty,
            string.Empty,
            commodity,
            Loc.Format(
                "Loc_TRADE_CARGO_ROW_SOURCE",
                candidate.TotalCargoUnits),
            $"→ {candidate.Target.SystemName} / {candidate.Target.StationName}",
            Loc.Format(
                "Loc_TRADE_CARGO_ROW_PER_TON",
                candidate.AverageValuePerTon),
            Loc.Format(
                "Loc_TRADE_CARGO_ROW_VALUE",
                candidate.TotalRevenue),
            Loc.Format(
                "Loc_TRADE_ROW_DISTANCE_FORMAT",
                candidate.DistanceLy),
            travel,
            valueRate,
            Loc.Format(
                "Loc_TRADE_ROW_AGE_FORMAT",
                candidate.WorstDataAge.TotalHours));
    }

    private void ShowSelectedCargoSaleCandidate(
        CargoSaleCandidate? candidate)
    {
        PinRouteButton.IsEnabled =
            candidate is not null
            && !candidate.IsCurrentMarket;

        ClearConfidence();

        if (candidate is null)
        {
            SelectedCommodityText.Text =
                Loc.Get(
                    "Loc_TRADE_CARGO_DETAIL_TITLE");

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

            return;
        }

        SelectedCommodityText.Text =
            Loc.Get(
                "Loc_TRADE_CARGO_DETAIL_TITLE");

        SelectedProfitText.Text =
            Loc.Format(
                "Loc_TRADE_CARGO_DETAIL_VALUE",
                candidate.TotalRevenue,
                candidate.SellableUnits,
                candidate.TotalCargoUnits);

        SelectedSourceText.Text =
            Loc.Get(
                "Loc_TRADE_CARGO_CURRENT_CARGO");

        SelectedSourceMetaText.Text =
            BuildCargoManifestSummary();

        SelectedTargetText.Text =
            $"{candidate.Target.SystemName}"
            + Environment.NewLine
            + candidate.Target.StationName;

        SelectedTargetMetaText.Text =
            candidate.IsCurrentMarket
                ? Loc.Get(
                    "Loc_TRADE_CARGO_CURRENT_MARKET_META")
                : BuildStationMeta(
                    candidate.Target,
                    candidate.WorstDataAge);

        var economics =
            new List<string>();

        foreach (CargoSaleLine line in candidate.Lines)
        {
            economics.Add(
                Loc.Format(
                    "Loc_TRADE_CARGO_LINE",
                    line.DisplayName,
                    line.SellAmount,
                    line.CargoAmount,
                    line.SellPrice,
                    line.Revenue));
        }

        if (candidate.UnsoldUnits > 0)
        {
            economics.Add(
                Loc.Format(
                    "Loc_TRADE_CARGO_UNSOLD",
                    candidate.UnsoldUnits));
        }

        SelectedRouteEconomicsText.Text =
            string.Join(
                Environment.NewLine,
                economics);

        if (candidate.IsCurrentMarket)
        {
            SelectedTravelEstimateText.Text =
                Loc.Get(
                    "Loc_TRADE_CARGO_ALREADY_HERE");
        }
        else
        {
            TradeLegTravelEstimate travel =
                EstimateCargoSaleTravel(
                    candidate);

            SelectedTravelEstimateText.Text =
                Loc.Format(
                    "Loc_TRADE_CARGO_TRAVEL_DETAIL",
                    FormatTravelTime(
                        travel.TotalTime),
                    travel.EstimatedJumps,
                    travel.LoadedJumpRangeLy,
                    FormatCreditsPerHour(
                        CargoSaleRevenuePerHour(
                            candidate)));
        }
    }

    private string BuildCargoManifestSummary()
    {
        CargoCommoditySnapshot[] cargo =
            currentJournal.CargoByCommodityId
                .Values
                .Where(item =>
                    item.Count > 0)
                .OrderByDescending(item =>
                    item.Count)
                .ToArray();

        if (cargo.Length == 0)
        {
            return Loc.Get(
                "Loc_TRADE_CARGO_EMPTY");
        }

        string manifest =
            string.Join(
                ", ",
                cargo
                    .Take(6)
                    .Select(item =>
                        $"{item.DisplayName} {item.Count:N0} t"));

        if (cargo.Length > 6)
        {
            manifest +=
                $" +{cargo.Length - 6}";
        }

        return manifest;
    }

    private TradeLegTravelEstimate EstimateCargoSaleTravel(
        CargoSaleCandidate candidate)
    {
        if (candidate.IsCurrentMarket)
        {
            return new TradeLegTravelEstimate
            {
                CargoTons =
                    candidate.TotalCargoUnits,
                LoadedJumpRangeLy =
                    currentJournal.MaxJumpRangeLy,
                EstimatedJumps =
                    0,
                JumpTime =
                    TimeSpan.Zero,
                SupercruiseTime =
                    TimeSpan.Zero,
                FixedOperationsTime =
                    TimeSpan.Zero,
                StationDistanceLs =
                    0
            };
        }

        return travelTimeEstimator.EstimateLeg(
            candidate.DistanceLy,
            candidate.TotalCargoUnits,
            candidate.Target.DistanceToArrivalLs,
            currentJournal);
    }

    private long CargoSaleRevenuePerHour(
        CargoSaleCandidate candidate)
    {
        if (candidate.IsCurrentMarket)
        {
            return 0;
        }

        TimeSpan duration =
            EstimateCargoSaleTravel(
                candidate)
            .TotalTime;

        return duration.TotalSeconds <= 0
            ? 0
            : checked(
                (long)Math.Round(
                    candidate.TotalRevenue
                    * 3600d
                    / duration.TotalSeconds));
    }

    private long CargoSaleSortValuePerHour(
        CargoSaleCandidate candidate) =>
        candidate.IsCurrentMarket
            ? long.MaxValue
            : CargoSaleRevenuePerHour(
                candidate);

    private double CargoSaleTravelSeconds(
        CargoSaleCandidate candidate) =>
        candidate.IsCurrentMarket
            ? 0
            : EstimateCargoSaleTravel(
                candidate)
            .TotalTime
            .TotalSeconds;

    private bool TryBuildCargoSaleConstraints(
        out TradeSearchConstraints constraints,
        out string error)
    {
        constraints =
            null!;
        error =
            string.Empty;

        if (!currentJournal.JournalAvailable
            || string.IsNullOrWhiteSpace(
                currentJournal.StarSystem))
        {
            error =
                Loc.Get(
                    "Loc_TRADE_CARGO_VALIDATION_JOURNAL");

            return false;
        }

        int cargoUnits =
            currentJournal.CargoByCommodityId
                .Values
                .Where(item =>
                    item.Count > 0)
                .Sum(item =>
                    item.Count);

        if (cargoUnits <= 0)
        {
            error =
                Loc.Get(
                    "Loc_TRADE_CARGO_VALIDATION_EMPTY");

            return false;
        }

        int stationDistance =
            SelectedInt(
                MaxStationDistanceComboBox,
                0);

        constraints =
            new TradeSearchConstraints
            {
                OriginSystemName =
                    currentJournal.StarSystem,
                OriginSystemAddress =
                    currentJournal.SystemAddress,
                CargoCapacity =
                    cargoUnits,
                SourceSearchRadiusLy =
                    0,
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
                    1,
                MinDemand =
                    SelectedLong(
                        MinDemandComboBox,
                        1),
                MaxCommodityCandidates =
                    Math.Max(
                        1,
                        currentJournal.CargoByCommodityId.Count),
                MaxResults =
                    SearchResultPoolSize,
                MaxConcurrentCommoditySearches =
                    6
            };

        try
        {
            constraints.Validate();
            return true;
        }
        catch (Exception ex)
        {
            error =
                ex.Message;
            constraints =
                null!;
            return false;
        }
    }

    private void RefreshCargoSaleFooter()
    {
        if (currentCargoSaleCandidates.Count == 0)
        {
            FooterText.Text =
                Loc.Get(
                    "Loc_TRADE_CARGO_IDLE_FOOTER");
            return;
        }

        long best =
            currentCargoSaleCandidates.Max(item =>
                item.TotalRevenue);

        FooterText.Text =
            Loc.Format(
                "Loc_TRADE_CARGO_FOOTER",
                currentCargoSaleCandidates.Count,
                best);
    }

    private void RefreshCargoSaleCompact(
        bool preserveStatus)
    {
        int cargoUnits =
            currentJournal.CargoByCommodityId
                .Values
                .Where(item => item.Count > 0)
                .Sum(item => item.Count);

        CompactFiltersText.Text =
            Loc.Format(
                "Loc_TRADE_CARGO_COMPACT_FILTERS",
                currentJournal.StarSystem,
                SelectedInt(
                    TargetRadiusComboBox,
                    80),
                CountActiveAdvancedFilters());

        CargoSaleCandidate? best =
            SortedCargoSaleCandidates()
                .FirstOrDefault();

        if (best is null)
        {
            CompactBestRouteText.Text =
                Loc.Get(
                    "Loc_TRADE_CARGO_NO_RESULTS");

            if (!preserveStatus
                && searchCancellation is null)
            {
                CompactStatusText.Text =
                    cargoUnits > 0
                        ? Loc.Get(
                            "Loc_TRADE_CARGO_READY")
                        : Loc.Get(
                            "Loc_TRADE_CARGO_VALIDATION_EMPTY");
            }

            CompactFooterText.Text =
                Loc.Get(
                    "Loc_TRADE_CARGO_IDLE_FOOTER");
            return;
        }

        CompactBestRouteText.Text =
            Loc.Format(
                "Loc_TRADE_CARGO_COMPACT_BEST",
                best.TotalRevenue,
                best.Target.SystemName,
                best.Target.StationName);

        if (!preserveStatus
            && searchCancellation is null)
        {
            CompactStatusText.Text =
                Loc.Format(
                    "Loc_TRADE_COMPACT_RESULTS_FORMAT",
                    currentCargoSaleCandidates.Count);
        }

        CompactFooterText.Text =
            Loc.Format(
                "Loc_TRADE_CARGO_COMPACT_META",
                best.SellableUnits,
                best.TotalCargoUnits,
                best.DistanceLy,
                best.WorstDataAge.TotalHours);
    }

    private void ResetCargoSaleResults()
    {
        currentCargoSaleCandidates.Clear();
        selectedCargoSaleCandidate =
            null;
    }

    private int ActiveResultCount =>
        IsCargoSaleMode
            ? currentCargoSaleCandidates.Count
            : currentCandidates.Count;

    private string SearchIdleResourceKey() =>
        IsCargoSaleMode
            ? "Loc_TRADE_CARGO_SEARCH"
            : IsCommodityLookupMode
                ? "Loc_TRADE_FIND_COMMODITY"
                : IsContinuousMode
                    ? "Loc_TRADE_CONTINUOUS_SEARCH"
                    : "Loc_SEARCH_ROUTES";

    private void ApplyCargoSaleControlAvailability(
        bool running)
    {
        bool cargoMode =
            IsCargoSaleMode;

        SelectedSourceLabelText.SetResourceReference(
            TextBlock.TextProperty,
            cargoMode
                ? "Loc_TRADE_CARGO_CURRENT_CARGO"
                : "Loc_TRADE_SOURCE");

        SelectedTargetLabelText.SetResourceReference(
            TextBlock.TextProperty,
            cargoMode
                ? "Loc_TRADE_CARGO_BUYER"
                : "Loc_TRADE_TARGET");

        if (running)
        {
            return;
        }

        AnchorSystemTextBox.IsEnabled =
            !cargoMode;
        CargoTextBox.IsEnabled =
            !cargoMode;
        SourceRadiusComboBox.IsEnabled =
            !cargoMode;
        SyncJournalButton.IsEnabled =
            !cargoMode;
        MinSupplyComboBox.IsEnabled =
            !cargoMode;
    }

    private void UpdateCargoSaleSortLabels()
    {
        PrimaryProfitSortItem.SetResourceReference(
            ContentControl.ContentProperty,
            IsCargoSaleMode
                ? "Loc_TRADE_CARGO_SORT_VALUE"
                : IsRoundTripMode
                    ? "Loc_TRADE_SORT_CYCLE"
                    : "Loc_TRADE_SORT_PROFIT");

        PerHourSortItem.SetResourceReference(
            ContentControl.ContentProperty,
            IsCargoSaleMode
                ? "Loc_TRADE_CARGO_SORT_VALUE_HOUR"
                : "Loc_TRADE_SORT_PER_HOUR");

        PerTonSortItem.SetResourceReference(
            ContentControl.ContentProperty,
            IsCargoSaleMode
                ? "Loc_TRADE_CARGO_SORT_VALUE_TON"
                : "Loc_TRADE_SORT_PER_TON");
    }

    private static string CargoSaleKey(
        CargoSaleCandidate candidate) =>
        candidate.Target.MarketId.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
}
