using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EDActivityOverlay.Services.Trading;

public interface ITradeOriginMarketProvider
{
    Task<IReadOnlyList<TradeMarketOrder>> GetSystemOrdersAsync(
        TradeSystemLocation system,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default);
}
