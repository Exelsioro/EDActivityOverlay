using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EDActivityOverlay.Services.Ardent;

public sealed partial class ArdentApiClient
{
    public Task<IReadOnlyList<ArdentMarketOrderDto>> GetSystemCommoditiesAsync(
        long systemAddress,
        CancellationToken cancellationToken = default)
    {
        if (systemAddress == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(systemAddress));
        }

        return
            GetArrayAsync<ArdentMarketOrderDto>(
                $"v2/system/address/{systemAddress}/commodities",
                TimeSpan.FromMinutes(2),
                cancellationToken);
    }
}
