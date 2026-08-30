using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Trading;

public sealed class TradeActiveRouteRerouteService
{
    private readonly ITradeDataProvider provider;

    public TradeActiveRouteRerouteService()
        : this(
            new ArdentMarketDataProvider())
    {
    }

    public TradeActiveRouteRerouteService(
        ITradeDataProvider provider)
    {
        this.provider =
            provider
            ?? throw new ArgumentNullException(
                nameof(provider));
    }

    public async Task<TradeRouteCandidate?> FindBetterBuyerAsync(
        GameStateSnapshot state,
        TradeActiveRouteSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        ArgumentNullException.ThrowIfNull(
            session);

        int cargoAmount =
            session.ActualCargoCount;

        if (cargoAmount <= 0)
        {
            return null;
        }

        TradeSearchConstraints constraints =
            session.SearchConstraints;

        TradeSystemLocation origin =
            await provider.ResolveSystemAsync(
                    new TradeSystemReference(
                        state.StarSystem,
                        state.SystemAddress),
                    cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<TradeMarketOrder> buyers =
            await provider.GetNearbyImportsAsync(
                    origin,
                    session.CommodityId,
                    constraints.TargetSearchRadiusLy,
                    constraints,
                    cancellationToken)
                .ConfigureAwait(false);

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        int currentLiveSell =
            session.LiveTargetMarket?.SellPrice
            ?? 0;

        TradeRouteCandidate? best =
            buyers
                .Where(order =>
                    order.MarketId
                    != session.ActiveLeg.Target.MarketId)
                .Where(order =>
                    IsUsableBuyer(
                        order,
                        constraints,
                        now))
                .Select(order =>
                    BuildCandidate(
                        session,
                        order,
                        cargoAmount,
                        now))
                .Where(candidate =>
                    candidate is not null)
                .Select(candidate =>
                    candidate!)
                .Where(candidate =>
                    currentLiveSell <= 0
                    || candidate.Target.SellToStationPrice
                       > currentLiveSell)
                .OrderByDescending(candidate =>
                    candidate.ProfitPerTrip)
                .ThenByDescending(candidate =>
                    candidate.ProfitPerTon)
                .ThenBy(candidate =>
                    candidate.Target.ReferenceDistanceLy
                    ?? double.MaxValue)
                .FirstOrDefault();

        return best;
    }

    private static TradeRouteCandidate? BuildCandidate(
        TradeActiveRouteSession session,
        TradeMarketOrder target,
        int cargoAmount,
        DateTimeOffset now)
    {
        long usableDemand =
            target.HasInfiniteDemand
                ? cargoAmount
                : Math.Max(
                    0,
                    target.Demand);

        int amount =
            checked(
                (int)Math.Min(
                    cargoAmount,
                    usableDemand));

        if (amount <= 0)
        {
            return null;
        }

        int profitPerTon =
            target.SellToStationPrice
            - session.EffectiveBuyPrice;

        if (profitPerTon <= 0)
        {
            return null;
        }

        double sourceToTarget =
            Distance(
                session.ActiveLeg.Source,
                target);

        TimeSpan targetAge =
            now > target.UpdatedAt
                ? now - target.UpdatedAt
                : TimeSpan.Zero;

        return new TradeRouteCandidate
        {
            Source =
                session.ActiveLeg.Source,
            Target =
                target,
            ProfitPerTon =
                profitPerTon,
            TradableAmount =
                amount,
            ProfitPerTrip =
                checked(
                    (long)profitPerTon
                    * amount),
            OriginToSourceDistanceLy =
                session.ActiveLeg.OriginToSourceDistanceLy,
            SourceToTargetDistanceLy =
                sourceToTarget,
            SourceAge =
                session.ActiveLeg.SourceAge,
            TargetAge =
                targetAge
        };
    }

    private static bool IsUsableBuyer(
        TradeMarketOrder order,
        TradeSearchConstraints constraints,
        DateTimeOffset now)
    {
        if (order.SellToStationPrice <= 0)
        {
            return false;
        }

        if (!constraints.IncludeFleetCarriers
            && order.IsFleetCarrier)
        {
            return false;
        }

        if (order.MaxLandingPadSize
            < constraints.MinLandingPadSize)
        {
            return false;
        }

        if (constraints.MaxStationDistanceLs
            is { } maxLs
            && (order.DistanceToArrivalLs is null
                || order.DistanceToArrivalLs > maxLs))
        {
            return false;
        }

        double distance =
            order.ReferenceDistanceLy
            ?? double.MaxValue;

        if (distance
            > constraints.TargetSearchRadiusLy)
        {
            return false;
        }

        if (order.UpdatedAt
            == DateTimeOffset.MinValue)
        {
            return false;
        }

        TimeSpan age =
            now > order.UpdatedAt
                ? now - order.UpdatedAt
                : TimeSpan.Zero;

        return age
               <= constraints.MaxDataAge;
    }

    private static double Distance(
        TradeMarketOrder left,
        TradeMarketOrder right)
    {
        double dx =
            left.SystemX
            - right.SystemX;

        double dy =
            left.SystemY
            - right.SystemY;

        double dz =
            left.SystemZ
            - right.SystemZ;

        return Math.Sqrt(
            dx * dx
            + dy * dy
            + dz * dz);
    }
}
