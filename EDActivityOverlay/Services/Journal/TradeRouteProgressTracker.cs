using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Models.Trading;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.Services.Journal;

public enum TradeRouteStage
{
    FlyToBuy,
    Buy,
    FlyToSell,
    Sell,
    Completed
}

public sealed record TradeRouteProgress
{
    public TradeRouteStage Stage { get; init; }
    public int LegNumber { get; init; } = 1;
    public int LegCount { get; init; } = 1;
    public string Action { get; init; } = string.Empty;
    public string System { get; init; } = string.Empty;
    public string Station { get; init; } = string.Empty;
    public string Commodity { get; init; } = string.Empty;
    public string CommodityId { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public int PlannedQuantity { get; init; }
    public int PurchasedQuantity { get; init; }
    public int SoldQuantity { get; init; }
    public int RemainingQuantity { get; init; }
    public int PlannedPrice { get; init; }
    public int? CurrentMarketPrice { get; init; }
    public int AverageBuyPrice { get; init; }
    public int AverageSellPrice { get; init; }
    public long PurchaseCost { get; init; }
    public long SaleRevenue { get; init; }
    public long ActualProfit { get; init; }
    public long ProjectedProfit { get; init; }
    public long PlannedProfit { get; init; }
    public double ProjectedVariancePercent { get; init; }
    public bool HasTransactions { get; init; }
    public int RemainingJumps { get; init; }
    public bool IsInDanger { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed class TradeRouteProgressChangedEventArgs(
    TradeRouteProgress progress) : EventArgs
{
    public TradeRouteProgress Progress { get; } =
        progress;
}

public sealed class TradeRouteProgressTracker : IDisposable
{
    private readonly JournalMonitorService journal;

    private TradeRoute route;
    private RouteLeg[] legs;
    private TradeExecutionLedger execution;
    private int currentLegIndex;
    private int baselineCargoCount;
    private long completedPurchaseCost;
    private long completedSaleRevenue;
    private long completedRealizedProfit;
    private readonly List<TradeHistoryLegRecord> completedHistoryLegs =
        new();
    private readonly Dictionary<string, int> cargoSaleSold =
        new(
            StringComparer.OrdinalIgnoreCase);
    private long cargoSaleRevenue;
    private DateTimeOffset routeStartedUtc;
    private DateTimeOffset legStartedUtc;
    private DateTimeOffset? completedUtc;
    private long initialPlannedProfit;
    private int rerouteCount;
    private bool historyRecorded;
    private bool completed;
    private bool disposed;

    public event EventHandler<TradeRouteProgressChangedEventArgs>? ProgressChanged;

    public TradeRouteProgress Current { get; private set; } =
        new();

    public TradeRouteProgressTracker(
        TradeRoute route,
        JournalMonitorService? journal = null)
    {
        this.route =
            route
            ?? throw new ArgumentNullException(
                nameof(route));

        this.journal =
            journal
            ?? JournalMonitorService.Instance;

        routeStartedUtc =
            DateTimeOffset.UtcNow;

        legStartedUtc =
            routeStartedUtc;

        initialPlannedProfit =
            route.TotalProfitPerTrip;

        legs =
            BuildLegs(
                route);

        execution =
            new TradeExecutionLedger(
                legs[0].PlannedQuantity);

        baselineCargoCount =
            FindCargoCount(
                this.journal.Current,
                legs[0].CommodityId);

        this.journal.StateChanged +=
            OnStateChanged;

        this.journal.JournalEventReceived +=
            OnJournalEvent;

        Refresh(
            this.journal.Current);
    }

    public void UpdateRoute(
        TradeRoute updatedRoute,
        bool preserveExecution)
    {
        ArgumentNullException.ThrowIfNull(
            updatedRoute);

        RouteLeg[] updatedLegs =
            BuildLegs(
                updatedRoute);

        if (!preserveExecution)
        {
            route =
                updatedRoute;

            legs =
                updatedLegs;

            currentLegIndex =
                0;

            completedPurchaseCost =
                0;

            completedSaleRevenue =
                0;

            completedRealizedProfit =
                0;

            completedHistoryLegs.Clear();

            routeStartedUtc =
                DateTimeOffset.UtcNow;

            legStartedUtc =
                routeStartedUtc;

            completedUtc =
                null;

            initialPlannedProfit =
                updatedRoute.TotalProfitPerTrip;

            rerouteCount =
                0;

            historyRecorded =
                false;

            completed =
                false;

            execution =
                new TradeExecutionLedger(
                    legs[0].PlannedQuantity);

            baselineCargoCount =
                FindCargoCount(
                    journal.Current,
                    legs[0].CommodityId);

            Refresh(
                journal.Current);

            return;
        }

        string currentCommodity =
            currentLegIndex < legs.Length
                ? legs[currentLegIndex].CommodityId
                : Current.CommodityId;

        string updatedCommodity =
            updatedLegs[0].CommodityId;

        if (!string.Equals(
                currentCommodity,
                updatedCommodity,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Cannot preserve trade execution across a reroute with a different commodity.");
        }

        rerouteCount =
            checked(
                rerouteCount
                + 1);

        route =
            updatedRoute;

        legs =
            updatedLegs;

        currentLegIndex =
            0;

        completed =
            false;

        execution.SetPlannedQuantity(
            Math.Max(
                execution.PurchasedQuantity,
                legs[0].PlannedQuantity));

        Refresh(
            journal.Current);
    }

    private void OnStateChanged(
        object? sender,
        GameStateChangedEventArgs e) =>
        Refresh(
            e.State);

    private void OnJournalEvent(
        object? sender,
        JournalEventReceivedEventArgs e)
    {
        if (e.Origin
            == JournalEventOrigin.Bootstrap)
        {
            return;
        }

        if (completed
            || currentLegIndex >= legs.Length)
        {
            return;
        }

        if (route.IsCargoSaleOnly)
        {
            ApplyCargoSaleJournalEvent(
                e);

            Refresh(
                journal.Current);

            return;
        }

        RouteLeg leg =
            legs[currentLegIndex];

        if (e.EventName.Equals(
                "MarketBuy",
                StringComparison.OrdinalIgnoreCase)
            && CommodityMatches(
                e.Data,
                leg)
            && MarketMatches(
                e.Data,
                leg.FromMarketId))
        {
            int count =
                GetInt(
                    e.Data,
                    "Count");

            int price =
                GetInt(
                    e.Data,
                    "BuyPrice");

            execution.ApplyBuy(
                count,
                price);
        }
        else if (e.EventName.Equals(
                     "MarketSell",
                     StringComparison.OrdinalIgnoreCase)
                 && CommodityMatches(
                     e.Data,
                     leg)
                 && MarketMatches(
                     e.Data,
                     leg.ToMarketId))
        {
            int count =
                GetInt(
                    e.Data,
                    "Count");

            int price =
                GetInt(
                    e.Data,
                    "SellPrice");

            int averagePaid =
                GetInt(
                    e.Data,
                    "AvgPricePaid");

            execution.ApplySell(
                count,
                price,
                averagePaid);

            bool saleComplete =
                execution.PurchasedQuantity > 0
                    ? execution.SoldQuantity
                      >= execution.PurchasedQuantity
                    : !HasRouteCommodity(
                        journal.Current,
                        leg);

            if (saleComplete)
            {
                DateTimeOffset completedAt =
                    JournalTimestamp(
                        e.Data);

                FinalizeCurrentHistoryLeg(
                    completedAt);

                if (currentLegIndex + 1
                    < legs.Length)
                {
                    AdvanceToNextLeg(
                        journal.Current);

                    legStartedUtc =
                        completedAt;
                }
                else
                {
                    completed =
                        true;

                    completedUtc =
                        completedAt;
                }
            }
        }

        Refresh(
            journal.Current);
    }

    private void AdvanceToNextLeg(
        GameStateSnapshot state)
    {
        completedPurchaseCost =
            checked(
                completedPurchaseCost
                + execution.PurchaseCost);

        completedSaleRevenue =
            checked(
                completedSaleRevenue
                + execution.SaleRevenue);

        completedRealizedProfit =
            checked(
                completedRealizedProfit
                + execution.RealizedProfit);

        currentLegIndex++;

        RouteLeg next =
            legs[currentLegIndex];

        execution =
            new TradeExecutionLedger(
                next.PlannedQuantity);

        baselineCargoCount =
            FindCargoCount(
                state,
                next.CommodityId);
    }

    private void Refresh(
        GameStateSnapshot state)
    {
        if (route.IsCargoSaleOnly)
        {
            RefreshCargoSaleOnly(
                state);

            return;
        }

        if (completed)
        {
            long completedActual =
                TotalRealizedProfit();

            Current =
                new TradeRouteProgress
                {
                    Stage =
                        TradeRouteStage.Completed,
                    LegNumber =
                        legs.Length,
                    LegCount =
                        legs.Length,
                    Action =
                        Loc.Get(
                            "Loc_ROUTE_COMPLETE"),
                    Commodity =
                        currentLegIndex < legs.Length
                            ? legs[currentLegIndex].CommodityDisplayName
                            : string.Empty,
                    CommodityId =
                        currentLegIndex < legs.Length
                            ? legs[currentLegIndex].CommodityId
                            : string.Empty,
                    PlannedQuantity =
                        execution.PlannedQuantity,
                    PurchasedQuantity =
                        execution.PurchasedQuantity,
                    SoldQuantity =
                        execution.SoldQuantity,
                    RemainingQuantity =
                        0,
                    AverageBuyPrice =
                        execution.AverageBuyPrice,
                    AverageSellPrice =
                        execution.AverageSellPrice,
                    PurchaseCost =
                        TotalPurchaseCost(),
                    SaleRevenue =
                        TotalSaleRevenue(),
                    ActualProfit =
                        completedActual,
                    ProjectedProfit =
                        completedActual,
                    PlannedProfit =
                        route.TotalProfitPerTrip,
                    ProjectedVariancePercent =
                        VariancePercent(
                            completedActual,
                            route.TotalProfitPerTrip),
                    HasTransactions =
                        execution.HasTransactions
                        || completedPurchaseCost != 0
                        || completedSaleRevenue != 0,
                    IsInDanger =
                        state.IsInDanger,
                    Note =
                        Loc.Get(
                            "Loc_Search_for_the_next_route_or_unpin_this_one")
                };

            RaiseChanged();

            RecordHistoryIfNeeded();

            return;
        }

        RouteLeg leg =
            legs[currentLegIndex];

        int cargoCount =
            FindCargoCount(
                state,
                leg.CommodityId);

        int routeCargoCount =
            Math.Max(
                0,
                cargoCount
                - baselineCargoCount);

        bool hasRouteCargo =
            execution.RemainingPurchasedQuantity > 0
            || routeCargoCount > 0;

        bool atOrigin =
            LocationMatches(
                state,
                leg.FromMarketId,
                leg.FromSystem,
                leg.FromStation);

        bool atDestination =
            LocationMatches(
                state,
                leg.ToMarketId,
                leg.ToSystem,
                leg.ToStation);

        bool plannedPurchaseFilled =
            execution.PlannedQuantity > 0
            && execution.PurchasedQuantity
               >= execution.PlannedQuantity;

        TradeRouteStage stage;

        if (atDestination
            && hasRouteCargo)
        {
            stage =
                TradeRouteStage.Sell;
        }
        else if (atOrigin
                 && !plannedPurchaseFilled)
        {
            stage =
                TradeRouteStage.Buy;
        }
        else if (hasRouteCargo)
        {
            stage =
                TradeRouteStage.FlyToSell;
        }
        else
        {
            stage =
                atOrigin
                    ? TradeRouteStage.Buy
                    : TradeRouteStage.FlyToBuy;
        }

        bool buying =
            stage
            is TradeRouteStage.Buy
            or TradeRouteStage.FlyToBuy;

        string system =
            buying
                ? leg.FromSystem
                : leg.ToSystem;

        string station =
            buying
                ? leg.FromStation
                : leg.ToStation;

        long marketId =
            buying
                ? leg.FromMarketId
                : leg.ToMarketId;

        int plannedPrice =
            buying
                ? leg.BuyPrice
                : leg.SellPrice;

        int? currentPrice =
            FindMarketPrice(
                state,
                leg.CommodityId,
                marketId,
                buying);

        int quantity =
            buying
                ? Math.Max(
                    0,
                    execution.PlannedQuantity
                    - execution.PurchasedQuantity)
                : Math.Max(
                    routeCargoCount,
                    execution.RemainingPurchasedQuantity);

        int projectedSellPrice =
            !buying
            && currentPrice is > 0
                ? currentPrice.Value
                : leg.SellPrice;

        long projected =
            checked(
                completedRealizedProfit
                + execution.ProjectedProfit(
                    leg.BuyPrice,
                    projectedSellPrice)
                + PlannedFutureLegProfit());

        long actual =
            TotalRealizedProfit();

        Current =
            new TradeRouteProgress
            {
                Stage =
                    stage,
                LegNumber =
                    currentLegIndex + 1,
                LegCount =
                    legs.Length,
                Action =
                    stage switch
                    {
                        TradeRouteStage.Buy =>
                            Loc.Get(
                                "Loc_BUY_CARGO"),
                        TradeRouteStage.Sell =>
                            Loc.Get(
                                "Loc_SELL_CARGO"),
                        TradeRouteStage.FlyToSell =>
                            Loc.Get(
                                "Loc_FLY_TO_SELL"),
                        _ =>
                            Loc.Get(
                                "Loc_FLY_TO_BUY")
                    },
                System =
                    system,
                Station =
                    station,
                Commodity =
                    leg.CommodityDisplayName,
                CommodityId =
                    leg.CommodityId,
                Quantity =
                    quantity,
                PlannedQuantity =
                    execution.PlannedQuantity,
                PurchasedQuantity =
                    execution.PurchasedQuantity,
                SoldQuantity =
                    execution.SoldQuantity,
                RemainingQuantity =
                    Math.Max(
                        routeCargoCount,
                        execution.RemainingPurchasedQuantity),
                PlannedPrice =
                    plannedPrice,
                CurrentMarketPrice =
                    currentPrice,
                AverageBuyPrice =
                    execution.AverageBuyPrice,
                AverageSellPrice =
                    execution.AverageSellPrice,
                PurchaseCost =
                    execution.PurchaseCost,
                SaleRevenue =
                    execution.SaleRevenue,
                ActualProfit =
                    actual,
                ProjectedProfit =
                    projected,
                PlannedProfit =
                    route.TotalProfitPerTrip,
                ProjectedVariancePercent =
                    VariancePercent(
                        projected,
                        route.TotalProfitPerTrip),
                HasTransactions =
                    execution.HasTransactions
                    || completedPurchaseCost != 0
                    || completedSaleRevenue != 0,
                RemainingJumps =
                    GetRemainingJumps(
                        state,
                        system),
                IsInDanger =
                    state.IsInDanger,
                Note =
                    BuildNote(
                        stage,
                        state,
                        plannedPrice,
                        currentPrice)
            };

        RaiseChanged();
    }

    private void FinalizeCurrentHistoryLeg(
        DateTimeOffset completedAt)
    {
        if (currentLegIndex < 0
            || currentLegIndex >= legs.Length
            || completedHistoryLegs.Any(item =>
                item.LegNumber
                == currentLegIndex + 1))
        {
            return;
        }

        RouteLeg leg =
            legs[currentLegIndex];

        completedHistoryLegs.Add(
            new TradeHistoryLegRecord
            {
                LegNumber =
                    currentLegIndex + 1,
                FromMarketId =
                    leg.FromMarketId,
                ToMarketId =
                    leg.ToMarketId,
                FromSystem =
                    leg.FromSystem,
                FromStation =
                    leg.FromStation,
                ToSystem =
                    leg.ToSystem,
                ToStation =
                    leg.ToStation,
                CommodityId =
                    leg.CommodityId,
                Commodity =
                    leg.CommodityDisplayName,
                PlannedQuantity =
                    leg.PlannedQuantity,
                PurchasedQuantity =
                    execution.PurchasedQuantity,
                SoldQuantity =
                    execution.SoldQuantity,
                PlannedBuyPrice =
                    leg.BuyPrice,
                PlannedSellPrice =
                    leg.SellPrice,
                AverageBuyPrice =
                    execution.AverageBuyPrice,
                AverageSellPrice =
                    execution.AverageSellPrice,
                PurchaseCost =
                    execution.PurchaseCost,
                SaleRevenue =
                    execution.SaleRevenue,
                ActualProfit =
                    execution.RealizedProfit,
                StartedAtUtc =
                    legStartedUtc,
                CompletedAtUtc =
                    completedAt
            });
    }

    private void RecordHistoryIfNeeded()
    {
        if (route.IsCargoSaleOnly
            || historyRecorded
            || !completed)
        {
            return;
        }

        historyRecorded =
            true;

        DateTimeOffset finished =
            completedUtc
            ?? DateTimeOffset.UtcNow;

        TradeHistoryService.Instance.Record(
            new TradeHistoryRecord
            {
                RouteKind =
                    route.IsRoundTrip
                        ? "roundtrip"
                        : "oneway",
                StartedAtUtc =
                    routeStartedUtc,
                CompletedAtUtc =
                    finished,
                RerouteCount =
                    rerouteCount,
                InitialPlannedProfit =
                    initialPlannedProfit,
                FinalPlannedProfit =
                    route.TotalProfitPerTrip,
                ActualProfit =
                    TotalRealizedProfit(),
                PurchaseCost =
                    TotalPurchaseCost(),
                SaleRevenue =
                    TotalSaleRevenue(),
                Legs =
                    completedHistoryLegs.ToArray()
            });
    }

    private static DateTimeOffset JournalTimestamp(
        JsonElement data)
    {
        string timestamp =
            GetString(
                data,
                "timestamp");

        return DateTimeOffset.TryParse(
            timestamp,
            out DateTimeOffset parsed)
                ? parsed
                : DateTimeOffset.UtcNow;
    }

    private void ApplyCargoSaleJournalEvent(
        JournalEventReceivedEventArgs e)
    {
        if (!e.EventName.Equals(
                "MarketSell",
                StringComparison.OrdinalIgnoreCase)
            || !MarketMatches(
                e.Data,
                route.CardHeader.ToStation.MarketId))
        {
            return;
        }

        string commodityId =
            CommodityIdentity.Normalize(
                GetString(
                    e.Data,
                    "Type"));

        string localized =
            GetString(
                e.Data,
                "Type_Localised");

        TradeCargoSaleItem? item =
            route.CargoSaleItems
                .FirstOrDefault(candidate =>
                    (!string.IsNullOrWhiteSpace(
                         commodityId)
                     && CommodityIdentity.Normalize(
                            candidate.InternalName)
                        .Equals(
                            commodityId,
                            StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(
                            localized)
                        && TextMatches(
                            candidate.Name,
                            localized)));

        if (item is null)
        {
            return;
        }

        string stableId =
            CommodityIdentity.Normalize(
                item.InternalName);

        if (string.IsNullOrWhiteSpace(
                stableId))
        {
            stableId =
                CommodityIdentity.Normalize(
                    item.Name);
        }

        int alreadySold =
            cargoSaleSold.TryGetValue(
                stableId,
                out int tracked)
                ? tracked
                : 0;

        int remainingPlanned =
            Math.Max(
                0,
                item.Quantity
                - alreadySold);

        int eventCount =
            Math.Max(
                0,
                GetInt(
                    e.Data,
                    "Count"));

        int applied =
            Math.Min(
                eventCount,
                remainingPlanned);

        if (applied <= 0)
        {
            return;
        }

        cargoSaleSold[stableId] =
            checked(
                alreadySold
                + applied);

        int sellPrice =
            Math.Max(
                0,
                GetInt(
                    e.Data,
                    "SellPrice"));

        cargoSaleRevenue =
            checked(
                cargoSaleRevenue
                + (long)applied
                  * sellPrice);

        bool allSold =
            route.CargoSaleItems
                .All(candidate =>
                {
                    string id =
                        CommodityIdentity.Normalize(
                            candidate.InternalName);

                    if (string.IsNullOrWhiteSpace(
                            id))
                    {
                        id =
                            CommodityIdentity.Normalize(
                                candidate.Name);
                    }

                    int sold =
                        cargoSaleSold.TryGetValue(
                            id,
                            out int value)
                            ? value
                            : 0;

                    return sold
                           >= candidate.Quantity;
                });

        if (allSold)
        {
            completed =
                true;

            completedUtc =
                JournalTimestamp(
                    e.Data);
        }
    }

    private void RefreshCargoSaleOnly(
        GameStateSnapshot state)
    {
        int planned =
            route.CargoSaleItems
                .Sum(item =>
                    Math.Max(
                        0,
                        item.Quantity));

        int sold =
            route.CargoSaleItems
                .Sum(item =>
                {
                    string id =
                        CommodityIdentity.Normalize(
                            item.InternalName);

                    if (string.IsNullOrWhiteSpace(
                            id))
                    {
                        id =
                            CommodityIdentity.Normalize(
                                item.Name);
                    }

                    return cargoSaleSold.TryGetValue(
                        id,
                        out int value)
                            ? Math.Min(
                                item.Quantity,
                                value)
                            : 0;
                });

        int remaining =
            Math.Max(
                0,
                planned
                - sold);

        bool atDestination =
            LocationMatches(
                state,
                route.CardHeader.ToStation.MarketId,
                route.CardHeader.ToStation.System,
                route.CardHeader.ToStation.Name);

        TradeRouteStage stage =
            completed
                ? TradeRouteStage.Completed
                : atDestination
                    ? TradeRouteStage.Sell
                    : TradeRouteStage.FlyToSell;

        string commodity =
            string.Join(
                " + ",
                route.CargoSaleItems
                    .Take(2)
                    .Select(item =>
                        item.Name.ToUpperInvariant()));

        if (route.CargoSaleItems.Count > 2)
        {
            commodity +=
                $" +{route.CargoSaleItems.Count - 2}";
        }

        long projectedValue =
            completed
                ? cargoSaleRevenue
                : route.PlannedSaleValue;

        Current =
            new TradeRouteProgress
            {
                Stage =
                    stage,
                LegNumber =
                    1,
                LegCount =
                    1,
                Action =
                    stage switch
                    {
                        TradeRouteStage.Completed =>
                            Loc.Get(
                                "Loc_ROUTE_COMPLETE"),
                        TradeRouteStage.Sell =>
                            Loc.Get(
                                "Loc_SELL_CARGO"),
                        _ =>
                            Loc.Get(
                                "Loc_FLY_TO_SELL")
                    },
                System =
                    route.CardHeader.ToStation.System,
                Station =
                    route.CardHeader.ToStation.Name,
                Commodity =
                    commodity,
                Quantity =
                    remaining,
                PlannedQuantity =
                    planned,
                SoldQuantity =
                    sold,
                RemainingQuantity =
                    remaining,
                SaleRevenue =
                    cargoSaleRevenue,
                ActualProfit =
                    cargoSaleRevenue,
                ProjectedProfit =
                    projectedValue,
                PlannedProfit =
                    route.PlannedSaleValue,
                ProjectedVariancePercent =
                    VariancePercent(
                        projectedValue,
                        route.PlannedSaleValue),
                HasTransactions =
                    cargoSaleRevenue > 0,
                RemainingJumps =
                    stage
                    == TradeRouteStage.Completed
                        ? 0
                        : GetRemainingJumps(
                            state,
                            route.CardHeader.ToStation.System),
                IsInDanger =
                    state.IsInDanger,
                Note =
                    stage
                    == TradeRouteStage.Completed
                        ? Loc.Get(
                            "Loc_Search_for_the_next_route_or_unpin_this_one")
                        : BuildNote(
                            stage,
                            state,
                            0,
                            null)
            };

        RaiseChanged();
    }

    private long PlannedFutureLegProfit()
    {
        long value =
            0;

        for (int index =
                 currentLegIndex + 1;
             index < legs.Length;
             index++)
        {
            RouteLeg future =
                legs[index];

            int spread =
                future.SellPrice
                - future.BuyPrice;

            value =
                checked(
                    value
                    + (long)future.PlannedQuantity
                      * spread);
        }

        return value;
    }

    private long TotalPurchaseCost() =>
        checked(
            completedPurchaseCost
            + execution.PurchaseCost);

    private long TotalSaleRevenue() =>
        checked(
            completedSaleRevenue
            + execution.SaleRevenue);

    private long TotalRealizedProfit() =>
        checked(
            completedRealizedProfit
            + execution.RealizedProfit);

    private static RouteLeg[] BuildLegs(
        TradeRoute route)
    {
        int firstQuantity =
            route.FirstRoute.PlannedQuantity > 0
                ? route.FirstRoute.PlannedQuantity
                : Math.Max(
                    0,
                    route.CargoCapacity);

        var result =
            new List<RouteLeg>
            {
                new(
                    route.CardHeader.FromStation.MarketId,
                    route.CardHeader.FromStation.SystemAddress,
                    route.CardHeader.FromStation.System,
                    route.CardHeader.FromStation.Name,
                    route.CardHeader.ToStation.MarketId,
                    route.CardHeader.ToStation.SystemAddress,
                    route.CardHeader.ToStation.System,
                    route.CardHeader.ToStation.Name,
                    StableCommodityId(
                        route.FirstRoute.BuyCommodity),
                    route.FirstRoute.BuyCommodity.Name,
                    firstQuantity,
                    route.FirstRoute.BuyCommodity.Price,
                    route.FirstRoute.SellCommodity.Price)
            };

        if (route.IsRoundTrip
            && route.SecondRoute is not null)
        {
            int secondQuantity =
                route.SecondRoute.PlannedQuantity > 0
                    ? route.SecondRoute.PlannedQuantity
                    : Math.Max(
                        0,
                        route.CargoCapacity);

            result.Add(
                new RouteLeg(
                    route.CardHeader.ToStation.MarketId,
                    route.CardHeader.ToStation.SystemAddress,
                    route.CardHeader.ToStation.System,
                    route.CardHeader.ToStation.Name,
                    route.CardHeader.FromStation.MarketId,
                    route.CardHeader.FromStation.SystemAddress,
                    route.CardHeader.FromStation.System,
                    route.CardHeader.FromStation.Name,
                    StableCommodityId(
                        route.SecondRoute.BuyCommodity),
                    route.SecondRoute.BuyCommodity.Name,
                    secondQuantity,
                    route.SecondRoute.BuyCommodity.Price,
                    route.SecondRoute.SellCommodity.Price));
        }

        return result.ToArray();
    }

    private static string StableCommodityId(
        Commodity commodity)
    {
        string value =
            CommodityIdentity.Normalize(
                commodity.InternalName);

        return string.IsNullOrWhiteSpace(
                value)
            ? CommodityIdentity.Normalize(
                commodity.Name)
            : value;
    }

    private static bool LocationMatches(
        GameStateSnapshot state,
        long marketId,
        string system,
        string station) =>
        TradeLocationMatcher.IsAtMarket(
            state,
            marketId,
            system,
            station);

    private static bool HasRouteCommodity(
        GameStateSnapshot state,
        RouteLeg leg) =>
        Math.Max(
            0,
            FindCargoCount(
                state,
                leg.CommodityId))
        > 0;

    private static int FindCargoCount(
        GameStateSnapshot state,
        string commodityId) =>
        state.CargoByCommodityId.TryGetValue(
            commodityId,
            out CargoCommoditySnapshot? item)
                ? item.Count
                : 0;

    private static int? FindMarketPrice(
        GameStateSnapshot state,
        string commodityId,
        long marketId,
        bool buying)
    {
        if (!state.Docked
            || state.MarketUpdatedUtc
               is not { } updated
            || DateTimeOffset.UtcNow
               - updated
               > TimeSpan.FromHours(
                   1))
        {
            return null;
        }

        if (marketId > 0
            && state.MarketSnapshotId
               != marketId)
        {
            return null;
        }

        if (!state.MarketByCommodityId.TryGetValue(
                commodityId,
                out MarketItemSnapshot? item))
        {
            return null;
        }

        int price =
            buying
                ? item.BuyPrice
                : item.SellPrice;

        return price > 0
            ? price
            : null;
    }

    private static int GetRemainingJumps(
        GameStateSnapshot state,
        string destination)
    {
        if (state.NavRoute.Count
            == 0)
        {
            return 0;
        }

        int destinationIndex =
            state.NavRoute
                .ToList()
                .FindIndex(
                    star =>
                        TextMatches(
                            star.System,
                            destination));

        return destinationIndex > 0
            ? destinationIndex
            : Math.Max(
                0,
                state.NavRoute.Count
                - 1);
    }

    private static string BuildNote(
        TradeRouteStage stage,
        GameStateSnapshot state,
        int planned,
        int? current)
    {
        if (state.IsInDanger)
        {
            return Loc.Get(
                "Loc_DANGER_flight_alerts_have_priority");
        }

        if (current is { } marketPrice
            && planned > 0)
        {
            double difference =
                (marketPrice - planned)
                * 100d
                / planned;

            return Loc.Format(
                "Loc_Market_Note_Format",
                marketPrice,
                planned,
                difference);
        }

        return stage
               is TradeRouteStage.Buy
               or TradeRouteStage.Sell
            ? Loc.Get(
                "Loc_Open_Commodity_Market_to_validate_the_current_price")
            : state.NavRoute.Count > 0
                ? Loc.Get(
                    "Loc_Game_route_detected")
                : Loc.Get(
                    "Loc_Plot_the_destination_in_Galaxy_Map");
    }

    private static bool CommodityMatches(
        JsonElement data,
        RouteLeg leg)
    {
        string internalName =
            CommodityIdentity.Normalize(
                GetString(
                    data,
                    "Type"));

        if (!string.IsNullOrWhiteSpace(
                internalName))
        {
            return internalName.Equals(
                leg.CommodityId,
                StringComparison.OrdinalIgnoreCase);
        }

        string localized =
            GetString(
                data,
                "Type_Localised");

        return TextMatches(
            localized,
            leg.CommodityDisplayName);
    }

    private static bool MarketMatches(
        JsonElement data,
        long expectedMarketId)
    {
        if (expectedMarketId <= 0)
        {
            return true;
        }

        long marketId =
            GetLong(
                data,
                "MarketID");

        return marketId <= 0
               || marketId
                  == expectedMarketId;
    }

    private static bool TextMatches(
        string? left,
        string? right) =>
        string.Equals(
            CommodityIdentity.Normalize(
                left ?? string.Empty),
            CommodityIdentity.Normalize(
                right ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);

    private static int GetInt(
        JsonElement data,
        string property) =>
        data.TryGetProperty(
            property,
            out JsonElement value)
        && value.TryGetInt32(
            out int result)
            ? result
            : 0;

    private static long GetLong(
        JsonElement data,
        string property) =>
        data.TryGetProperty(
            property,
            out JsonElement value)
        && value.TryGetInt64(
            out long result)
            ? result
            : 0;

    private static string GetString(
        JsonElement data,
        string property) =>
        data.TryGetProperty(
            property,
            out JsonElement value)
        && value.ValueKind
           == JsonValueKind.String
            ? value.GetString()
              ?? string.Empty
            : string.Empty;

    private static double VariancePercent(
        long actualOrProjected,
        long planned)
    {
        if (planned == 0)
        {
            return 0;
        }

        return (actualOrProjected - planned)
               * 100d
               / Math.Abs(
                   (double)planned);
    }

    private void RaiseChanged() =>
        ProgressChanged?.Invoke(
            this,
            new TradeRouteProgressChangedEventArgs(
                Current));

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed =
            true;

        journal.StateChanged -=
            OnStateChanged;

        journal.JournalEventReceived -=
            OnJournalEvent;
    }

    private sealed record RouteLeg(
        long FromMarketId,
        long FromSystemAddress,
        string FromSystem,
        string FromStation,
        long ToMarketId,
        long ToSystemAddress,
        string ToSystem,
        string ToStation,
        string CommodityId,
        string CommodityDisplayName,
        int PlannedQuantity,
        int BuyPrice,
        int SellPrice);
}
