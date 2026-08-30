namespace EDActivityOverlay.Services.Trading;

public interface ITradeDataProvider
{
    string Name { get; }

    Task<TradeSystemLocation> ResolveSystemAsync(
        TradeSystemReference system,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TradeCommoditySummary>> GetCommoditySummariesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TradeMarketOrder>> GetSystemCommodityOrdersAsync(
        TradeSystemLocation system,
        string commodityName,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TradeMarketOrder>> GetNearbyExportsAsync(
        TradeSystemLocation system,
        string commodityName,
        int maxDistanceLy,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TradeMarketOrder>> GetNearbyImportsAsync(
        TradeSystemLocation system,
        string commodityName,
        int maxDistanceLy,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default);
}
