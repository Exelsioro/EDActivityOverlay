using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl
{
    private const int RoundTripSeedLimit = 40;

    private readonly TradeRoundTripSearchService roundTripSearchService =
        new();

    private readonly Dictionary<string, TradeRoundTripCandidate> roundTripByOutboundKey =
        new(
            StringComparer.Ordinal);

    private bool IsRoundTripMode =>
        string.Equals(
            RouteModeTag(),
            "roundtrip",
            StringComparison.OrdinalIgnoreCase);

    private async Task RunSelectedSearchModeAsync(
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken)
    {
        if (IsCargoSaleMode)
        {
            await RunCargoSaleSearchAsync(
                constraints,
                cancellationToken);
            return;
        }

        if (!IsRoundTripMode)
        {
            await foreach (TradeSearchProgress progress
                           in searchService.SearchProgressAsync(
                               constraints,
                               cancellationToken))
            {
                ApplyProgress(
                    progress);
            }

            return;
        }

        await foreach (TradeRoundTripSearchProgress progress
                       in roundTripSearchService.SearchProgressAsync(
                           constraints,
                           RoundTripSeedLimit,
                           cancellationToken))
        {
            ApplyRoundTripProgress(
                progress);
        }
    }

    private void ApplyRoundTripProgress(
        TradeRoundTripSearchProgress progress)
    {
        string status;

        switch (progress.Stage)
        {
            case TradeRoundTripSearchStage.DiscoveringOutbound:
                status =
                    progress.TotalOutboundCommodities > 0
                        ? Loc.Format(
                            "Loc_TRADE_ROUND_DISCOVERY_PROGRESS",
                            progress.CompletedOutboundCommodities,
                            progress.TotalOutboundCommodities,
                            progress.PotentialOutboundRoutes)
                        : Loc.Get(
                            "Loc_TRADE_ROUND_DISCOVERY_START");

                SearchStatusText.Text =
                    status;

                CompactStatusText.Text =
                    status;

                CompactFooterText.Text =
                    Loc.Format(
                        "Loc_TRADE_COMPACT_PROGRESS",
                        progress.CompletedOutboundCommodities,
                        progress.TotalOutboundCommodities,
                        progress.Elapsed.TotalSeconds);

                return;

            case TradeRoundTripSearchStage.EnrichingPairs:
                status =
                    progress.FailedPairs > 0
                        ? Loc.Format(
                            "Loc_TRADE_ROUND_ENRICH_PROGRESS_FAILED",
                            progress.CompletedPairs,
                            progress.TotalPairs,
                            progress.BestCandidates.Count,
                            progress.FailedPairs)
                        : Loc.Format(
                            "Loc_TRADE_ROUND_ENRICH_PROGRESS",
                            progress.CompletedPairs,
                            progress.TotalPairs,
                            progress.BestCandidates.Count);

                SearchStatusText.Text =
                    status;

                CompactStatusText.Text =
                    status;

                if (progress.BestCandidates.Count > 0)
                {
                    ApplyRoundTripCandidates(
                        progress.BestCandidates);
                }

                FooterText.Text =
                    Loc.Format(
                        "Loc_TRADE_ROUND_ENRICH_FOOTER",
                        progress.CompletedPairs,
                        progress.TotalPairs,
                        progress.Elapsed.TotalSeconds);

                CompactFooterText.Text =
                    Loc.Format(
                        "Loc_TRADE_ROUND_ENRICH_FOOTER",
                        progress.CompletedPairs,
                        progress.TotalPairs,
                        progress.Elapsed.TotalSeconds);

                RefreshCompactPresentation(
                    preserveStatus:
                        true);

                return;

            case TradeRoundTripSearchStage.Completed:
                if (progress.BestCandidates.Count > 0)
                {
                    ApplyRoundTripCandidates(
                        progress.BestCandidates);
                }

                status =
                    Loc.Format(
                        "Loc_TRADE_ROUND_DONE",
                        currentCandidates.Count,
                        progress.Elapsed.TotalSeconds);

                SearchStatusText.Text =
                    status;

                CompactStatusText.Text =
                    status;

                RefreshFooter();
                RefreshCompactPresentation(
                    preserveStatus:
                        true);

                return;
        }
    }

    private void ApplyRoundTripCandidates(
        IReadOnlyList<TradeRoundTripCandidate> candidates)
    {
        roundTripByOutboundKey.Clear();

        foreach (TradeRoundTripCandidate candidate
                 in candidates)
        {
            roundTripByOutboundKey[
                Key(
                    candidate.Outbound)] =
                candidate;
        }

        ApplyCandidates(
            candidates
                .Select(
                    candidate =>
                        candidate.ToDisplayCandidate())
                .ToArray());
    }

    private bool TryGetRoundTrip(
        TradeRouteCandidate candidate,
        out TradeRoundTripCandidate roundTrip) =>
        roundTripByOutboundKey.TryGetValue(
            Key(
                candidate),
            out roundTrip!);

    private bool TryBuildRoundTripRow(
        TradeRouteCandidate candidate,
        bool held,
        out TradeRow row)
    {
        if (!TryGetRoundTrip(
                candidate,
                out TradeRoundTripCandidate roundTrip))
        {
            row =
                null!;

            return
                false;
        }

        TradeRouteConfidence confidence =
            ConfidenceFor(
                candidate);

        row =
            new TradeRow(
                candidate,
                Key(
                    candidate),
                held
                    ? Loc.Get(
                        "Loc_TRADE_HELD_SELECTION")
                    : Loc.Get(
                        "Loc_TRADE_ROUND_BADGE"),
                ConfidenceBadge(
                    confidence),
                confidence.Level.ToString(),
                Loc.Format(
                    "Loc_TRADE_ROUND_ROW_COMMODITIES",
                    roundTrip.Outbound.Source.CommodityName.ToUpperInvariant(),
                    roundTrip.ReturnCommodity.ToUpperInvariant()),
                $"{roundTrip.Outbound.Source.SystemName} / {roundTrip.Outbound.Source.StationName}",
                $"⇄ {roundTrip.Outbound.Target.SystemName} / {roundTrip.Outbound.Target.StationName}",
                Loc.Format(
                    "Loc_TRADE_ROUND_ROW_PER_TON",
                    roundTrip.Outbound.ProfitPerTon,
                    roundTrip.ReturnProfitPerTon),
                Loc.Format(
                    "Loc_TRADE_ROUND_ROW_CYCLE",
                    roundTrip.ProfitPerCycle),
                Loc.Format(
                    "Loc_TRADE_ROW_DISTANCE_FORMAT",
                    roundTrip.TradeLegDistanceLy),
                FormatEstimatedTravelTime(
                    candidate),
                FormatCreditsPerHour(
                    EstimatedProfitPerHour(
                        candidate)),
                Loc.Format(
                    "Loc_TRADE_ROW_AGE_FORMAT",
                    roundTrip.WorstDataAge.TotalHours));

        return
            true;
    }

    private bool TryShowRoundTripCandidate(
        TradeRouteCandidate candidate)
    {
        if (!TryGetRoundTrip(
                candidate,
                out TradeRoundTripCandidate roundTrip))
        {
            return
                false;
        }

        SelectedCommodityText.Text =
            Loc.Format(
                "Loc_TRADE_ROUND_DETAIL_TITLE",
                roundTrip.Outbound.Source.CommodityName.ToUpperInvariant(),
                roundTrip.ReturnCommodity.ToUpperInvariant());

        SelectedProfitText.Text =
            Loc.Format(
                "Loc_TRADE_ROUND_DETAIL_PROFIT",
                roundTrip.ProfitPerCycle,
                roundTrip.Outbound.ProfitPerTrip,
                roundTrip.ReturnProfitPerTrip);

        SelectedSourceText.Text =
            $"{roundTrip.Outbound.Source.SystemName}"
            + Environment.NewLine
            + roundTrip.Outbound.Source.StationName;

        SelectedSourceMetaText.Text =
            BuildStationMeta(
                roundTrip.Outbound.Source,
                Max(
                    roundTrip.Outbound.SourceAge,
                    roundTrip.ReturnTargetAge));

        SelectedTargetText.Text =
            $"{roundTrip.Outbound.Target.SystemName}"
            + Environment.NewLine
            + roundTrip.Outbound.Target.StationName;

        SelectedTargetMetaText.Text =
            BuildStationMeta(
                roundTrip.Outbound.Target,
                Max(
                    roundTrip.Outbound.TargetAge,
                    roundTrip.ReturnSourceAge));

        string outboundDemand =
            roundTrip.Outbound.Target.HasInfiniteDemand
                ? "∞"
                : roundTrip.Outbound.Target.Demand.ToString(
                    "N0");

        string returnDemand =
            roundTrip.ReturnTarget.HasInfiniteDemand
                ? "∞"
                : roundTrip.ReturnTarget.Demand.ToString(
                    "N0");

        SelectedRouteEconomicsText.Text =
            Loc.Format(
                "Loc_TRADE_ROUND_DETAIL_ECONOMICS",
                roundTrip.Outbound.Source.CommodityName.ToUpperInvariant(),
                roundTrip.Outbound.Source.BuyFromStationPrice,
                roundTrip.Outbound.Target.SellToStationPrice,
                roundTrip.Outbound.Source.Stock,
                outboundDemand,
                roundTrip.Outbound.TradableAmount,
                roundTrip.ReturnCommodity.ToUpperInvariant(),
                roundTrip.ReturnSource.BuyFromStationPrice,
                roundTrip.ReturnTarget.SellToStationPrice,
                roundTrip.ReturnSource.Stock,
                returnDemand,
                roundTrip.ReturnTradableAmount,
                roundTrip.TradeLegDistanceLy,
                roundTrip.CycleDistanceLy);

        SelectedTravelEstimateText.Text =
            FormatTravelDetail(
                roundTrip);

        ShowConfidence(
            roundTrip);

        return
            true;
    }

    private bool TryRenderRoundTripCompact(
        TradeRouteCandidate candidate,
        bool preserveStatus)
    {
        if (!TryGetRoundTrip(
                candidate,
                out TradeRoundTripCandidate roundTrip))
        {
            return
                false;
        }

        CompactBestRouteText.Text =
            Loc.Format(
                "Loc_TRADE_ROUND_COMPACT_BEST",
                roundTrip.Outbound.Source.CommodityName.ToUpperInvariant(),
                roundTrip.ReturnCommodity.ToUpperInvariant(),
                roundTrip.Outbound.Source.SystemName,
                roundTrip.Outbound.Target.SystemName,
                roundTrip.ProfitPerCycle,
                roundTrip.TradeLegDistanceLy);

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
                "Loc_TRADE_ROUND_COMPACT_META",
                roundTrip.Outbound.ProfitPerTrip,
                roundTrip.ReturnProfitPerTrip,
                roundTrip.CycleDistanceLy,
                roundTrip.WorstDataAge.TotalHours)
            + Environment.NewLine
            + FormatCompactTravel(
                roundTrip);

        return
            true;
    }

    private void PinSelectedCandidate()
    {
        if (selectedCandidate is null)
        {
            return;
        }

        if (TryGetRoundTrip(
                selectedCandidate,
                out TradeRoundTripCandidate roundTrip))
        {
            RoundTripPinRequested?.Invoke(
                roundTrip);

            return;
        }

        PinRequested?.Invoke(
            selectedCandidate);
    }

    public event Action<TradeRoundTripCandidate>? RoundTripPinRequested;

    private void RouteModeComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (applyingJournal)
        {
            return;
        }

        searchCancellation?.Cancel();

        roundTripByOutboundKey.Clear();
        ResetCargoSaleResults();
        currentCandidates.Clear();
        currentPage =
            0;
        selectedCandidate =
            null;

        RoutesList.ItemsSource =
            null;

        UpdatePaginationUi();
        ShowSelectedCandidate(
            null);

        UpdateRouteModeUi();
        CaptureSession();
        RefreshFooter();
        RefreshCompactPresentation();
    }

    private void UpdateRouteModeUi()
    {
        UpdateCargoSaleSortLabels();
        UpdateConfidenceSortAvailability();
        ApplyCargoSaleControlAvailability(
            searchCancellation is not null);

        SearchButton.SetResourceReference(
            ContentControl.ContentProperty,
            searchCancellation is not null
                ? "Loc_TRADE_CANCEL"
                : SearchIdleResourceKey());

        CompactActionButton.SetResourceReference(
            ContentControl.ContentProperty,
            searchCancellation is not null
                ? "Loc_TRADE_CANCEL"
                : SearchIdleResourceKey());
    }
    private string RouteModeTag() =>
        (RouteModeComboBox.SelectedItem
            as ComboBoxItem)?.Tag?.ToString()
        ?? "oneway";

    private static TimeSpan Max(
        TimeSpan left,
        TimeSpan right) =>
        left >= right
            ? left
            : right;
}
