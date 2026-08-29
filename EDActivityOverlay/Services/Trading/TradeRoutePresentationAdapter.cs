using EDActivityOverlay.Models.Trading;

namespace EDActivityOverlay.Services.Trading;

public static class TradeRoutePresentationAdapter
{
    public static TradeRoute ToPresentation(TradeRouteCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return new TradeRoute
        {
            CardHeader = new CardHeader
            {
                FromStation = ToStation(candidate.Source),
                ToStation = ToStation(candidate.Target)
            },
            FirstRoute = new TradeLeg
            {
                BuyCommodity = new Commodity
                {
                    Name = candidate.Source.CommodityName,
                    Price = candidate.Source.BuyFromStationPrice,
                    Supply = candidate.Source.Stock.ToString("N0")
                },
                SellCommodity = new Commodity
                {
                    Name = candidate.Target.CommodityName,
                    Price = candidate.Target.SellToStationPrice,
                    Demand = candidate.Target.HasInfiniteDemand
                        ? "∞"
                        : candidate.Target.Demand.ToString("N0")
                },
                ProfitPerUnit = candidate.ProfitPerTon,
                LastUpdate = candidate.OldestUpdateUtc.ToLocalTime().ToString("g")
            },
            CargoCapacity = candidate.TradableAmount,
            IsRoundTrip = false,
            RouteDistance = candidate.SourceToTargetDistanceLy,
            LastUpdate = candidate.OldestUpdateUtc.ToLocalTime().ToString("g"),
            TotalProfitPerTrip = SaturatingInt(candidate.ProfitPerTrip)
        };
    }

    public static List<TradeRoute> ToPresentation(IEnumerable<TradeRouteCandidate> candidates) =>
        candidates.Select(ToPresentation).ToList();

    private static Station ToStation(TradeMarketOrder order) =>
        new()
        {
            Name = order.StationName,
            System = order.SystemName,
            DistanceFromStar = order.DistanceToArrivalLs ?? 0,
            StationType = order.StationType,
            LandingPadSize = order.MaxLandingPadSize switch
            {
                3 => "L",
                2 => "M",
                1 => "S",
                _ => "?"
            },
            StationDistanceLs = order.DistanceToArrivalLs ?? 0,
            LastUpdated = order.UpdatedAt.ToLocalTime().ToString("g")
        };

    private static int SaturatingInt(long value) =>
        value switch
        {
            > int.MaxValue => int.MaxValue,
            < int.MinValue => int.MinValue,
            _ => (int)value
        };
}
