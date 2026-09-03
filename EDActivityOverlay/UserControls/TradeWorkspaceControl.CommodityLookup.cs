using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl
{
    private readonly TradeCommodityLookupService commodityLookupService = new();
    private bool commodityNamesLoading;

    public event Action<string>? NavigateSystemRequested;

    private bool IsCommodityLookupMode =>
        string.Equals(RouteModeTag(), "commodity", StringComparison.OrdinalIgnoreCase);

    private void InitializeCommodityLookupMode()
    {
        _ = LoadCommodityNamesAsync();
    }

    private async Task LoadCommodityNamesAsync()
    {
        if (commodityNamesLoading)
            return;

        commodityNamesLoading = true;
        try
        {
            IReadOnlyList<string> names =
                await commodityLookupService.GetCommodityNamesAsync().ConfigureAwait(true);
            string text = CommodityLookupComboBox.Text;
            CommodityLookupComboBox.ItemsSource = names;
            if (!string.IsNullOrWhiteSpace(text))
                CommodityLookupComboBox.Text = text;
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Commodity lookup catalog failed: {ex.Message}");
        }
        finally
        {
            commodityNamesLoading = false;
        }
    }

    private int CommodityLookupQuantity()
    {
        return int.TryParse(
            CargoTextBox.Text.Trim(),
            NumberStyles.Integer,
            CultureInfo.CurrentCulture,
            out int quantity)
            ? Math.Max(1, quantity)
            : 1;
    }

    private bool TryBuildCommodityLookupConstraints(
        out TradeSearchConstraints constraints,
        out string error)
    {
        constraints = null!;
        error = string.Empty;

        string anchor = AnchorSystemTextBox.Text.Trim();
        string commodity = CommodityLookupComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(anchor))
        {
            error = Loc.Get("Loc_TRADE_VALIDATION_SYSTEM");
            return false;
        }
        if (string.IsNullOrWhiteSpace(commodity))
        {
            error = Loc.Get("Loc_TRADE_COMMODITY_VALIDATION");
            return false;
        }
        if (!int.TryParse(
                CargoTextBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int quantity)
            || quantity < 1)
        {
            error = Loc.Get("Loc_TRADE_VALIDATION_CARGO");
            return false;
        }

        long address = currentJournal.SystemAddress != 0
            && currentJournal.StarSystem.Equals(anchor, StringComparison.OrdinalIgnoreCase)
                ? currentJournal.SystemAddress
                : 0;
        int stationDistance = SelectedInt(MaxStationDistanceComboBox, 0);

        constraints = new TradeSearchConstraints
        {
            OriginSystemName = anchor,
            OriginSystemAddress = address,
            CargoCapacity = quantity,
            AvailableCredits = currentJournal.JournalAvailable
                ? Math.Max(0, currentJournal.Balance)
                : null,
            SourceSearchRadiusLy = SelectedInt(SourceRadiusComboBox, 30),
            TargetSearchRadiusLy = 0,
            MaxDataAge = TimeSpan.FromHours(SelectedInt(MaxAgeComboBox, 72)),
            MinLandingPadSize = SelectedInt(MinPadComboBox, 3),
            MaxStationDistanceLs = stationDistance <= 0 ? null : stationDistance,
            IncludeFleetCarriers = FleetCarriersCheckBox.IsChecked == true,
            MinSupply = 1,
            MinDemand = 1,
            MaxCommodityCandidates = 1,
            MaxResults = SearchResultPoolSize,
            MaxConcurrentCommoditySearches = 1
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

    private async Task RunCommodityLookupSearchAsync(
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken)
    {
        string commodity = CommodityLookupComboBox.Text.Trim();
        int quantity = CommodityLookupQuantity();
        string searching = Loc.Format(
            "Loc_TRADE_COMMODITY_SEARCHING_FORMAT",
            commodity,
            quantity);
        SearchStatusText.Text = searching;
        CompactStatusText.Text = searching;

        IReadOnlyList<TradeCommoditySourceCandidate> sources =
            await commodityLookupService.SearchAsync(
                    constraints,
                    commodity,
                    quantity,
                    cancellationToken)
                .ConfigureAwait(true);

        ApplyCandidates(sources.Select(ToCommodityDisplayCandidate).ToArray());
        string done = sources.Count == 0
            ? Loc.Get("Loc_TRADE_COMMODITY_NO_RESULTS")
            : Loc.Format("Loc_TRADE_COMMODITY_DONE_FORMAT", sources.Count);
        SearchStatusText.Text = done;
        CompactStatusText.Text = done;
        RefreshCommodityLookupFooter();
        RefreshCommodityLookupCompact(preserveStatus: true);
    }

    private static TradeRouteCandidate ToCommodityDisplayCandidate(
        TradeCommoditySourceCandidate source)
    {
        return new TradeRouteCandidate
        {
            Source = source.Market,
            Target = source.Market,
            ProfitPerTon = -source.Market.BuyFromStationPrice,
            TradableAmount = source.PurchasableQuantity,
            ProfitPerTrip = -source.TotalCost,
            OriginToSourceDistanceLy = source.DistanceLy,
            SourceToTargetDistanceLy = 0,
            SourceAge = source.Age,
            TargetAge = source.Age
        };
    }

    private TradeCommoditySourceCandidate CommoditySourceFromDisplay(
        TradeRouteCandidate candidate)
    {
        int requested = CommodityLookupQuantity();
        int available = checked((int)Math.Min(
            Math.Max(0L, candidate.Source.Stock),
            int.MaxValue));
        int quantity = Math.Min(
            requested,
            Math.Min(available, Math.Max(0, candidate.TradableAmount)));
        return new TradeCommoditySourceCandidate(
            candidate.Source,
            candidate.Source.CommodityName,
            requested,
            available,
            quantity,
            checked((long)quantity * candidate.Source.BuyFromStationPrice),
            candidate.SourceAge);
    }

    private IEnumerable<TradeRouteCandidate> SortedCommodityLookupCandidates()
    {
        IEnumerable<TradeRouteCandidate> rows = currentCandidates;
        return SortTag() switch
        {
            "distance" => rows
                .OrderBy(item => item.OriginToSourceDistanceLy)
                .ThenBy(item => item.Source.BuyFromStationPrice),
            "freshness" => rows
                .OrderBy(item => item.SourceAge)
                .ThenBy(item => item.Source.BuyFromStationPrice),
            "time" => rows
                .OrderBy(item => EstimateCommodityTravel(item).TotalTime)
                .ThenBy(item => item.Source.BuyFromStationPrice),
            _ => rows
                .OrderByDescending(item => CommoditySourceFromDisplay(item).FullCoverage)
                .ThenBy(item => item.Source.BuyFromStationPrice)
                .ThenBy(item => item.OriginToSourceDistanceLy)
        };
    }

    private bool TryBuildCommodityLookupRow(
        TradeRouteCandidate candidate,
        bool held,
        out TradeRow row)
    {
        if (!IsCommodityLookupMode)
        {
            row = null!;
            return false;
        }

        TradeCommoditySourceCandidate source = CommoditySourceFromDisplay(candidate);
        string coverage = source.RequestedQuantity <= 0
            ? "—"
            : $"{Math.Min(100d, source.AvailableQuantity * 100d / source.RequestedQuantity):0}%";
        row = new TradeRow(
            candidate,
            Key(candidate),
            held ? Loc.Get("Loc_TRADE_HELD_SELECTION") : Loc.Get("Loc_TRADE_COMMODITY_SOURCE_BADGE"),
            source.FullCoverage ? Loc.Get("Loc_TRADE_COMMODITY_FULL_STOCK") : Loc.Get("Loc_TRADE_COMMODITY_PARTIAL_STOCK"),
            source.FullCoverage ? "High" : "Medium",
            source.CommodityName.ToUpperInvariant(),
            $"{source.Market.SystemName} / {source.Market.StationName}",
            Loc.Format("Loc_TRADE_COMMODITY_STOCK_FORMAT", source.AvailableQuantity, coverage),
            Loc.Format("Loc_TRADE_COMMODITY_TOTAL_FORMAT", source.PurchasableQuantity, source.TotalCost),
            Loc.Format("Loc_TRADE_COMMODITY_PRICE_FORMAT", source.Market.BuyFromStationPrice),
            Loc.Format("Loc_TRADE_ROW_DISTANCE_FORMAT", source.DistanceLy),
            FormatCommodityTravel(source),
            string.Empty,
            Loc.Format("Loc_TRADE_ROW_AGE_FORMAT", source.Age.TotalHours));
        return true;
    }

    private bool TryShowCommodityLookupCandidate(TradeRouteCandidate candidate)
    {
        if (!IsCommodityLookupMode)
            return false;

        TradeCommoditySourceCandidate source = CommoditySourceFromDisplay(candidate);
        PinRouteButton.IsEnabled = !string.IsNullOrWhiteSpace(source.Market.SystemName);
        PinRouteButton.SetResourceReference(
            ContentControl.ContentProperty,
            "Loc_TRADE_PLOT_TO_SELLER");

        SelectedCommodityText.Text = source.CommodityName.ToUpperInvariant();
        SelectedProfitText.Text = Loc.Format(
            "Loc_TRADE_COMMODITY_DETAIL_PRICE",
            source.Market.BuyFromStationPrice,
            source.PurchasableQuantity,
            source.TotalCost);
        SelectedSourceLabelText.SetResourceReference(TextBlock.TextProperty, "Loc_TRADE_COMMODITY_SELLER");
        SelectedTargetLabelText.SetResourceReference(TextBlock.TextProperty, "Loc_TRADE_COMMODITY_AVAILABILITY");
        SelectedSourceText.Text = $"{source.Market.SystemName}{Environment.NewLine}{source.Market.StationName}";
        SelectedSourceMetaText.Text = BuildStationMeta(source.Market, source.Age);
        SelectedTargetText.Text = Loc.Format(
            "Loc_TRADE_COMMODITY_AVAILABILITY_FORMAT",
            source.AvailableQuantity,
            source.RequestedQuantity,
            source.PurchasableQuantity);
        SelectedTargetMetaText.Text = string.Empty;
        SelectedRouteEconomicsText.Text = Loc.Format(
            "Loc_TRADE_COMMODITY_DETAIL_ECONOMICS",
            source.Market.BuyFromStationPrice,
            source.TotalCost,
            source.AvailableQuantity,
            source.DistanceLy);
        SelectedTravelEstimateText.Text = FormatCommodityTravelDetail(source);
        ClearConfidence();
        return true;
    }

    private void PinCommodityLookupCandidate()
    {
        if (selectedCandidate is null || !IsCommodityLookupMode)
            return;
        string system = selectedCandidate.Source.SystemName;
        if (string.IsNullOrWhiteSpace(system))
            return;
        NavigateSystemRequested?.Invoke(system);
    }

    private TradeLegTravelEstimate EstimateCommodityTravel(TradeRouteCandidate candidate) =>
        travelTimeEstimator.EstimateLeg(
            candidate.OriginToSourceDistanceLy,
            0,
            candidate.Source.DistanceToArrivalLs,
            currentJournal);

    private string FormatCommodityTravel(TradeCommoditySourceCandidate source) =>
        FormatTravelTime(EstimateCommodityTravel(ToCommodityDisplayCandidate(source)).TotalTime);

    private string FormatCommodityTravelDetail(TradeCommoditySourceCandidate source)
    {
        TradeLegTravelEstimate estimate = EstimateCommodityTravel(ToCommodityDisplayCandidate(source));
        return Loc.Format(
            "Loc_TRADE_COMMODITY_TRAVEL_FORMAT",
            source.DistanceLy,
            FormatTravelTime(estimate.TotalTime),
            estimate.EstimatedJumps);
    }

    private void RefreshCommodityLookupFooter()
    {
        if (!IsCommodityLookupMode)
            return;
        TradeRouteCandidate? best = SortedCommodityLookupCandidates().FirstOrDefault();
        FooterText.Text = best is null
            ? Loc.Get("Loc_TRADE_COMMODITY_IDLE_FOOTER")
            : Loc.Format(
                "Loc_TRADE_COMMODITY_FOOTER",
                currentCandidates.Count,
                best.Source.BuyFromStationPrice,
                best.Source.SystemName,
                best.Source.StationName);
    }

    private void RefreshCommodityLookupCompact(bool preserveStatus)
    {
        string commodity = string.IsNullOrWhiteSpace(CommodityLookupComboBox.Text)
            ? "—"
            : CommodityLookupComboBox.Text.Trim();
        CompactFiltersText.Text = Loc.Format(
            "Loc_TRADE_COMMODITY_COMPACT_FILTERS",
            AnchorSystemTextBox.Text.Trim(),
            commodity,
            CommodityLookupQuantity(),
            SelectedInt(SourceRadiusComboBox, 30));

        TradeRouteCandidate? best = SortedCommodityLookupCandidates().FirstOrDefault();
        if (best is null)
        {
            CompactBestRouteText.Text = Loc.Get("Loc_TRADE_COMMODITY_NO_RESULTS");
            if (!preserveStatus && searchCancellation is null)
                CompactStatusText.Text = Loc.Get("Loc_TRADE_COMMODITY_READY");
            CompactFooterText.Text = Loc.Get("Loc_TRADE_COMMODITY_IDLE_FOOTER");
            return;
        }

        TradeCommoditySourceCandidate source = CommoditySourceFromDisplay(best);
        CompactBestRouteText.Text = Loc.Format(
            "Loc_TRADE_COMMODITY_COMPACT_BEST",
            source.CommodityName,
            source.Market.BuyFromStationPrice,
            source.Market.SystemName,
            source.Market.StationName);
        if (!preserveStatus && searchCancellation is null)
            CompactStatusText.Text = Loc.Format("Loc_TRADE_COMMODITY_DONE_FORMAT", currentCandidates.Count);
        CompactFooterText.Text = Loc.Format(
            "Loc_TRADE_COMMODITY_COMPACT_META",
            source.AvailableQuantity,
            source.DistanceLy,
            source.Age.TotalHours);
    }

    private void UpdateCommodityLookupModeUi()
    {
        bool lookup = IsCommodityLookupMode;
        PinRouteButton.SetResourceReference(
            ContentControl.ContentProperty,
            lookup ? "Loc_TRADE_PLOT_TO_SELLER" : "Loc_TRADE_PIN_ROUTE");
        TargetRadiusPanel.Visibility = lookup ? Visibility.Collapsed : Visibility.Visible;
        CommodityLookupPanel.Visibility = lookup ? Visibility.Visible : Visibility.Collapsed;
        CargoLabelText.SetResourceReference(
            TextBlock.TextProperty,
            lookup ? "Loc_TRADE_COMMODITY_QUANTITY" : "Loc_TRADE_CARGO_FREE");

        if (lookup)
        {
            PrimaryProfitSortItem.SetResourceReference(ContentControl.ContentProperty, "Loc_TRADE_SORT_CHEAPEST");
        }
        PerHourSortItem.IsEnabled = !lookup;
        PerTonSortItem.IsEnabled = !lookup;
        ConfidenceSortItem.IsEnabled = !lookup;
    }

    private void ApplyCommodityLookupControlAvailability(bool running)
    {
        if (!IsCommodityLookupMode)
            return;
        CommodityLookupComboBox.IsEnabled = !running;
        TargetRadiusComboBox.IsEnabled = false;
        MinDemandComboBox.IsEnabled = false;
    }

    private void RefreshCommodityLookupLocalization()
    {
        UpdateCommodityLookupModeUi();
    }
}
