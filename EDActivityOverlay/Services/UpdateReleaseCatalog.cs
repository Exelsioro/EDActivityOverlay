using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace EDActivityOverlay.Services;

internal sealed record UpdateCatalogSnapshot(string Json, DateTimeOffset CheckedUtc);

/// <summary>Cache public metadata and fall back to Pages when the shared GitHub API quota is exhausted.</summary>
internal sealed class UpdateReleaseCatalog(HttpClient client, string? cachePath = null)
{
    internal static readonly Uri ApiUri = new("https://api.github.com/repos/Exelsioro/EDActivityOverlay/releases?per_page=100");
    internal static readonly Uri MirrorUri = new("https://exelsioro.github.io/EDActivityOverlay/updates.json");
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);
    private const int MaximumCatalogBytes = 4 * 1024 * 1024;
    private UpdateCatalogSnapshot? cached;
    private DateTimeOffset? apiRetryAfterUtc;

    internal async Task<UpdateCatalogSnapshot> FetchAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        cached ??= LoadCache();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!forceRefresh && cached is not null && cached.CheckedUtc <= now
            && now - cached.CheckedUtc < CacheLifetime)
            return cached;

        if (apiRetryAfterUtc is null || now >= apiRetryAfterUtc)
        {
            try { return SaveCache(await ReadAsync(ApiUri, cancellationToken)); }
            catch (Exception ex) when (IsSourceFailure(ex, cancellationToken))
            {
                Logger.Logger.Warning($"Update API unavailable; trying Pages: {ex.Message}");
            }
        }

        try { return SaveCache(await ReadAsync(MirrorUri, cancellationToken)); }
        catch (Exception ex) when (IsSourceFailure(ex, cancellationToken))
        {
            Logger.Logger.Warning($"Update Pages catalog unavailable: {ex.Message}");
            throw new HttpRequestException(Loc.Get("Loc_UPDATE_SOURCES_UNAVAILABLE"), ex);
        }
    }

    private static bool IsSourceFailure(Exception exception, CancellationToken token) =>
        exception is HttpRequestException or IOException or JsonException or InvalidDataException
        || (exception is OperationCanceledException && !token.IsCancellationRequested);

    private async Task<UpdateCatalogSnapshot> ReadAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (uri == ApiUri && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            DateTimeOffset retry = response.Headers.RetryAfter?.Date
                ?? DateTimeOffset.UtcNow + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(15));
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
                && long.TryParse(values.FirstOrDefault(), out long resetSeconds)
                && resetSeconds is > 0 and <= 253402300799)
                retry = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);
            apiRetryAfterUtc = retry > DateTimeOffset.UtcNow ? retry : DateTimeOffset.UtcNow.AddMinutes(1);
        }
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumCatalogBytes)
            throw new InvalidDataException("Update catalog exceeds the size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + read > MaximumCatalogBytes)
                throw new InvalidDataException("Update catalog exceeds the size limit.");
            buffer.Write(chunk, 0, read);
        }
        string json = Encoding.UTF8.GetString(buffer.ToArray());
        Validate(json);
        return new(json, DateTimeOffset.UtcNow);
    }

    private static void Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Update catalog is not a release list.");
    }

    private UpdateCatalogSnapshot? LoadCache()
    {
        if (cachePath is null) return null;
        try
        {
            if (!File.Exists(cachePath) || new FileInfo(cachePath).Length > MaximumCatalogBytes * 2)
                return null;
            var snapshot = JsonSerializer.Deserialize<UpdateCatalogSnapshot>(File.ReadAllText(cachePath));
            if (snapshot is not null) Validate(snapshot.Json);
            return snapshot;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Logger.Logger.Warning($"Update cache could not be read: {ex.Message}");
            return null;
        }
    }

    private UpdateCatalogSnapshot SaveCache(UpdateCatalogSnapshot snapshot)
    {
        cached = snapshot;
        if (cachePath is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                AtomicFileStorage.WriteAllText(cachePath, JsonSerializer.Serialize(snapshot));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Logger.Warning($"Update cache could not be saved: {ex.Message}");
            }
        }
        return snapshot;
    }
}
