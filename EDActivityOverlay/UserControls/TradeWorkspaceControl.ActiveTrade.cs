using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.UserControls;

public partial class TradeWorkspaceControl
{
    private readonly TradeActiveRouteRerouteService activeRouteRerouteService =
        new();

    private TradeActiveRouteSession? activeTradeSession;
    private TradeRouteProgressTracker? executionTracker;
    private TradeRouteProgress? executionProgress;
    private TradeSearchConstraints? lastSearchConstraints;
    private bool rerouteRunning;

    public bool HasActiveTradeRoute =>
        activeTradeSession is not null;

    public event Action<TradeRouteCandidate>? ReroutePinUpdateRequested;
    public event Action? UnpinRequested;

    public void ActivatePinnedRoute(
        TradeRouteCandidate candidate,
        TradeRouteProgressTracker? tracker = null)
    {
        TradeSearchConstraints constraints =
            lastSearchConstraints
            ?? BuildFallbackActiveConstraints(
                candidate.TradableAmount);

        activeTradeSession =
            new TradeActiveRouteSession(
                candidate,
                constraints,
                currentJournal);

        selectedCandidate =
            candidate;

        ResetCargoSaleResults();

        AdoptPinnedContinuation(
            candidate);

        AttachExecutionTracker(
            tracker);

        SetFullMode(
            false);

        RefreshActiveTradeCompact();
    }
    public void ActivatePinnedRoute(
        TradeRoundTripCandidate candidate,
        TradeRouteProgressTracker? tracker = null)
    {
        TradeSearchConstraints constraints =
            lastSearchConstraints
            ?? BuildFallbackActiveConstraints(
                Math.Max(
                    candidate.Outbound.TradableAmount,
                    candidate.ReturnTradableAmount));

        activeTradeSession =
            new TradeActiveRouteSession(
                candidate,
                constraints,
                currentJournal);

        selectedCandidate =
            candidate.Outbound;

        ResetCargoSaleResults();

        DisableContinuousForRoundTrip();

        AttachExecutionTracker(
            tracker);

        SetFullMode(
            false);

        RefreshActiveTradeCompact();
    }

    public void AttachExecutionTracker(
        TradeRouteProgressTracker? tracker)
    {
        if (ReferenceEquals(
                executionTracker,
                tracker))
        {
            if (tracker is not null)
            {
                ApplyExecutionProgress(
                    tracker.Current);
            }

            return;
        }

        DetachExecutionTracker();

        executionTracker =
            tracker;

        if (executionTracker is null)
        {
            executionProgress =
                null;

            return;
        }

        executionTracker.ProgressChanged +=
            ExecutionTracker_ProgressChanged;

        ApplyExecutionProgress(
            executionTracker.Current);
    }

    private void DetachExecutionTracker()
    {
        if (executionTracker is not null)
        {
            executionTracker.ProgressChanged -=
                ExecutionTracker_ProgressChanged;
        }

        executionTracker =
            null;

        executionProgress =
            null;
    }

    private void ExecutionTracker_ProgressChanged(
        object? sender,
        TradeRouteProgressChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyExecutionProgress(
                e.Progress);

            RefreshActiveTradeCompact();

            return;
        }

        Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    ApplyExecutionProgress(
                        e.Progress);

                    RefreshActiveTradeCompact();
                }));
    }

    private void ApplyExecutionProgress(
        TradeRouteProgress progress)
    {
        executionProgress =
            progress;

        activeTradeSession?
            .SetExecutedAverageBuyPrice(
                progress.AverageBuyPrice > 0
                    ? progress.AverageBuyPrice
                    : null);
    }
    public void ClearActiveTradeRouteFromHost()
    {
        DisableContinuousForRoundTrip();
        DetachExecutionTracker();

        activeTradeSession =
            null;

        rerouteRunning =
            false;

        RefreshCompactPresentation();
        UpdateCompactModeButtons();
    }

    private void RememberSearchConstraints(
        TradeSearchConstraints constraints)
    {
        lastSearchConstraints =
            constraints;
    }

    private void RefreshActiveTradeState(
        GameStateSnapshot state)
    {
        CompactSellCargoButton.IsEnabled =
            HasCurrentCargo(
                state);

        if (activeTradeSession is null)
        {
            UpdateCompactModeButtons();

            return;
        }

        activeTradeSession.Update(
            state);

        UpdateContinuousPlanningForActiveTrade(
            activeTradeSession,
            executionProgress);

        RefreshActiveTradeCompact();
    }

    private void RefreshActiveTradeCompact()
    {
        if (activeTradeSession is null)
        {
            return;
        }

        TradeActiveRouteSession session =
            activeTradeSession;

        TradeRouteCandidate leg =
            session.ActiveLeg;

        TradeRouteProgress? execution =
            executionProgress;

        if (TryRenderContinuousCompletion(
                session,
                execution))
        {
            UpdateCompactModeButtons();
            return;
        }

        string legBadge =
            session.IsReturnLeg
                ? Loc.Get(
                    "Loc_TRADE_ACTIVE_RETURN_LEG")
                : Loc.Get(
                    "Loc_TRADE_ACTIVE_OUTBOUND_LEG");

        string stage =
            execution is null
                ? StageLabel(
                    session.Stage)
                : StageLabel(
                    execution.Stage);

        int quantity =
            execution is { PlannedQuantity: > 0 }
                ? execution.PlannedQuantity
                : leg.TradableAmount;

        CompactFiltersText.Text =
            Loc.Format(
                "Loc_TRADE_ACTIVE_HEADER",
                legBadge,
                stage);

        CompactBestRouteText.Text =
            Loc.Format(
                "Loc_TRADE_ACTIVE_ROUTE",
                leg.Source.CommodityName.ToUpperInvariant(),
                quantity,
                leg.Source.SystemName,
                leg.Source.StationName,
                leg.Target.SystemName,
                leg.Target.StationName);

        CompactStatusText.Text =
            BuildActiveStatus(
                session,
                execution);

        CompactFooterText.Text =
            BuildActiveFooter(
                session,
                execution);

        AppendContinuousPreviewToActiveHud();

        UpdateCompactModeButtons();
    }
    private string BuildActiveStatus(
        TradeActiveRouteSession session,
        TradeRouteProgress? execution)
    {
        TradeRouteCandidate leg =
            session.ActiveLeg;

        if (execution?.Stage
            == TradeRouteStage.Completed)
        {
            return Loc.Format(
                "Loc_TRADE_EXEC_COMPLETED",
                execution.ActualProfit,
                execution.PlannedProfit,
                execution.ProjectedVariancePercent);
        }

        if (execution is not null
            && execution.PurchasedQuantity > 0
            && execution.Stage
               == TradeRouteStage.Buy)
        {
            return Loc.Format(
                "Loc_TRADE_EXEC_BUY_PROGRESS",
                execution.PurchasedQuantity,
                execution.PlannedQuantity,
                execution.AverageBuyPrice,
                execution.PurchaseCost);
        }

        if (execution is not null
            && execution.PurchasedQuantity > 0
            && execution.Stage
               == TradeRouteStage.FlyToSell)
        {
            return Loc.Format(
                "Loc_TRADE_EXEC_LOADED",
                execution.RemainingQuantity,
                execution.AverageBuyPrice,
                execution.ProjectedProfit);
        }

        if (execution is not null
            && execution.SoldQuantity > 0
            && execution.Stage
               == TradeRouteStage.Sell)
        {
            string sold =
                Loc.Format(
                    "Loc_TRADE_EXEC_SELL_PROGRESS",
                    execution.SoldQuantity,
                    Math.Max(
                        execution.PurchasedQuantity,
                        execution.PlannedQuantity),
                    execution.AverageSellPrice,
                    execution.SaleRevenue,
                    execution.ActualProfit);

            if (session.ShouldOfferReroute)
            {
                sold +=
                    Environment.NewLine
                    + Loc.Get(
                        "Loc_TRADE_ACTIVE_TARGET_DEGRADED");
            }

            return sold;
        }

        if (session.Stage
            == TradeActiveStage.Completed)
        {
            return Loc.Get(
                "Loc_TRADE_ACTIVE_COMPLETED");
        }

        if (session.SourceMarketOpen)
        {
            if (session.LiveSourceMarket
                is null)
            {
                return Loc.Get(
                    "Loc_TRADE_ACTIVE_SOURCE_MISSING");
            }

            return Loc.Format(
                "Loc_TRADE_ACTIVE_SOURCE_LIVE",
                leg.Source.BuyFromStationPrice,
                session.LiveSourceMarket.BuyPrice,
                session.LiveSourceMarket.Supply);
        }

        if (session.TargetMarketOpen)
        {
            if (session.LiveTargetMarket
                is null)
            {
                return Loc.Get(
                    "Loc_TRADE_ACTIVE_TARGET_MISSING");
            }

            string demand =
                session.LiveTargetMarket.Demand > 0
                    ? session.LiveTargetMarket.Demand.ToString(
                        "N0")
                    : "∞";

            string live =
                Loc.Format(
                    "Loc_TRADE_ACTIVE_TARGET_LIVE",
                    leg.Target.SellToStationPrice,
                    session.LiveTargetMarket.SellPrice,
                    demand);

            if (session.ShouldOfferReroute)
            {
                return live
                       + Environment.NewLine
                       + Loc.Get(
                           "Loc_TRADE_ACTIVE_TARGET_DEGRADED");
            }

            return live;
        }

        if (session.CargoLoaded)
        {
            return Loc.Format(
                "Loc_TRADE_ACTIVE_IN_CARGO",
                session.ActualCargoCount);
        }

        return session.Stage switch
        {
            TradeActiveStage.AtSource =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_AT_SOURCE"),
            TradeActiveStage.TravellingToTarget =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_TO_TARGET"),
            _ =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_TO_SOURCE")
        };
    }
    private string BuildActiveFooter(
        TradeActiveRouteSession session,
        TradeRouteProgress? execution)
    {
        TradeRouteCandidate leg =
            session.ActiveLeg;

        bool loaded =
            session.CargoLoaded;

        double distance =
            session.RemainingDistanceLy(
                currentJournal);

        int cargoTons =
            loaded
                ? Math.Max(
                    0,
                    session.ActualCargoCount)
                : 0;

        double? arrival =
            loaded
                ? leg.Target.DistanceToArrivalLs
                : leg.Source.DistanceToArrivalLs;

        TradeLegTravelEstimate travel =
            travelTimeEstimator.EstimateLeg(
                distance,
                cargoTons,
                arrival,
                currentJournal);

        string destination =
            loaded
                ? Loc.Get(
                    "Loc_TRADE_ACTIVE_DEST_BUYER")
                : Loc.Get(
                    "Loc_TRADE_ACTIVE_DEST_SUPPLIER");

        if (execution is not null
            && execution.HasTransactions)
        {
            return Loc.Format(
                "Loc_TRADE_EXEC_FOOTER",
                destination,
                distance,
                FormatTravelTime(
                    travel.TotalTime),
                travel.EstimatedJumps,
                execution.ActualProfit,
                execution.ProjectedProfit);
        }

        return Loc.Format(
            "Loc_TRADE_ACTIVE_FOOTER",
            destination,
            distance,
            FormatTravelTime(
                travel.TotalTime),
            travel.EstimatedJumps,
            session.ExpectedProfit);
    }
    private async void CompactActionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (activeTradeSession is null)
        {
            await StartOrCancelSearchAsync();

            return;
        }

        if (activeContinuousRoute
            && IsActiveTradeCompletedForContinuation())
        {
            await HandleCompletedContinuousActionAsync();

            return;
        }

        if (activeTradeSession.ShouldOfferReroute)
        {
            await RerouteActiveTradeAsync();

            return;
        }

        ClearActiveTradeRoute(
            notifyHost: true);
    }

    private void CompactSecondaryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (activeContinuousRoute
            && IsActiveTradeCompletedForContinuation())
        {
            ShowContinuationOptionsFull();
            return;
        }

        if (activeTradeSession is not null
            && activeTradeSession.ShouldOfferReroute)
        {
            activeTradeSession.AcknowledgeTargetDegradation();

            RefreshActiveTradeCompact();

            return;
        }

        SetFullMode(
            true);
    }

    private async void CompactSellCargoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (activeContinuousRoute
            && IsActiveTradeCompletedForContinuation())
        {
            StopContinuousTrade();
            return;
        }

        if (activeTradeSession is not null
            || !HasCurrentCargo(
                currentJournal))
        {
            return;
        }

        applyingJournal =
            true;

        try
        {
            SelectTag(
                RouteModeComboBox,
                "cargo");
        }
        finally
        {
            applyingJournal =
                false;
        }

        UpdateRouteModeUi();
        CaptureSession();

        await StartOrCancelSearchAsync();
    }

    private async Task RerouteActiveTradeAsync()
    {
        if (activeTradeSession is null
            || rerouteRunning)
        {
            return;
        }

        rerouteRunning =
            true;

        UpdateCompactModeButtons();

        CompactStatusText.Text =
            Loc.Get(
                "Loc_TRADE_ACTIVE_REROUTING");

        try
        {
            TradeRouteCandidate? rerouted =
                await activeRouteRerouteService.FindBetterBuyerAsync(
                        currentJournal,
                        activeTradeSession)
                    .ConfigureAwait(true);

            if (rerouted is null)
            {
                activeTradeSession.AcknowledgeTargetDegradation();

                CompactStatusText.Text =
                    Loc.Get(
                        "Loc_TRADE_ACTIVE_NO_BETTER_BUYER");

                return;
            }

            activeTradeSession.ApplyReroute(
                rerouted,
                currentJournal);

            if (activeContinuousRoute)
            {
                ResetActiveContinuationPreview(
                    keepPlannedLookahead:
                        false);

                UpdateContinuousPlanningForActiveTrade(
                    activeTradeSession,
                    executionProgress);
            }

            selectedCandidate =
                rerouted;

            ReroutePinUpdateRequested?.Invoke(
                rerouted);

            RefreshActiveTradeCompact();
        }
        catch (Exception ex)
        {
            Logger.Logger.Error(
                $"Trade active reroute failed: {ex}");

            CompactStatusText.Text =
                Loc.Format(
                    "Loc_TRADE_SEARCH_ERROR",
                    ex.Message);
        }
        finally
        {
            rerouteRunning =
                false;

            UpdateCompactModeButtons();
        }
    }

    private void ClearActiveTradeRoute(
        bool notifyHost)
    {
        DisableContinuousForRoundTrip();
        DetachExecutionTracker();

        activeTradeSession =
            null;

        rerouteRunning =
            false;

        if (notifyHost)
        {
            UnpinRequested?.Invoke();
        }

        RefreshCompactPresentation();
        UpdateCompactModeButtons();
    }

    private void UpdateCompactModeButtons()
    {
        bool active =
            activeTradeSession is not null;

        CompactSellCargoButton.Visibility =
            active
                ? Visibility.Collapsed
                : Visibility.Visible;

        CompactSellCargoButton.IsEnabled =
            !active
            && !rerouteRunning
            && HasCurrentCargo(
                currentJournal);

        if (active
            && activeContinuousRoute
            && IsActiveTradeCompletedForContinuation())
        {
            UpdateContinuousCompletionButtons();
            return;
        }

        if (!active)
        {
            CompactActionButton.IsEnabled =
                true;

            CompactActionButton.SetResourceReference(
                ContentControl.ContentProperty,
                searchCancellation is not null
                    ? "Loc_TRADE_CANCEL"
                    : SearchIdleResourceKey());

            CompactSecondaryButton.SetResourceReference(
                ContentControl.ContentProperty,
                "Loc_TRADE_MORE");

            return;
        }

        CompactActionButton.IsEnabled =
            !rerouteRunning;

        if (activeTradeSession!.ShouldOfferReroute)
        {
            CompactActionButton.SetResourceReference(
                ContentControl.ContentProperty,
                rerouteRunning
                    ? "Loc_TRADE_ACTIVE_REROUTING_BUTTON"
                    : "Loc_TRADE_ACTIVE_REROUTE");

            CompactSecondaryButton.SetResourceReference(
                ContentControl.ContentProperty,
                "Loc_TRADE_ACTIVE_KEEP_ROUTE");
        }
        else
        {
            CompactActionButton.SetResourceReference(
                ContentControl.ContentProperty,
                "Loc_TRADE_ACTIVE_CANCEL_ROUTE");

            CompactSecondaryButton.SetResourceReference(
                ContentControl.ContentProperty,
                "Loc_TRADE_ACTIVE_SEARCH");
        }
    }

    private TradeSearchConstraints BuildFallbackActiveConstraints(
        int cargo) =>
        new()
        {
            OriginSystemName =
                string.IsNullOrWhiteSpace(
                    currentJournal.StarSystem)
                    ? "Unknown"
                    : currentJournal.StarSystem,
            OriginSystemAddress =
                currentJournal.SystemAddress,
            CargoCapacity =
                Math.Max(
                    1,
                    cargo),
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
                SelectedInt(
                    MaxStationDistanceComboBox,
                    0)
                is > 0 and var distance
                    ? distance
                    : null,
            IncludeFleetCarriers =
                FleetCarriersCheckBox.IsChecked
                == true,
            MinSupply =
                1,
            MinDemand =
                1,
            MaxCommodityCandidates =
                1,
            MaxResults =
                SearchResultPoolSize,
            MaxConcurrentCommoditySearches =
                4
        };

    private static bool HasCurrentCargo(
        GameStateSnapshot state) =>
        state.CargoByCommodityId
            .Values
            .Any(item =>
                item.Count > 0);

    private static string StageLabel(
        TradeRouteStage stage) =>
        stage switch
        {
            TradeRouteStage.Buy =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_STAGE_SOURCE"),
            TradeRouteStage.FlyToSell =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_STAGE_TO_TARGET"),
            TradeRouteStage.Sell =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_STAGE_TARGET"),
            TradeRouteStage.Completed =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_STAGE_DONE"),
            _ =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_STAGE_TO_SOURCE")
        };

    private static string StageLabel(
        TradeActiveStage stage) =>
        stage switch
        {
            TradeActiveStage.AtSource =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_STAGE_SOURCE"),
            TradeActiveStage.TravellingToTarget =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_STAGE_TO_TARGET"),
            TradeActiveStage.AtTarget =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_STAGE_TARGET"),
            TradeActiveStage.Completed =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_STAGE_DONE"),
            _ =>
                Loc.Get(
                    "Loc_TRADE_ACTIVE_STAGE_TO_SOURCE")
        };
}
