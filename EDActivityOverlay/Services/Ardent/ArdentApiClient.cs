using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EDActivityOverlay.Services.Ardent;

public sealed class ArdentApiException : Exception
{
    public ArdentApiException(string path, HttpStatusCode statusCode, string detail)
        : base($"Ardent request '{path}' failed: HTTP {(int)statusCode} ({statusCode}). {detail}")
    {
        RequestPath = path;
        StatusCode = statusCode;
    }

    public string RequestPath { get; }
    public HttpStatusCode StatusCode { get; }
}

public sealed partial class ArdentApiClient
{
    private static readonly Uri DefaultBaseAddress = new("https://api.ardent-insight.com/");
    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;
    private readonly ArdentRequestCache cache;
    private readonly SemaphoreSlim requestGate;

    public ArdentApiClient()
        : this(SharedHttpClient, new ArdentRequestCache())
    {
    }

    public ArdentApiClient(
        HttpClient httpClient,
        ArdentRequestCache? cache = null,
        int maxConcurrentRequests = 8)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (maxConcurrentRequests < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentRequests));
        }

        this.httpClient = httpClient;
        this.httpClient.BaseAddress ??= DefaultBaseAddress;
        this.cache = cache ?? new ArdentRequestCache();
        requestGate = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
    }

    public Task<IReadOnlyList<ArdentCommodityReportDto>> GetCommoditiesAsync(
        CancellationToken cancellationToken = default) =>
        GetArrayAsync<ArdentCommodityReportDto>(
            "v2/commodities",
            TimeSpan.FromMinutes(30),
            cancellationToken);

    public Task<ArdentSystemDto> GetSystemAsync(
        ArdentSystemReference system,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(system);

        string path = system.HasAddress
            ? $"v2/system/address/{system.SystemAddress}"
            : $"v2/system/name/{Escape(system.Name)}";

        return GetObjectAsync<ArdentSystemDto>(
            path,
            TimeSpan.FromMinutes(30),
            cancellationToken);
    }

    public Task<IReadOnlyList<ArdentMarketOrderDto>> GetSystemCommodityAsync(
        long systemAddress,
        string commodityName,
        int maxDaysAgo,
        CancellationToken cancellationToken = default) =>
        GetArrayAsync<ArdentMarketOrderDto>(
            $"v2/system/address/{systemAddress}/commodity/name/{EscapeCommodity(commodityName)}?maxDaysAgo={Math.Max(1, maxDaysAgo)}",
            TimeSpan.FromMinutes(2),
            cancellationToken);

    public Task<IReadOnlyList<ArdentMarketOrderDto>> GetNearbyExportsAsync(
        long systemAddress,
        string commodityName,
        long minVolume,
        int maxDistanceLy,
        int maxDaysAgo,
        bool? fleetCarriers,
        CancellationToken cancellationToken = default) =>
        GetArrayAsync<ArdentMarketOrderDto>(
            $"v2/system/address/{systemAddress}/commodity/name/{EscapeCommodity(commodityName)}/nearby/exports"
            + BuildQuery(
                ("minVolume", Math.Max(1, minVolume).ToString()),
                ("maxDistance", Math.Clamp(maxDistanceLy, 0, 500).ToString()),
                ("maxDaysAgo", Math.Max(1, maxDaysAgo).ToString()),
                ("fleetCarriers", BooleanQuery(fleetCarriers))),
            TimeSpan.FromMinutes(2),
            cancellationToken);

    public Task<IReadOnlyList<ArdentMarketOrderDto>> GetNearbyImportsAsync(
        long systemAddress,
        string commodityName,
        long minVolume,
        int maxDistanceLy,
        int maxDaysAgo,
        bool? fleetCarriers,
        CancellationToken cancellationToken = default) =>
        GetArrayAsync<ArdentMarketOrderDto>(
            $"v2/system/address/{systemAddress}/commodity/name/{EscapeCommodity(commodityName)}/nearby/imports"
            + BuildQuery(
                ("minVolume", Math.Max(1, minVolume).ToString()),
                ("maxDistance", Math.Clamp(maxDistanceLy, 0, 500).ToString()),
                ("maxDaysAgo", Math.Max(1, maxDaysAgo).ToString()),
                ("fleetCarriers", BooleanQuery(fleetCarriers))),
            TimeSpan.FromMinutes(2),
            cancellationToken);

    private async Task<T> GetObjectAsync<T>(
        string path,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        string json = await GetJsonAsync(path, ttl, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Ardent returned an empty object for '{path}'.");
    }

    private async Task<IReadOnlyList<T>> GetArrayAsync<T>(
        string path,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        string json = await GetJsonAsync(path, ttl, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T[]>(json, JsonOptions) ?? Array.Empty<T>();
    }

    private async Task<string> GetJsonAsync(
        string path,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        if (cache.TryGet(path, out string cached))
        {
            return cached;
        }

        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cache.TryGet(path, out cached))
            {
                return cached;
            }

            using HttpResponseMessage response = await httpClient.GetAsync(
                path,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string preview = json.Length <= 400 ? json : json[..400];
                throw new ArdentApiException(path, response.StatusCode, preview);
            }

            cache.Set(path, json, ttl);
            return json;
        }
        finally
        {
            requestGate.Release();
        }
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("System name is required.", nameof(value));
        }

        return Uri.EscapeDataString(value.Trim());
    }

    private static string EscapeCommodity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Commodity name is required.", nameof(value));
        }

        return Uri.EscapeDataString(value.Trim().ToLowerInvariant());
    }

    private static string BuildQuery(params (string Key, string? Value)[] values)
    {
        string[] parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .Select(value => $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value!)}")
            .ToArray();

        return parts.Length == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static string? BooleanQuery(bool? value) =>
        value.HasValue ? (value.Value ? "true" : "false") : null;

    private static HttpClient CreateSharedHttpClient()
    {
        var client = new HttpClient
        {
            BaseAddress = DefaultBaseAddress,
            Timeout = TimeSpan.FromSeconds(15)
        };

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("EDActivityOverlay", "1.0"));

        return client;
    }
}
