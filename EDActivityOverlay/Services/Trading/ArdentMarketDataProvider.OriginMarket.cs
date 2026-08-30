using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EDActivityOverlay.Services.Ardent;

namespace EDActivityOverlay.Services.Trading;

public sealed partial class ArdentMarketDataProvider : ITradeOriginMarketProvider
{
    public async Task<IReadOnlyList<TradeMarketOrder>> GetSystemOrdersAsync(
        TradeSystemLocation system,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            system);

        ArgumentNullException.ThrowIfNull(
            constraints);

        IReadOnlyList<ArdentMarketOrderDto> rows =
            await client.GetSystemCommoditiesAsync(
                    system.SystemAddress,
                    cancellationToken)
                .ConfigureAwait(
                    false);

        return
            rows
                .Select(
                    row =>
                        Map(
                            row,
                            referenceDistanceLy: 0))
                .ToArray();
    }
}
