using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Trading;

public enum TradeActiveStage
{
    TravellingToSource = 0,
    AtSource = 1,
    TravellingToTarget = 2,
    AtTarget = 3,
    Completed = 4
}

public sealed class TradeActiveRouteSession
{
    private TradeRouteCandidate activeLeg;
    private TradeRoundTripCandidate? roundTrip;
    private int roundTripPhase;
    private int baselineCargoCount;
    private bool sourceVisited;
    private bool cargoLoaded;
    private bool completed;
    private int? observedBuyPrice;
    private DateTimeOffset? ignoredDegradedMarketUpdate;

    public TradeActiveRouteSession(
        TradeRouteCandidate candidate,
        TradeSearchConstraints constraints,
        GameStateSnapshot state)
    {
        activeLeg =
            candidate
            ?? throw new ArgumentNullException(
                nameof(candidate));

        SearchConstraints =
            constraints
            ?? throw new ArgumentNullException(
                nameof(constraints));

        ArgumentNullException.ThrowIfNull(
            state);

        baselineCargoCount =
            CargoCount(
                state,
                CommodityId);

        Update(
            state);
    }

    public TradeActiveRouteSession(
        TradeRoundTripCandidate candidate,
        TradeSearchConstraints constraints,
        GameStateSnapshot state)
        : this(
            candidate?.Outbound
            ?? throw new ArgumentNullException(
                nameof(candidate)),
            constraints,
            state)
    {
        roundTrip =
            candidate;

        roundTripPhase =
            0;

        Update(
            state);
    }

    public TradeSearchConstraints SearchConstraints { get; }

    public TradeRouteCandidate ActiveLeg =>
        activeLeg;

    public TradeRoundTripCandidate? RoundTrip =>
        roundTrip;

    public bool IsRoundTrip =>
        roundTrip is not null;

    public bool IsReturnLeg =>
        IsRoundTrip
        && roundTripPhase == 1;

    public string CommodityId =>
        CommodityIdentity.Normalize(
            activeLeg.Source.CommodityName);

    public TradeActiveStage Stage { get; private set; }

    public bool CargoLoaded =>
        cargoLoaded;

    public bool IsCompleted =>
        completed;

    public int ActualCargoCount { get; private set; }

    public bool SourceMarketOpen { get; private set; }

    public bool TargetMarketOpen { get; private set; }

    public MarketItemSnapshot? LiveSourceMarket { get; private set; }

    public MarketItemSnapshot? LiveTargetMarket { get; private set; }

    public DateTimeOffset? CurrentMarketUpdateUtc { get; private set; }

    public bool TargetDegraded { get; private set; }

    public bool ShouldOfferReroute =>
        TargetDegraded
        && (!ignoredDegradedMarketUpdate.HasValue
            || ignoredDegradedMarketUpdate
               != CurrentMarketUpdateUtc);

    public int EffectiveBuyPrice =>
        observedBuyPrice
        ?? activeLeg.Source.BuyFromStationPrice;

    public int EffectiveSellPrice =>
        LiveTargetMarket?.SellPrice
        is > 0
            ? LiveTargetMarket.SellPrice
            : activeLeg.Target.SellToStationPrice;

    public long ExpectedProfit
    {
        get
        {
            int amount =
                cargoLoaded
                    ? Math.Max(
                        0,
                        ActualCargoCount - baselineCargoCount)
                    : activeLeg.TradableAmount;

            if (amount <= 0)
            {
                amount =
                    activeLeg.TradableAmount;
            }

            int spread =
                EffectiveSellPrice
                - EffectiveBuyPrice;

            return spread <= 0
                ? 0
                : checked(
                    (long)spread
                    * amount);
        }
    }

    public void Update(
        GameStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        if (completed)
        {
            Stage =
                TradeActiveStage.Completed;

            return;
        }

        ActualCargoCount =
            CargoCount(
                state,
                CommodityId);

        bool atSource =
            IsCurrentMarket(
                state,
                activeLeg.Source.MarketId);

        bool atTarget =
            IsCurrentMarket(
                state,
                activeLeg.Target.MarketId);

        ReadLiveMarket(
            state,
            atSource,
            atTarget);

        if (atSource)
        {
            sourceVisited =
                true;

            if (LiveSourceMarket?.BuyPrice
                is > 0)
            {
                observedBuyPrice =
                    LiveSourceMarket.BuyPrice;
            }
        }

        if (!cargoLoaded
            && sourceVisited
            && ActualCargoCount
               > baselineCargoCount)
        {
            cargoLoaded =
                true;
        }

        if (cargoLoaded
            && atTarget
            && ActualCargoCount
               <= baselineCargoCount)
        {
            if (roundTrip is not null
                && roundTripPhase == 0)
            {
                AdvanceToReturnLeg(
                    state);

                return;
            }

            completed =
                true;

            Stage =
                TradeActiveStage.Completed;

            return;
        }

        Stage =
            cargoLoaded
                ? atTarget
                    ? TradeActiveStage.AtTarget
                    : TradeActiveStage.TravellingToTarget
                : atSource
                    ? TradeActiveStage.AtSource
                    : TradeActiveStage.TravellingToSource;

        UpdateTargetDegradation();
    }

    public void AcknowledgeTargetDegradation()
    {
        ignoredDegradedMarketUpdate =
            CurrentMarketUpdateUtc;
    }

    public void ApplyReroute(
        TradeRouteCandidate rerouted,
        GameStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(
            rerouted);

        ArgumentNullException.ThrowIfNull(
            state);

        activeLeg =
            rerouted;

        roundTrip =
            null;

        roundTripPhase =
            0;

        completed =
            false;

        sourceVisited =
            true;

        cargoLoaded =
            true;

        ActualCargoCount =
            CargoCount(
                state,
                CommodityId);

        baselineCargoCount =
            Math.Max(
                0,
                ActualCargoCount
                - Math.Max(
                    1,
                    rerouted.TradableAmount));

        ignoredDegradedMarketUpdate =
            null;

        LiveSourceMarket =
            null;

        LiveTargetMarket =
            null;

        SourceMarketOpen =
            false;

        TargetMarketOpen =
            false;

        TargetDegraded =
            false;

        Update(
            state);
    }

    public double RemainingDistanceLy(
        GameStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        TradeMarketOrder destination =
            cargoLoaded
                ? activeLeg.Target
                : activeLeg.Source;

        if (state.SystemX is { } x
            && state.SystemY is { } y
            && state.SystemZ is { } z)
        {
            return Distance(
                x,
                y,
                z,
                destination.SystemX,
                destination.SystemY,
                destination.SystemZ);
        }

        if (cargoLoaded
            && destination.ReferenceDistanceLy
               is { } reference)
        {
            return reference;
        }

        return cargoLoaded
            ? activeLeg.SourceToTargetDistanceLy
            : activeLeg.OriginToSourceDistanceLy;
    }

    private void ReadLiveMarket(
        GameStateSnapshot state,
        bool atSource,
        bool atTarget)
    {
        SourceMarketOpen =
            atSource
            && state.MarketSnapshotId
               == activeLeg.Source.MarketId;

        TargetMarketOpen =
            atTarget
            && state.MarketSnapshotId
               == activeLeg.Target.MarketId;

        CurrentMarketUpdateUtc =
            SourceMarketOpen
            || TargetMarketOpen
                ? state.MarketUpdatedUtc
                : null;

        LiveSourceMarket =
            SourceMarketOpen
            && state.MarketByCommodityId.TryGetValue(
                CommodityId,
                out MarketItemSnapshot? source)
                ? source
                : null;

        LiveTargetMarket =
            TargetMarketOpen
            && state.MarketByCommodityId.TryGetValue(
                CommodityId,
                out MarketItemSnapshot? target)
                ? target
                : null;
    }

    private void UpdateTargetDegradation()
    {
        if (!TargetMarketOpen
            || !cargoLoaded)
        {
            TargetDegraded =
                false;

            return;
        }

        if (LiveTargetMarket is null
            || LiveTargetMarket.SellPrice <= 0)
        {
            TargetDegraded =
                true;

            return;
        }

        bool priceDrop =
            activeLeg.Target.SellToStationPrice > 0
            && LiveTargetMarket.SellPrice
               < activeLeg.Target.SellToStationPrice
                 * 0.95d;

        int cargoToSell =
            Math.Max(
                1,
                ActualCargoCount
                - baselineCargoCount);

        bool insufficientDemand =
            LiveTargetMarket.Demand > 0
            && LiveTargetMarket.Demand
               < cargoToSell;

        TargetDegraded =
            priceDrop
            || insufficientDemand;
    }

    private void AdvanceToReturnLeg(
        GameStateSnapshot state)
    {
        if (roundTrip is null)
        {
            return;
        }

        roundTripPhase =
            1;

        activeLeg =
            BuildReturnLeg(
                roundTrip);

        baselineCargoCount =
            CargoCount(
                state,
                CommodityId);

        sourceVisited =
            true;

        cargoLoaded =
            false;

        observedBuyPrice =
            null;

        ignoredDegradedMarketUpdate =
            null;

        SourceMarketOpen =
            false;

        TargetMarketOpen =
            false;

        LiveSourceMarket =
            null;

        LiveTargetMarket =
            null;

        TargetDegraded =
            false;

        Update(
            state);
    }

    private static TradeRouteCandidate BuildReturnLeg(
        TradeRoundTripCandidate candidate) =>
        new()
        {
            Source =
                candidate.ReturnSource,
            Target =
                candidate.ReturnTarget,
            ProfitPerTon =
                candidate.ReturnProfitPerTon,
            TradableAmount =
                candidate.ReturnTradableAmount,
            ProfitPerTrip =
                candidate.ReturnProfitPerTrip,
            OriginToSourceDistanceLy =
                0,
            SourceToTargetDistanceLy =
                candidate.TradeLegDistanceLy,
            SourceAge =
                candidate.ReturnSourceAge,
            TargetAge =
                candidate.ReturnTargetAge
        };

    private static int CargoCount(
        GameStateSnapshot state,
        string commodityId) =>
        state.CargoByCommodityId.TryGetValue(
            commodityId,
            out CargoCommoditySnapshot? cargo)
                ? cargo.Count
                : 0;

    private static bool IsCurrentMarket(
        GameStateSnapshot state,
        long marketId) =>
        state.Docked
        && state.MarketSnapshotId
           == marketId;

    private static double Distance(
        double ax,
        double ay,
        double az,
        double bx,
        double by,
        double bz)
    {
        double dx =
            ax - bx;

        double dy =
            ay - by;

        double dz =
            az - bz;

        return Math.Sqrt(
            dx * dx
            + dy * dy
            + dz * dz);
    }
}
