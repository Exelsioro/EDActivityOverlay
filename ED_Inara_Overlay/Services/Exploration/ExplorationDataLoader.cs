using ED_Inara_Overlay.Models;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace ED_Inara_Overlay.Services.Exploration;

internal sealed class ExplorationDataLoader(
    ExplorationSystemCache cache,
    IReadOnlyList<IExplorationSystemProvider> providers)
{
    public async Task<ExplorationSystemDataSnapshot?> LoadAsync(
        long systemAddress,
        string systemName,
        TimeSpan cacheLifetime,
        bool allowEdsmFallback,
        bool bypassFreshCache,
        CancellationToken cancellationToken)
    {
        if (!bypassFreshCache)
        {
            ExplorationSystemDataSnapshot? cached = await cache.LoadAsync(
                systemAddress, systemName, cacheLifetime, false, cancellationToken).ConfigureAwait(false);
            if (cached is not null) return cached;
        }

        foreach (IExplorationSystemProvider provider in providers)
        {
            if (!allowEdsmFallback && provider.Name.Equals("EDSM", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                ExplorationSystemDataSnapshot? result = await provider.GetSystemAsync(
                    systemAddress, systemName, cancellationToken).ConfigureAwait(false);
                if (result is null) continue;
                await cache.SaveAsync(result, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TimeoutException)
            {
                Logger.Logger.Warning($"{provider.Name} exploration lookup failed: {ex.Message}");
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.Logger.Warning($"{provider.Name} exploration lookup timed out: {ex.Message}");
            }
        }

        return await cache.LoadAsync(
            systemAddress, systemName, cacheLifetime, true, cancellationToken).ConfigureAwait(false);
    }
}
