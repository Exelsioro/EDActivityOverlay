using EDActivityOverlay.Models;
using EDActivityOverlay.Models.Trading;

namespace EDActivityOverlay.Services.Trading;

public static partial class TradeRoutePresentationAdapter
{
    public static TradeRoute ToPresentation(
        CargoSaleCandidate candidate,
        GameStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(state);

        CargoSaleLine[] lines =
            candidate.Lines
                .Where(line => line.SellAmount > 0)
                .ToArray();

        if (lines.Length == 0)
        {
            throw new ArgumentException(
                "Cargo-sale route requires at least one sellable cargo line.",
                nameof(candidate));
        }

        CargoSaleLine first =
            lines[0];

        return new TradeRoute
        {
            CardHeader =
                new CardHeader
                {
                    FromStation =
                        new Station
                        {
                            MarketId =
                                state.MarketId
                                ?? 0,
                            SystemAddress =
                                state.SystemAddress,
                            Name =
                                state.Station,
                            System =
                                state.StarSystem
                        },
                    ToStation =
                        ToStation(
                            candidate.Target)
                },
            FirstRoute =
                new TradeLeg
                {
                    PlannedQuantity =
                        candidate.SellableUnits,
                    BuyCommodity =
                        new Commodity
                        {
                            InternalName =
                                first.CommodityId,
                            Name =
                                first.DisplayName
                        },
                    SellCommodity =
                        new Commodity
                        {
                            InternalName =
                                first.CommodityId,
                            Name =
                                first.DisplayName,
                            Price =
                                first.SellPrice,
                            Demand =
                                first.Demand == 0
                                    ? "∞"
                                    : first.Demand.ToString("N0")
                        },
                    LastUpdate =
                        candidate.Target.UpdatedAt
                            .ToLocalTime()
                            .ToString("g")
                },
            CargoCapacity =
                candidate.SellableUnits,
            IsRoundTrip =
                false,
            IsCargoSaleOnly =
                true,
            CargoSaleItems =
                lines
                    .Select(line =>
                        new TradeCargoSaleItem
                        {
                            InternalName =
                                line.CommodityId,
                            Name =
                                line.DisplayName,
                            Quantity =
                                line.SellAmount,
                            PlannedSellPrice =
                                line.SellPrice
                        })
                    .ToList(),
            PlannedSaleValue =
                candidate.TotalRevenue,
            RouteDistance =
                candidate.DistanceLy,
            LastUpdate =
                candidate.Target.UpdatedAt
                    .ToLocalTime()
                    .ToString("g"),
            TotalProfitPerTrip =
                SaturatingInt(
                    candidate.TotalRevenue)
        };
    }
}
