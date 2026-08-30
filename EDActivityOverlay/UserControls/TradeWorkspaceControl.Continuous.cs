using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl
{
    private readonly TradeContinuousSearchService continuousSearchService =
        new();

    private readonly Dictionary<string, TradeContinuousPlan> continuousByFirstKey =
        new(StringComparer.Ordinal);

    private readonly LinkedList<long> recentContinuousMarkets =
        new();

    private List<TradeContinuousPlan> currentContinuousPlans =
        new();

    private TradeContinuousPlan? pendingPinnedContinuousPlan;
    private TradeRouteCandidate? plannedContinuousLookahead;
    private bool activeContinuousRoute;

    private IReadOnlyList<TradeContinuousPlan> activeContinuationOptions =
        Array.Empty<TradeContinuousPlan>();

    private TradeContinuousPlan? activeContinuationPreview;
    private CancellationTokenSource? continuationPreviewCancellation;
    private bool continuationPreviewRefreshing;
    private bool continuationPreviewFinalized;
    private string continuationPreviewRequestKey =
        string.Empty;

    private bool IsContinuousMode =>
        string.Equals(
            RouteModeTag(),
            "continuous",
            StringComparison.OrdinalIgnoreCase);

    private async Task RunContinuousSearchAsync(
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken)
    {
        TradeContinuousSearchRequest request =
            BuildContinuousRequestFromCurrentMarket(
                constraints);

        var progress =
            new Progress<TradeContinuousSearchProgress>(
                ApplyContinuousProgress);

        IReadOnlyList<TradeContinuousPlan> plans =
            await continuousSearchService.SearchAsync(
                    request,
                    progress,
                    cancellationToken)
                .ConfigureAwait(true);

        ApplyContinuousPlans(
            plans);

        string status =
            plans.Count == 0
                ? Loc.Get(
                    "Loc_TRADE_CONTINUOUS_NO_RESULTS")
                : Loc.Format(
                    "Loc_TRADE_CONTINUOUS_DONE",
                    plans.Count);

        SearchStatusText.Text =
            status;

        CompactStatusText.Text =
            status;

        RefreshContinuousFooter();
        RefreshContinuousCompact(
            preserveStatus:
                true);
    }

    private void ApplyContinuousProgress(
        TradeContinuousSearchProgress progress)
    {
        string status =
            progress.Stage switch
            {
                TradeContinuousSearchStage.ResolvingStart =>
                    Loc.Get(
                        "Loc_TRADE_CONTINUOUS_RESOLVING"),
                TradeContinuousSearchStage.SearchingFirstHop =>
                    Loc.Get(
                        "Loc_TRADE_CONTINUOUS_FIRST_HOP"),
                TradeContinuousSearchStage.EnrichingLookahead =>
                    Loc.Format(
                        "Loc_TRADE_CONTINUOUS_LOOKAHEAD_PROGRESS",
                        progress.CompletedSeeds,
                        progress.TotalSeeds,
                        progress.PlansAvailable,
                        progress.FailedSeeds),
                _ =>
                    Loc.Format(
                        "Loc_TRADE_CONTINUOUS_DONE",
                        progress.PlansAvailable)
            };

        SearchStatusText.Text =
            status;

        CompactStatusText.Text =
            status;
    }

    private TradeContinuousSearchRequest BuildContinuousRequestFromCurrentMarket(
        TradeSearchConstraints constraints)
    {
        if (currentJournal.MarketId is not { } marketId
            || marketId <= 0)
        {
            throw new InvalidOperationException(
                Loc.Get(
                    "Loc_TRADE_CONTINUOUS_VALIDATION_MARKET"));
        }

        return new TradeContinuousSearchRequest
        {
            StartSystem =
                new TradeSystemReference(
                    currentJournal.StarSystem,
                    currentJournal.SystemAddress),
            StartMarketId =
                marketId,
            Constraints =
                constraints,
            Ship =
                currentJournal,
            RecentMarketIds =
                RecentContinuousMarketIds()
        };
    }

    private void ApplyContinuousPlans(
        IReadOnlyList<TradeContinuousPlan> plans)
    {
        continuousByFirstKey.Clear();

        currentContinuousPlans =
            plans
                .Take(
                    SearchResultPoolSize)
                .ToList();

        foreach (TradeContinuousPlan plan
                 in currentContinuousPlans)
        {
            continuousByFirstKey[
                Key(
                    plan.First)] =
                plan;
        }

        ApplyCandidates(
            currentContinuousPlans
                .Select(plan =>
                    plan.First)
                .ToArray());
    }

    private void ResetContinuousResults()
    {
        continuousByFirstKey.Clear();
        currentContinuousPlans.Clear();
    }

    private bool TryGetContinuousPlan(
        TradeRouteCandidate candidate,
        out TradeContinuousPlan plan) =>
        continuousByFirstKey.TryGetValue(
            Key(
                candidate),
            out plan!);

    private IEnumerable<TradeRouteCandidate> SortedContinuousCandidates()
    {
        IEnumerable<TradeContinuousPlan> plans =
            SortTag() switch
            {
                "profit" =>
                    currentContinuousPlans
                        .OrderByDescending(plan =>
                            plan.TotalProfit)
                        .ThenByDescending(plan =>
                            plan.ConfidenceScore),

                "time" =>
                    currentContinuousPlans
                        .OrderBy(plan =>
                            plan.TotalTime)
                        .ThenByDescending(plan =>
                            plan.TotalProfit),

                "confidence" =>
                    currentContinuousPlans
                        .OrderByDescending(plan =>
                            plan.ConfidenceScore)
                        .ThenByDescending(plan =>
                            plan.RankingProfitPerHour),

                "freshness" =>
                    currentContinuousPlans
                        .OrderBy(plan =>
                            plan.EffectiveWorstDataAge)
                        .ThenByDescending(plan =>
                            plan.RankingProfitPerHour),

                "perton" =>
                    currentContinuousPlans
                        .OrderByDescending(plan =>
                            plan.First.ProfitPerTon)
                        .ThenByDescending(plan =>
                            plan.RankingProfitPerHour),

                "distance" =>
                    currentContinuousPlans
                        .OrderBy(plan =>
                            plan.First.SourceToTargetDistanceLy)
                        .ThenByDescending(plan =>
                            plan.RankingProfitPerHour),

                _ =>
                    currentContinuousPlans
                        .OrderByDescending(plan =>
                            plan.RankingProfitPerHour)
                        .ThenByDescending(plan =>
                            plan.ConfidenceScore)
                        .ThenByDescending(plan =>
                            plan.TotalProfit)
            };

        return
            plans.Select(plan =>
                plan.First);
    }

    private bool TryBuildContinuousRow(
        TradeRouteCandidate candidate,
        bool held,
        out TradeRow row)
    {
        if (!TryGetContinuousPlan(
                candidate,
                out TradeContinuousPlan plan))
        {
            row =
                null!;

            return
                false;
        }

        string nextCommodity =
            plan.Lookahead?.Source.CommodityName.ToUpperInvariant()
            ?? "—";

        string heldLabel =
            held
                ? Loc.Get(
                    "Loc_TRADE_HELD_SELECTION")
                : plan.HasLookahead
                    ? Loc.Format(
                        "Loc_TRADE_CONTINUOUS_ROW_NEXT",
                        nextCommodity)
                    : Loc.Get(
                        "Loc_TRADE_CONTINUOUS_ROW_DEAD_END");

        if (plan.FirstBacktracks
            || plan.LookaheadBacktracks)
        {
            heldLabel +=
                " · "
                + Loc.Get(
                    "Loc_TRADE_CONTINUOUS_BACKTRACK");
        }

        string target =
            $"→ {plan.First.Target.SystemName} / {plan.First.Target.StationName}";

        if (plan.Lookahead is { } second)
        {
            target +=
                $"  |  NEXT → {second.Target.SystemName} / {second.Target.StationName}";
        }

        row =
            new TradeRow(
                candidate,
                Key(
                    candidate),
                heldLabel,
                Loc.Format(
                    "Loc_TRADE_CONFIDENCE_BADGE",
                    ConfidenceLevelName(
                        plan.ConfidenceLevel),
                    plan.ConfidenceScore),
                plan.ConfidenceLevel.ToString(),
                plan.HasLookahead
                    ? Loc.Format(
                        "Loc_TRADE_CONTINUOUS_ROW_COMMODITIES",
                        plan.First.Source.CommodityName.ToUpperInvariant(),
                        nextCommodity)
                    : plan.First.Source.CommodityName.ToUpperInvariant(),
                $"{plan.First.Source.SystemName} / {plan.First.Source.StationName}",
                target,
                Loc.Format(
                    "Loc_TRADE_CONTINUOUS_ROW_PER_TON",
                    plan.First.ProfitPerTon,
                    plan.Lookahead?.ProfitPerTon
                    ?? 0),
                Loc.Format(
                    "Loc_TRADE_CONTINUOUS_ROW_TOTAL",
                    plan.TotalProfit,
                    plan.LegCount),
                Loc.Format(
                    "Loc_TRADE_CONTINUOUS_ROW_DISTANCE",
                    plan.First.SourceToTargetDistanceLy,
                    plan.Lookahead?.SourceToTargetDistanceLy
                    ?? 0),
                FormatTravelTime(
                    plan.TotalTime),
                FormatCreditsPerHour(
                    plan.ProfitPerHour),
                Loc.Format(
                    "Loc_TRADE_ROW_AGE_FORMAT",
                    plan.EffectiveWorstDataAge.TotalHours));

        return
            true;
    }

    private bool TryShowContinuousCandidate(
        TradeRouteCandidate candidate)
    {
        if (!TryGetContinuousPlan(
                candidate,
                out TradeContinuousPlan plan))
        {
            return
                false;
        }

        SelectedCommodityText.Text =
            Loc.Format(
                "Loc_TRADE_CONTINUOUS_DETAIL_TITLE",
                plan.First.Source.CommodityName.ToUpperInvariant());

        SelectedProfitText.Text =
            plan.Lookahead is { } lookahead
                ? Loc.Format(
                    "Loc_TRADE_CONTINUOUS_DETAIL_PROFIT",
                    plan.TotalProfit,
                    plan.First.ProfitPerTrip,
                    lookahead.ProfitPerTrip)
                : Loc.Format(
                    "Loc_TRADE_CONTINUOUS_DETAIL_PROFIT_SINGLE",
                    plan.First.ProfitPerTrip);

        SelectedSourceText.Text =
            $"{plan.First.Source.SystemName}"
            + Environment.NewLine
            + plan.First.Source.StationName;

        SelectedSourceMetaText.Text =
            BuildStationMeta(
                plan.First.Source,
                plan.First.SourceAge);

        SelectedTargetText.Text =
            $"{plan.First.Target.SystemName}"
            + Environment.NewLine
            + plan.First.Target.StationName;

        SelectedTargetMetaText.Text =
            BuildStationMeta(
                plan.First.Target,
                plan.First.TargetAge);

        string firstDemand =
            plan.First.Target.HasInfiniteDemand
                ? "∞"
                : plan.First.Target.Demand.ToString(
                    "N0");

        string economics =
            Loc.Format(
                "Loc_TRADE_CONTINUOUS_DETAIL_FIRST",
                plan.First.Source.CommodityName.ToUpperInvariant(),
                plan.First.Source.BuyFromStationPrice,
                plan.First.Target.SellToStationPrice,
                plan.First.Source.Stock,
                firstDemand,
                plan.First.TradableAmount,
                plan.First.ProfitPerTrip);

        if (plan.Lookahead is { } second)
        {
            string secondDemand =
                second.Target.HasInfiniteDemand
                    ? "∞"
                    : second.Target.Demand.ToString(
                        "N0");

            economics +=
                Environment.NewLine
                + Environment.NewLine
                + Loc.Format(
                    "Loc_TRADE_CONTINUOUS_DETAIL_NEXT",
                    second.Source.CommodityName.ToUpperInvariant(),
                    second.Source.SystemName,
                    second.Source.StationName,
                    second.Target.SystemName,
                    second.Target.StationName,
                    second.Source.BuyFromStationPrice,
                    second.Target.SellToStationPrice,
                    second.Source.Stock,
                    secondDemand,
                    second.TradableAmount,
                    second.ProfitPerTrip);
        }
        else
        {
            economics +=
                Environment.NewLine
                + Environment.NewLine
                + Loc.Get(
                    "Loc_TRADE_CONTINUOUS_DETAIL_NO_NEXT");
        }

        SelectedRouteEconomicsText.Text =
            economics;

        SelectedTravelEstimateText.Text =
            FormatContinuousTravelDetail(
                plan);

        ShowContinuousConfidence(
            plan);

        return
            true;
    }

    private string FormatContinuousTravelDetail(
        TradeContinuousPlan plan)
    {
        string result =
            Loc.Format(
                "Loc_TRADE_CONTINUOUS_TRAVEL_TOTAL",
                plan.LegCount,
                FormatTravelTime(
                    plan.TotalTime),
                plan.TotalProfit,
                FormatCreditsPerHour(
                    plan.ProfitPerHour));

        result +=
            Environment.NewLine
            + Loc.Format(
                "Loc_TRADE_CONTINUOUS_TRAVEL_FIRST",
                FormatTravelTime(
                    plan.FirstTravel.TotalTime),
                plan.FirstTravel.EstimatedJumps);

        if (plan.LookaheadTravel is { } secondTravel)
        {
            result +=
                Environment.NewLine
                + Loc.Format(
                    "Loc_TRADE_CONTINUOUS_TRAVEL_NEXT",
                    FormatTravelTime(
                        secondTravel.TotalTime),
                    secondTravel.EstimatedJumps);
        }

        if (plan.PlanningFactor < 0.999d)
        {
            result +=
                Environment.NewLine
                + Loc.Format(
                    "Loc_TRADE_CONTINUOUS_PLANNING_FACTOR",
                    plan.PlanningFactor * 100d,
                    FormatCreditsPerHour(
                        plan.RankingProfitPerHour));
        }

        return
            result;
    }

    private void ShowContinuousConfidence(
        TradeContinuousPlan plan)
    {
        SelectedConfidencePanel.Visibility =
            Visibility.Visible;

        var lines =
            new List<string>
            {
                Loc.Format(
                    "Loc_TRADE_CONFIDENCE_SCORE",
                    ConfidenceLevelName(
                        plan.ConfidenceLevel),
                    plan.ConfidenceScore),
                Loc.Format(
                    "Loc_TRADE_CONTINUOUS_CONFIDENCE_LEGS",
                    plan.FirstConfidence.Score,
                    plan.LookaheadConfidence?.Score.ToString(
                        CultureInfo.InvariantCulture)
                    ?? "—")
            };

        if (plan.Lookahead is not null)
        {
            lines.Add(
                Loc.Format(
                    "Loc_TRADE_CONTINUOUS_CONFIDENCE_ETA",
                    plan.FirstTravel.TotalTime.TotalMinutes,
                    plan.EffectiveWorstDataAge.TotalHours));
        }

        if (!plan.HasLookahead)
        {
            lines.Add(
                "⚠ "
                + Loc.Get(
                    "Loc_TRADE_CONTINUOUS_DETAIL_NO_NEXT"));
        }

        if (plan.FirstBacktracks
            || plan.LookaheadBacktracks)
        {
            lines.Add(
                "⚠ "
                + Loc.Get(
                    "Loc_TRADE_CONTINUOUS_BACKTRACK_DETAIL"));
        }

        SelectedConfidenceText.Text =
            string.Join(
                Environment.NewLine,
                lines);
    }

    private bool TryRenderContinuousCompact(
        TradeRouteCandidate candidate,
        bool preserveStatus)
    {
        if (!TryGetContinuousPlan(
                candidate,
                out TradeContinuousPlan plan))
        {
            return
                false;
        }

        CompactBestRouteText.Text =
            Loc.Format(
                "Loc_TRADE_CONTINUOUS_COMPACT_BEST",
                plan.First.Source.CommodityName.ToUpperInvariant(),
                plan.First.Target.SystemName,
                plan.TotalProfit,
                plan.LegCount);

        if (plan.Lookahead is { } second)
        {
            CompactBestRouteText.Text +=
                Environment.NewLine
                + Loc.Format(
                    "Loc_TRADE_CONTINUOUS_COMPACT_NEXT",
                    second.Source.CommodityName.ToUpperInvariant(),
                    second.Target.SystemName);
        }

        if (!preserveStatus
            && searchCancellation is null)
        {
            CompactStatusText.Text =
                Loc.Format(
                    "Loc_TRADE_CONTINUOUS_DONE",
                    currentContinuousPlans.Count);
        }

        CompactFooterText.Text =
            Loc.Format(
                "Loc_TRADE_CONTINUOUS_COMPACT_META",
                FormatCreditsPerHour(
                    plan.ProfitPerHour),
                plan.ConfidenceScore,
                plan.PlanningFactor * 100d);

        return
            true;
    }

    private void RefreshContinuousFooter()
    {
        if (currentContinuousPlans.Count == 0)
        {
            FooterText.Text =
                Loc.Get(
                    "Loc_TRADE_CONTINUOUS_IDLE_FOOTER");

            return;
        }

        TradeContinuousPlan best =
            currentContinuousPlans
                .OrderByDescending(plan =>
                    plan.RankingProfitPerHour)
                .First();

        FooterText.Text =
            Loc.Format(
                "Loc_TRADE_CONTINUOUS_FOOTER",
                currentContinuousPlans.Count,
                best.TotalProfit,
                FormatCreditsPerHour(
                    best.ProfitPerHour));
    }

    private void RefreshContinuousCompact(
        bool preserveStatus)
    {
        CompactFiltersText.Text =
            Loc.Format(
                "Loc_TRADE_CONTINUOUS_COMPACT_FILTERS",
                currentJournal.StarSystem,
                currentJournal.Station,
                SelectedInt(
                    TargetRadiusComboBox,
                    80),
                CountActiveAdvancedFilters());

        TradeContinuousPlan? best =
            currentContinuousPlans
                .OrderByDescending(plan =>
                    plan.RankingProfitPerHour)
                .ThenByDescending(plan =>
                    plan.ConfidenceScore)
                .FirstOrDefault();

        if (best is null)
        {
            CompactBestRouteText.Text =
                Loc.Get(
                    "Loc_TRADE_CONTINUOUS_NO_RESULTS");

            if (!preserveStatus
                && searchCancellation is null)
            {
                CompactStatusText.Text =
                    Loc.Get(
                        "Loc_TRADE_CONTINUOUS_READY");
            }

            CompactFooterText.Text =
                Loc.Get(
                    "Loc_TRADE_CONTINUOUS_IDLE_FOOTER");

            return;
        }

        TryRenderContinuousCompact(
            best.First,
            preserveStatus);
    }

    private bool TryBuildContinuousConstraints(
        out TradeSearchConstraints constraints,
        out string error)
    {
        constraints =
            null!;

        error =
            string.Empty;

        if (!currentJournal.JournalAvailable
            || !currentJournal.Docked
            || currentJournal.MarketId is not { } marketId
            || marketId <= 0
            || currentJournal.SystemAddress == 0
            || string.IsNullOrWhiteSpace(
                currentJournal.StarSystem))
        {
            error =
                Loc.Get(
                    "Loc_TRADE_CONTINUOUS_VALIDATION_MARKET");

            return
                false;
        }

        int freeCargo =
            currentJournal.FreeCargo;

        if (freeCargo <= 0)
        {
            error =
                Loc.Get(
                    "Loc_TRADE_VALIDATION_CARGO");

            return
                false;
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
                    freeCargo,
                AvailableCredits =
                    Math.Max(
                        0,
                        currentJournal.Balance),
                DiversifyCandidatePool =
                    true,
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
                    SelectedLong(
                        MinSupplyComboBox,
                        1),
                MinDemand =
                    SelectedLong(
                        MinDemandComboBox,
                        1),
                MaxCommodityCandidates =
                    TradeContinuousSearchService.FirstHopCommodityLimit,
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

    private void ApplyContinuousControlAvailability(
        bool running)
    {
        if (running
            || !IsContinuousMode)
        {
            return;
        }

        AnchorSystemTextBox.IsEnabled =
            false;

        CargoTextBox.IsEnabled =
            false;

        SourceRadiusComboBox.IsEnabled =
            false;

        SyncJournalButton.IsEnabled =
            false;
    }

    private void UpdateContinuousSortLabels()
    {
        if (!IsContinuousMode)
        {
            return;
        }

        PrimaryProfitSortItem.SetResourceReference(
            ContentControl.ContentProperty,
            "Loc_TRADE_CONTINUOUS_SORT_TOTAL");

        PerHourSortItem.SetResourceReference(
            ContentControl.ContentProperty,
            "Loc_TRADE_CONTINUOUS_SORT_RATE");

        PerTonSortItem.SetResourceReference(
            ContentControl.ContentProperty,
            "Loc_TRADE_CONTINUOUS_SORT_FIRST_TON");
    }

    private void ApplyContinuousDefaultSort()
    {
        if (!IsContinuousMode)
        {
            return;
        }

        bool previous =
            applyingJournal;

        applyingJournal =
            true;

        try
        {
            SelectTag(
                SortComboBox,
                "perhour");
        }
        finally
        {
            applyingJournal =
                previous;
        }
    }

    private void PrepareContinuousPin(
        TradeContinuousPlan plan)
    {
        pendingPinnedContinuousPlan =
            plan;
    }

    private void AdoptPinnedContinuation(
        TradeRouteCandidate candidate)
    {
        ResetActiveContinuationPreview(
            keepPlannedLookahead:
                false);

        if (pendingPinnedContinuousPlan is { } pending
            && Key(
                pending.First)
                .Equals(
                    Key(
                        candidate),
                    StringComparison.Ordinal))
        {
            activeContinuousRoute =
                true;

            plannedContinuousLookahead =
                pending.Lookahead;

            RememberContinuousMarket(
                candidate.Source.MarketId);
        }
        else
        {
            activeContinuousRoute =
                false;

            plannedContinuousLookahead =
                null;
        }

        pendingPinnedContinuousPlan =
            null;
    }

    private void DisableContinuousForRoundTrip()
    {
        pendingPinnedContinuousPlan =
            null;

        activeContinuousRoute =
            false;

        plannedContinuousLookahead =
            null;

        ResetActiveContinuationPreview(
            keepPlannedLookahead:
                false);
    }

    private void RememberContinuousMarket(
        long marketId)
    {
        if (marketId <= 0)
        {
            return;
        }

        LinkedListNode<long>? existing =
            recentContinuousMarkets.Find(
                marketId);

        if (existing is not null)
        {
            recentContinuousMarkets.Remove(
                existing);
        }

        recentContinuousMarkets.AddFirst(
            marketId);

        while (recentContinuousMarkets.Count > 3)
        {
            recentContinuousMarkets.RemoveLast();
        }
    }

    private IReadOnlyList<long> RecentContinuousMarketIds() =>
        recentContinuousMarkets.ToArray();

    private void UpdateContinuousPlanningForActiveTrade(
        TradeActiveRouteSession session,
        TradeRouteProgress? execution)
    {
        if (!activeContinuousRoute)
        {
            return;
        }

        bool completed =
            IsActiveTradeCompletedForContinuation();

        if (!completed
            && !session.CargoLoaded
            && execution?.Stage
               != TradeRouteStage.FlyToSell)
        {
            return;
        }

        TradeMarketOrder futureStart =
            session.ActiveLeg.Target;

        if (futureStart.MarketId <= 0)
        {
            return;
        }

        if (completed)
        {
            if (!currentJournal.Docked
                || currentJournal.MarketId
                   != futureStart.MarketId)
            {
                return;
            }

            string actualKey =
                BuildContinuationPreviewKey(
                    futureStart,
                    actual:
                        true,
                    credits:
                        currentJournal.JournalAvailable
                            ? currentJournal.Balance
                            : null,
                    marketUpdate:
                        currentJournal.MarketUpdatedUtc);

            if (continuationPreviewFinalized
                && continuationPreviewRequestKey
                   == actualKey)
            {
                return;
            }

            StartContinuationPreview(
                futureStart,
                currentJournal.JournalAvailable
                    ? Math.Max(
                        0,
                        currentJournal.Balance)
                    : null,
                actualKey,
                finalized:
                    true);

            return;
        }

        long? predictedCredits =
            PredictCreditsAtActiveTarget(
                session,
                execution);

        string previewKey =
            BuildContinuationPreviewKey(
                futureStart,
                actual:
                    false,
                credits:
                    predictedCredits,
                marketUpdate:
                    null);

        if (continuationPreviewRequestKey
            == previewKey)
        {
            return;
        }

        StartContinuationPreview(
            futureStart,
            predictedCredits,
            previewKey,
            finalized:
                false);
    }

    private long? PredictCreditsAtActiveTarget(
        TradeActiveRouteSession session,
        TradeRouteProgress? execution)
    {
        if (!currentJournal.JournalAvailable)
        {
            return
                null;
        }

        long credits =
            Math.Max(
                0,
                currentJournal.Balance);

        int amount =
            execution?.RemainingQuantity
            is > 0
                ? execution.RemainingQuantity
                : session.ActualCargoCount > 0
                    ? session.ActualCargoCount
                    : session.ActiveLeg.TradableAmount;

        int sellPrice =
            session.EffectiveSellPrice;

        if (amount <= 0
            || sellPrice <= 0)
        {
            return
                credits;
        }

        return
            Math.Max(
                0,
                checked(
                    credits
                    + (long)amount
                      * sellPrice));
    }

    private void StartContinuationPreview(
        TradeMarketOrder startMarket,
        long? startingCredits,
        string requestKey,
        bool finalized)
    {
        continuationPreviewCancellation?.Cancel();
        continuationPreviewCancellation?.Dispose();

        var cancellation =
            new CancellationTokenSource();

        continuationPreviewCancellation =
            cancellation;

        continuationPreviewRequestKey =
            requestKey;

        continuationPreviewRefreshing =
            true;

        if (finalized)
        {
            // Do not present the in-flight preview as if it were already
            // validated against the actual arrival market.
            activeContinuationOptions =
                Array.Empty<TradeContinuousPlan>();

            activeContinuationPreview =
                null;
        }
        else
        {
            continuationPreviewFinalized =
                false;
        }

        _ =
            RefreshContinuationPreviewAsync(
                startMarket,
                startingCredits,
                requestKey,
                finalized,
                cancellation);
    }

    private async Task RefreshContinuationPreviewAsync(
        TradeMarketOrder startMarket,
        long? startingCredits,
        string requestKey,
        bool finalized,
        CancellationTokenSource cancellation)
    {
        try
        {
            TradeSearchConstraints constraints =
                BuildFutureContinuousConstraints(
                    startMarket,
                    startingCredits);

            TradeContinuousSearchRequest request =
                new()
                {
                    StartSystem =
                        new TradeSystemReference(
                            startMarket.SystemName,
                            startMarket.SystemAddress),
                    KnownStartLocation =
                        new TradeSystemLocation(
                            startMarket.SystemAddress,
                            startMarket.SystemName,
                            startMarket.SystemX,
                            startMarket.SystemY,
                            startMarket.SystemZ),
                    StartMarketId =
                        startMarket.MarketId,
                    Constraints =
                        constraints,
                    Ship =
                        currentJournal,
                    RecentMarketIds =
                        RecentContinuousMarketIds()
                };

            IReadOnlyList<TradeContinuousPlan> plans =
                await continuousSearchService.SearchAsync(
                        request,
                        progress:
                            null,
                        cancellationToken:
                            cancellation.Token)
                    .ConfigureAwait(true);

            if (!ReferenceEquals(
                    continuationPreviewCancellation,
                    cancellation)
                || cancellation.IsCancellationRequested
                || continuationPreviewRequestKey
                   != requestKey)
            {
                return;
            }

            activeContinuationOptions =
                plans;

            activeContinuationPreview =
                plans.FirstOrDefault();

            continuationPreviewFinalized =
                finalized;

            RefreshActiveTradeCompact();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"Continuous background preview failed: {ex.Message}");

            if (ReferenceEquals(
                    continuationPreviewCancellation,
                    cancellation))
            {
                activeContinuationOptions =
                    Array.Empty<TradeContinuousPlan>();

                activeContinuationPreview =
                    null;
            }
        }
        finally
        {
            if (ReferenceEquals(
                    continuationPreviewCancellation,
                    cancellation))
            {
                continuationPreviewRefreshing =
                    false;

                RefreshActiveTradeCompact();
            }
        }
    }

    private TradeSearchConstraints BuildFutureContinuousConstraints(
        TradeMarketOrder startMarket,
        long? credits)
    {
        TradeSearchConstraints basis =
            lastSearchConstraints
            ?? BuildFallbackActiveConstraints(
                Math.Max(
                    1,
                    currentJournal.CargoCapacity));

        int cargoCapacity =
            currentJournal.CargoCapacity > 0
                ? currentJournal.CargoCapacity
                : Math.Max(
                    1,
                    basis.CargoCapacity);

        return basis with
        {
            OriginSystemName =
                startMarket.SystemName,
            OriginSystemAddress =
                startMarket.SystemAddress,
            CargoCapacity =
                cargoCapacity,
            AvailableCredits =
                credits,
            SourceSearchRadiusLy =
                0,
            DiversifyCandidatePool =
                true
        };
    }

    private static string BuildContinuationPreviewKey(
        TradeMarketOrder market,
        bool actual,
        long? credits,
        DateTimeOffset? marketUpdate) =>
        $"{market.MarketId}:"
        + $"{actual}:"
        + $"{credits ?? -1}:"
        + $"{marketUpdate?.UtcDateTime.Ticks ?? 0}";

    private bool IsActiveTradeCompletedForContinuation() =>
        activeTradeSession is not null
        && (executionProgress?.Stage
            == TradeRouteStage.Completed
            || activeTradeSession.IsCompleted);

    private bool TryRenderContinuousCompletion(
        TradeActiveRouteSession session,
        TradeRouteProgress? execution)
    {
        if (!activeContinuousRoute
            || !IsActiveTradeCompletedForContinuation())
        {
            return
                false;
        }

        CompactFiltersText.Text =
            Loc.Get(
                "Loc_TRADE_CONTINUOUS_COMPLETE_HEADER");

        CompactStatusText.Text =
            execution is not null
                ? Loc.Format(
                    "Loc_TRADE_EXEC_COMPLETED",
                    execution.ActualProfit,
                    execution.PlannedProfit,
                    execution.ProjectedVariancePercent)
                : Loc.Get(
                    "Loc_TRADE_ACTIVE_COMPLETED");

        if (activeContinuationPreview is { } plan)
        {
            CompactBestRouteText.Text =
                Loc.Format(
                    "Loc_TRADE_CONTINUOUS_COMPLETE_NEXT",
                    plan.First.Source.CommodityName.ToUpperInvariant(),
                    plan.First.Target.SystemName,
                    plan.First.Target.StationName,
                    plan.First.ProfitPerTrip);

            if (plan.Lookahead is { } second)
            {
                CompactBestRouteText.Text +=
                    Environment.NewLine
                    + Loc.Format(
                        "Loc_TRADE_CONTINUOUS_COMPACT_NEXT",
                        second.Source.CommodityName.ToUpperInvariant(),
                        second.Target.SystemName);
            }

            CompactFooterText.Text =
                Loc.Format(
                    "Loc_TRADE_CONTINUOUS_COMPLETE_META",
                    plan.TotalProfit,
                    FormatTravelTime(
                        plan.TotalTime),
                    FormatCreditsPerHour(
                        plan.ProfitPerHour),
                    plan.ConfidenceScore);

            return
                true;
        }

        CompactBestRouteText.Text =
            continuationPreviewRefreshing
                ? Loc.Get(
                    "Loc_TRADE_CONTINUOUS_BACKGROUND_SEARCH")
                : Loc.Get(
                    "Loc_TRADE_CONTINUOUS_NO_RESULTS");

        CompactFooterText.Text =
            continuationPreviewRefreshing
                ? Loc.Get(
                    "Loc_TRADE_CONTINUOUS_BACKGROUND_HINT")
                : Loc.Get(
                    "Loc_TRADE_CONTINUOUS_COMPLETE_NO_NEXT");

        return
            true;
    }

    private void AppendContinuousPreviewToActiveHud()
    {
        if (!activeContinuousRoute
            || activeTradeSession is null
            || IsActiveTradeCompletedForContinuation())
        {
            return;
        }

        if (activeContinuationPreview is { } plan)
        {
            CompactFooterText.Text +=
                Environment.NewLine
                + Loc.Format(
                    "Loc_TRADE_CONTINUOUS_ACTIVE_PREVIEW",
                    plan.First.Source.CommodityName.ToUpperInvariant(),
                    plan.First.Target.SystemName,
                    FormatCreditsPerHour(
                        plan.ProfitPerHour));

            return;
        }

        if (plannedContinuousLookahead is { } planned)
        {
            CompactFooterText.Text +=
                Environment.NewLine
                + Loc.Format(
                    "Loc_TRADE_CONTINUOUS_ACTIVE_PLANNED",
                    planned.Source.CommodityName.ToUpperInvariant(),
                    planned.Target.SystemName);

            return;
        }

        if (continuationPreviewRefreshing)
        {
            CompactFooterText.Text +=
                Environment.NewLine
                + Loc.Get(
                    "Loc_TRADE_CONTINUOUS_BACKGROUND_SEARCH");
        }
    }

    private async Task HandleCompletedContinuousActionAsync()
    {
        if (activeContinuationPreview is { } plan)
        {
            PrepareContinuousPin(
                plan);

            PinRequested?.Invoke(
                plan.First);

            return;
        }

        if (activeTradeSession is null)
        {
            return;
        }

        TradeMarketOrder start =
            activeTradeSession.ActiveLeg.Target;

        string key =
            BuildContinuationPreviewKey(
                start,
                actual:
                    true,
                credits:
                    currentJournal.JournalAvailable
                        ? currentJournal.Balance
                        : null,
                marketUpdate:
                    currentJournal.MarketUpdatedUtc);

        StartContinuationPreview(
            start,
            currentJournal.JournalAvailable
                ? Math.Max(
                    0,
                    currentJournal.Balance)
                : null,
            key,
            finalized:
                true);

        if (continuationPreviewCancellation is { } cancellation)
        {
            await WaitForContinuationPreviewAsync(
                cancellation.Token);
        }
    }

    private async Task WaitForContinuationPreviewAsync(
        CancellationToken cancellationToken)
    {
        while (continuationPreviewRefreshing)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(
                    50,
                    cancellationToken)
                .ConfigureAwait(true);
        }
    }

    private void ShowContinuationOptionsFull()
    {
        bool previous =
            applyingJournal;

        applyingJournal =
            true;

        try
        {
            SelectTag(
                RouteModeComboBox,
                "continuous");

            SelectTag(
                SortComboBox,
                "perhour");
        }
        finally
        {
            applyingJournal =
                previous;
        }

        UpdateRouteModeUi();

        if (activeContinuationOptions.Count > 0)
        {
            ApplyContinuousPlans(
                activeContinuationOptions);

            SearchStatusText.Text =
                Loc.Format(
                    "Loc_TRADE_CONTINUOUS_DONE",
                    activeContinuationOptions.Count);
        }

        SetFullMode(
            true);
    }

    private void UpdateContinuousCompletionButtons()
    {
        CompactSellCargoButton.Visibility =
            Visibility.Visible;

        CompactSellCargoButton.IsEnabled =
            true;

        CompactSellCargoButton.SetResourceReference(
            ContentControl.ContentProperty,
            "Loc_TRADE_CONTINUOUS_STOP");

        CompactActionButton.IsEnabled =
            !continuationPreviewRefreshing
            || activeContinuationPreview is not null;

        CompactActionButton.SetResourceReference(
            ContentControl.ContentProperty,
            activeContinuationPreview is not null
                ? "Loc_TRADE_CONTINUOUS_CONTINUE"
                : continuationPreviewRefreshing
                    ? "Loc_TRADE_ACTIVE_REROUTING_BUTTON"
                    : "Loc_TRADE_CONTINUOUS_RETRY");

        CompactSecondaryButton.SetResourceReference(
            ContentControl.ContentProperty,
            "Loc_TRADE_MORE");
    }

    private void StopContinuousTrade()
    {
        activeContinuousRoute =
            false;

        pendingPinnedContinuousPlan =
            null;

        plannedContinuousLookahead =
            null;

        ResetActiveContinuationPreview(
            keepPlannedLookahead:
                false);

        ClearActiveTradeRoute(
            notifyHost:
                true);
    }

    private void ResetActiveContinuationPreview(
        bool keepPlannedLookahead)
    {
        continuationPreviewCancellation?.Cancel();
        continuationPreviewCancellation?.Dispose();

        continuationPreviewCancellation =
            null;

        activeContinuationOptions =
            Array.Empty<TradeContinuousPlan>();

        activeContinuationPreview =
            null;

        continuationPreviewRefreshing =
            false;

        continuationPreviewFinalized =
            false;

        continuationPreviewRequestKey =
            string.Empty;

        if (!keepPlannedLookahead)
        {
            plannedContinuousLookahead =
                null;
        }
    }

    private void DisposeContinuousPlanning()
    {
        ResetActiveContinuationPreview(
            keepPlannedLookahead:
                false);
    }
}
