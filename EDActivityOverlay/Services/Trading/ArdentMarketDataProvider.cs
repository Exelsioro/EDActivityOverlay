using EDActivityOverlay.Services.Ardent;

namespace EDActivityOverlay.Services.Trading;

public sealed partial class ArdentMarketDataProvider : ITradeDataProvider
{
    private readonly ArdentApiClient client;

    public ArdentMarketDataProvider() : this(new ArdentApiClient())
    {
    }

    public ArdentMarketDataProvider(ArdentApiClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public string Name => "Ardent Insight";

    public async Task<TradeSystemLocation> ResolveSystemAsync(
        TradeSystemReference system,
        CancellationToken cancellationToken = default)
    {
        ArdentSystemDto dto = await client.GetSystemAsync(
            new ArdentSystemReference(system.Name, system.SystemAddress),
            cancellationToken).ConfigureAwait(false);

        return new TradeSystemLocation(
            dto.SystemAddress,
            dto.SystemName,
            dto.SystemX,
            dto.SystemY,
            dto.SystemZ);
    }

    public async Task<IReadOnlyList<TradeCommoditySummary>> GetCommoditySummariesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ArdentCommodityReportDto> rows =
            await client.GetCommoditiesAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.CommodityName))
            .Select(row => new TradeCommoditySummary(
                row.CommodityName,
                row.MinBuyPrice,
                row.MaxSellPrice,
                row.TotalStock,
                row.TotalDemand))
            .ToArray();
    }

    public async Task<IReadOnlyList<TradeMarketOrder>> GetSystemCommodityOrdersAsync(
        TradeSystemLocation system,
        string commodityName,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ArdentMarketOrderDto> rows = await client.GetSystemCommodityAsync(
            system.SystemAddress,
            commodityName,
            constraints.ApiMaxDaysAgo,
            cancellationToken).ConfigureAwait(false);

        return rows.Select(row => Map(row, 0)).ToArray();
    }

    public async Task<IReadOnlyList<TradeMarketOrder>> GetNearbyExportsAsync(
        TradeSystemLocation system,
        string commodityName,
        int maxDistanceLy,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ArdentMarketOrderDto> rows = await client.GetNearbyExportsAsync(
            system.SystemAddress,
            commodityName,
            constraints.MinSupply,
            maxDistanceLy,
            constraints.ApiMaxDaysAgo,
            constraints.IncludeFleetCarriers ? null : false,
            cancellationToken).ConfigureAwait(false);

        return rows.Select(row => Map(row, row.Distance)).ToArray();
    }

    public async Task<IReadOnlyList<TradeMarketOrder>> GetNearbyImportsAsync(
        TradeSystemLocation system,
        string commodityName,
        int maxDistanceLy,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ArdentMarketOrderDto> rows = await client.GetNearbyImportsAsync(
            system.SystemAddress,
            commodityName,
            constraints.MinDemand,
            maxDistanceLy,
            constraints.ApiMaxDaysAgo,
            constraints.IncludeFleetCarriers ? null : false,
            cancellationToken).ConfigureAwait(false);

        return rows.Select(row => Map(row, row.Distance)).ToArray();
    }

    private static TradeMarketOrder Map(ArdentMarketOrderDto row, double? referenceDistanceLy) =>
        new()
        {
            CommodityName = row.CommodityName,
            MarketId = row.MarketId,
            StationName = row.StationName,
            StationType = row.StationType,
            DistanceToArrivalLs = row.DistanceToArrival,
            MaxLandingPadSize = row.MaxLandingPadSize ?? 0,
            SystemAddress = row.SystemAddress,
            SystemName = row.SystemName,
            SystemX = row.SystemX,
            SystemY = row.SystemY,
            SystemZ = row.SystemZ,
            BuyFromStationPrice = row.BuyPrice,
            SellToStationPrice = row.SellPrice,
            Demand = row.Demand,
            Stock = row.Stock,
            UpdatedAt = row.UpdatedAt ?? DateTimeOffset.MinValue,
            ReferenceDistanceLy = referenceDistanceLy
        };
}
