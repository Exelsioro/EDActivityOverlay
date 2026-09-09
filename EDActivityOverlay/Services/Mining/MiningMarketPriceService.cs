using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.Services.Mining;

public sealed record MiningMarketPriceQuote(
    string CommodityId,
    int AverageSellPrice,
    int MedianSellPrice,
    int BestSellPrice,
    int SampleCount,
    DateTimeOffset NewestUpdateUtc)
{
    public int ReferenceSellPrice =>
        MedianSellPrice > 0
            ? MedianSellPrice
            : AverageSellPrice > 0
                ? AverageSellPrice
                : BestSellPrice;

    public bool Available => SampleCount > 0 && ReferenceSellPrice > 0;
}

public sealed record MiningMarketPriceSnapshot(
    long SystemAddress,
    string SystemName,
    DateTimeOffset UpdatedUtc,
    bool IsLoading,
    string Error,
    IReadOnlyDictionary<string, MiningMarketPriceQuote> Quotes)
{
    public static MiningMarketPriceSnapshot Empty { get; } = new(
        0,
        string.Empty,
        DateTimeOffset.MinValue,
        false,
        string.Empty,
        new Dictionary<string, MiningMarketPriceQuote>(StringComparer.OrdinalIgnoreCase));

    public bool TryGet(string? commodityId, out MiningMarketPriceQuote? quote)
    {
        quote = null;
        MiningTargetOption? option = MiningTargetCatalog.Find(commodityId);
        string key = option?.CommodityId ?? commodityId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return Quotes.TryGetValue(key, out quote) && quote.Available;
    }
}

public sealed class MiningMarketPriceChangedEventArgs(
    MiningMarketPriceSnapshot current) : EventArgs
{
    public MiningMarketPriceSnapshot Current { get; } = current;
}

public sealed class MiningMarketPriceService : IDisposable
{
    private static readonly TimeSpan RefreshTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxDataAge = TimeSpan.FromDays(3);
    private const int SearchRadiusLy = 80;
    private const int MaxConcurrency = 6;

    private readonly object sync = new();
    private readonly ArdentMarketDataProvider provider;
    private CancellationTokenSource? refreshCts;
    private MiningMarketPriceSnapshot current = MiningMarketPriceSnapshot.Empty;
    private string lastSignature = string.Empty;
    private bool disposed;

    public static MiningMarketPriceService Instance { get; } = new();

    public event EventHandler<MiningMarketPriceChangedEventArgs>? Changed;

    public MiningMarketPriceSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    internal MiningMarketPriceService()
        : this(new ArdentMarketDataProvider())
    {
    }

    internal MiningMarketPriceService(ArdentMarketDataProvider provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public void RequestRefresh(
        GameStateSnapshot state,
        IEnumerable<string> commodityIds,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(disposed, this);

        string[] requested = commodityIds
            .Select(item => MiningTargetCatalog.Find(item)?.CommodityId ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requested.Length == 0
            || (state.SystemAddress == 0 && string.IsNullOrWhiteSpace(state.StarSystem)))
        {
            return;
        }

        string signature = $"{state.SystemAddress}|{state.StarSystem}|{string.Join(',', requested)}";
        CancellationToken token;
        MiningMarketPriceSnapshot loading;

        lock (sync)
        {
            if (!force
                && signature.Equals(lastSignature, StringComparison.Ordinal)
                && current.IsLoading)
            {
                return;
            }

            if (!force
                && signature.Equals(lastSignature, StringComparison.Ordinal)
                && current.UpdatedUtc != DateTimeOffset.MinValue
                && DateTimeOffset.UtcNow - current.UpdatedUtc < RefreshTtl)
            {
                return;
            }

            lastSignature = signature;
            refreshCts?.Cancel();
            refreshCts?.Dispose();
            refreshCts = new CancellationTokenSource();
            token = refreshCts.Token;

            bool sameSystem =
                current.SystemAddress != 0 && state.SystemAddress != 0
                    ? current.SystemAddress == state.SystemAddress
                    : current.SystemName.Equals(state.StarSystem, StringComparison.OrdinalIgnoreCase);
            loading = current with
            {
                SystemAddress = state.SystemAddress,
                SystemName = state.StarSystem,
                IsLoading = true,
                Error = string.Empty,
                Quotes = sameSystem
                    ? current.Quotes
                    : new Dictionary<string, MiningMarketPriceQuote>(StringComparer.OrdinalIgnoreCase)
            };
            current = loading;
        }

        Changed?.Invoke(this, new MiningMarketPriceChangedEventArgs(loading));
        _ = RefreshCoreAsync(state.SystemAddress, state.StarSystem, requested, signature, token);
    }

    private async Task RefreshCoreAsync(
        long systemAddress,
        string systemName,
        IReadOnlyList<string> commodityIds,
        string signature,
        CancellationToken cancellationToken)
    {
        try
        {
            TradeSystemLocation origin = await provider.ResolveSystemAsync(
                new TradeSystemReference(systemName, systemAddress),
                cancellationToken).ConfigureAwait(false);

            var constraints = new TradeSearchConstraints
            {
                OriginSystemName = origin.SystemName,
                OriginSystemAddress = origin.SystemAddress,
                CargoCapacity = 1,
                TargetSearchRadiusLy = SearchRadiusLy,
                SourceSearchRadiusLy = 0,
                MaxDataAge = MaxDataAge,
                IncludeFleetCarriers = false,
                MinDemand = 1,
                MinSupply = 1
            };

            using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
            Task<MiningMarketPriceQuote>[] tasks = commodityIds
                .Select(async commodityId =>
                {
                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return await LoadQuoteAsync(
                            origin,
                            commodityId,
                            constraints,
                            cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        gate.Release();
                    }
                })
                .ToArray();

            MiningMarketPriceQuote[] loaded = await Task.WhenAll(tasks).ConfigureAwait(false);
            var quotes = loaded.ToDictionary(
                item => item.CommodityId,
                item => item,
                StringComparer.OrdinalIgnoreCase);

            Publish(
                signature,
                new MiningMarketPriceSnapshot(
                    origin.SystemAddress,
                    origin.SystemName,
                    DateTimeOffset.UtcNow,
                    false,
                    string.Empty,
                    quotes));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            MiningMarketPriceSnapshot failed;
            lock (sync)
            {
                if (!signature.Equals(lastSignature, StringComparison.Ordinal))
                {
                    return;
                }

                failed = current with
                {
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    IsLoading = false,
                    Error = ex.Message
                };
                current = failed;
            }

            Changed?.Invoke(this, new MiningMarketPriceChangedEventArgs(failed));
            Logger.Logger.Warning($"Mining market price refresh failed: {ex.Message}");
        }
    }

    private async Task<MiningMarketPriceQuote> LoadQuoteAsync(
        TradeSystemLocation origin,
        string commodityId,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken)
    {
        MiningTargetOption? option = MiningTargetCatalog.Find(commodityId);
        if (option is null)
        {
            return EmptyQuote(commodityId);
        }

        try
        {
            IReadOnlyList<TradeMarketOrder> orders = await provider.GetNearbyImportsAsync(
                origin,
                option.EnglishName,
                SearchRadiusLy,
                constraints,
                cancellationToken).ConfigureAwait(false);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            TradeMarketOrder[] eligible = orders
                .Where(item => item.SellToStationPrice > 0)
                .Where(item => !item.IsFleetCarrier)
                .Where(item => item.UpdatedAt != DateTimeOffset.MinValue
                    && now - item.UpdatedAt <= MaxDataAge)
                .ToArray();

            if (eligible.Length == 0)
            {
                return EmptyQuote(option.CommodityId);
            }

            int[] prices = eligible
                .Select(item => item.SellToStationPrice)
                .OrderBy(value => value)
                .ToArray();

            int median = prices.Length % 2 == 1
                ? prices[prices.Length / 2]
                : (int)Math.Round(
                    (prices[prices.Length / 2 - 1] + (long)prices[prices.Length / 2]) / 2d,
                    MidpointRounding.AwayFromZero);

            return new MiningMarketPriceQuote(
                option.CommodityId,
                (int)Math.Round(prices.Average(), MidpointRounding.AwayFromZero),
                median,
                prices[^1],
                prices.Length,
                eligible.Max(item => item.UpdatedAt));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"Mining price lookup failed for {option.CommodityId}: {ex.Message}");
            return EmptyQuote(option.CommodityId);
        }
    }

    private static MiningMarketPriceQuote EmptyQuote(string commodityId) => new(
        commodityId,
        0,
        0,
        0,
        0,
        DateTimeOffset.MinValue);

    private void Publish(string signature, MiningMarketPriceSnapshot snapshot)
    {
        lock (sync)
        {
            if (!signature.Equals(lastSignature, StringComparison.Ordinal))
            {
                return;
            }

            current = snapshot;
        }

        Changed?.Invoke(this, new MiningMarketPriceChangedEventArgs(snapshot));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (sync)
        {
            refreshCts?.Cancel();
            refreshCts?.Dispose();
            refreshCts = null;
        }
    }
}
