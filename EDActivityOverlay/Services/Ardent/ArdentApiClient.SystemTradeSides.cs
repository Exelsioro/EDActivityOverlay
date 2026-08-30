namespace EDActivityOverlay.Services.Ardent;

public sealed partial class ArdentApiClient
{
    public Task<IReadOnlyList<ArdentMarketOrderDto>> GetSystemExportsAsync(
        long systemAddress,
        long minVolume,
        int maxDaysAgo,
        bool? fleetCarriers,
        CancellationToken cancellationToken = default)
    {
        if (systemAddress == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(systemAddress));
        }

        return GetArrayAsync<ArdentMarketOrderDto>(
            $"v2/system/address/{systemAddress}/commodities/exports"
            + BuildQuery(
                ("minVolume", Math.Max(1, minVolume).ToString()),
                ("maxDaysAgo", Math.Max(1, maxDaysAgo).ToString()),
                ("fleetCarriers", BooleanQuery(fleetCarriers))),
            TimeSpan.FromMinutes(2),
            cancellationToken);
    }

    public Task<IReadOnlyList<ArdentMarketOrderDto>> GetSystemImportsAsync(
        long systemAddress,
        long minVolume,
        int maxDaysAgo,
        bool? fleetCarriers,
        CancellationToken cancellationToken = default)
    {
        if (systemAddress == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(systemAddress));
        }

        return GetArrayAsync<ArdentMarketOrderDto>(
            $"v2/system/address/{systemAddress}/commodities/imports"
            + BuildQuery(
                ("minVolume", Math.Max(1, minVolume).ToString()),
                ("minPrice", "1"),
                ("maxDaysAgo", Math.Max(1, maxDaysAgo).ToString()),
                ("fleetCarriers", BooleanQuery(fleetCarriers))),
            TimeSpan.FromMinutes(2),
            cancellationToken);
    }
}
