namespace EDActivityOverlay.Services.Trading;

public interface ITradeSystemTradeSidesProvider
{
    Task<IReadOnlyList<TradeMarketOrder>> GetSystemExportsAsync(
        TradeSystemLocation system,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TradeMarketOrder>> GetSystemImportsAsync(
        TradeSystemLocation system,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default);
}
