using EDActivityOverlay.Models.Trading;

namespace EDActivityOverlay.Services.Trading;

public static partial class TradeRoutePresentationAdapter
{
    public static TradeRoute ToPresentation(
        TradeRoundTripCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);

        TradeRouteCandidate outbound =
            candidate.Outbound;

        return
            new TradeRoute
            {
                CardHeader =
                    new CardHeader
                    {
                        FromStation =
                            ToStation(
                                outbound.Source),
                        ToStation =
                            ToStation(
                                outbound.Target)
                    },
                FirstRoute =
                    new TradeLeg
                    {
                        BuyCommodity =
                            new Commodity
                            {
                                Name =
                                    outbound.Source.CommodityName,
                                Price =
                                    outbound.Source.BuyFromStationPrice,
                                Supply =
                                    outbound.Source.Stock.ToString(
                                        "N0")
                            },
                        SellCommodity =
                            new Commodity
                            {
                                Name =
                                    outbound.Target.CommodityName,
                                Price =
                                    outbound.Target.SellToStationPrice,
                                Demand =
                                    outbound.Target.HasInfiniteDemand
                                        ? "∞"
                                        : outbound.Target.Demand.ToString(
                                            "N0")
                            },
                        ProfitPerUnit =
                            outbound.ProfitPerTon,
                        LastUpdate =
                            outbound.OldestUpdateUtc
                                .ToLocalTime()
                                .ToString(
                                    "g")
                    },
                SecondRoute =
                    new TradeLeg
                    {
                        BuyCommodity =
                            new Commodity
                            {
                                Name =
                                    candidate.ReturnSource.CommodityName,
                                Price =
                                    candidate.ReturnSource.BuyFromStationPrice,
                                Supply =
                                    candidate.ReturnSource.Stock.ToString(
                                        "N0")
                            },
                        SellCommodity =
                            new Commodity
                            {
                                Name =
                                    candidate.ReturnTarget.CommodityName,
                                Price =
                                    candidate.ReturnTarget.SellToStationPrice,
                                Demand =
                                    candidate.ReturnTarget.HasInfiniteDemand
                                        ? "∞"
                                        : candidate.ReturnTarget.Demand.ToString(
                                            "N0")
                            },
                        ProfitPerUnit =
                            candidate.ReturnProfitPerTon,
                        LastUpdate =
                            candidate.OldestUpdateUtc
                                .ToLocalTime()
                                .ToString(
                                    "g")
                    },
                CargoCapacity =
                    Math.Max(
                        outbound.TradableAmount,
                        candidate.ReturnTradableAmount),
                IsRoundTrip =
                    true,
                RouteDistance =
                    outbound.SourceToTargetDistanceLy,
                LastUpdate =
                    candidate.OldestUpdateUtc
                        .ToLocalTime()
                        .ToString(
                            "g"),
                TotalProfitPerTrip =
                    SaturatingInt(
                        candidate.ProfitPerCycle)
            };
    }
}
