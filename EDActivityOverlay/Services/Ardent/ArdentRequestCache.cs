using System.Collections.Concurrent;

namespace EDActivityOverlay.Services.Ardent;

public sealed class ArdentRequestCache
{
    private sealed record Entry(string Json, DateTimeOffset ExpiresUtc);

    private readonly ConcurrentDictionary<string, Entry> entries =
        new(StringComparer.Ordinal);

    public bool TryGet(string key, out string json)
    {
        if (entries.TryGetValue(key, out Entry? entry))
        {
            if (entry.ExpiresUtc > DateTimeOffset.UtcNow)
            {
                json = entry.Json;
                return true;
            }

            entries.TryRemove(key, out _);
        }

        json = string.Empty;
        return false;
    }

    public void Set(string key, string json, TimeSpan ttl)
    {
        if (ttl > TimeSpan.Zero)
        {
            entries[key] = new Entry(json, DateTimeOffset.UtcNow + ttl);
        }
    }
}
