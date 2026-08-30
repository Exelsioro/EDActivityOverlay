using EDActivityOverlay.Services.Ardent;

namespace EDActivityOverlay.Services.Trading;

public sealed partial class ArdentMarketDataProvider : ITradeSystemTradeSidesProvider
{
    public async Task<IReadOnlyList<TradeMarketOrder>> GetSystemExportsAsync(
        TradeSystemLocation system,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(constraints);

        IReadOnlyList<ArdentMarketOrderDto> rows =
            await client.GetSystemExportsAsync(
                    system.SystemAddress,
                    constraints.MinSupply,
                    constraints.ApiMaxDaysAgo,
                    constraints.IncludeFleetCarriers ? null : false,
                    cancellationToken)
                .ConfigureAwait(false);

        return rows
            .Select(row => Map(row, referenceDistanceLy: 0))
            .ToArray();
    }

    public async Task<IReadOnlyList<TradeMarketOrder>> GetSystemImportsAsync(
        TradeSystemLocation system,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(constraints);

        IReadOnlyList<ArdentMarketOrderDto> rows =
            await client.GetSystemImportsAsync(
                    system.SystemAddress,
                    constraints.MinDemand,
                    constraints.ApiMaxDaysAgo,
                    constraints.IncludeFleetCarriers ? null : false,
                    cancellationToken)
                .ConfigureAwait(false);

        return rows
            .Select(row => Map(row, referenceDistanceLy: 0))
            .ToArray();
    }
}
