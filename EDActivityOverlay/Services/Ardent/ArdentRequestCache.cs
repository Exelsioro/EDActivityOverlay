using System.Collections.Concurrent;

namespace EDActivityOverlay.Services.Ardent;

public sealed class ArdentRequestCache
{
    private sealed record Entry(string Json, DateTimeOffset ExpiresUtc);

    private readonly ConcurrentDictionary<string, Entry> entries =
        new(StringComparer.Ordinal);
    private readonly object insertionGate = new();

    public bool TryGet(string key, out string json)
    {
        if (entries.TryGetValue(key, out Entry? entry))
        {
            if (entry.ExpiresUtc > DateTimeOffset.UtcNow)
            {
                json = entry.Json;
                return true;
            }

            ((ICollection<KeyValuePair<string, Entry>>)entries).Remove(new(key, entry));
        }

        json = string.Empty;
        return false;
    }

    public void Remove(string key, string json)
    {
        if (entries.TryGetValue(key, out Entry? entry) && entry.Json == json)
            ((ICollection<KeyValuePair<string, Entry>>)entries).Remove(new(key, entry));
    }

    public void Set(string key, string json, TimeSpan ttl)
    {
        lock (insertionGate)
        {
            if (ttl > TimeSpan.Zero)
            {
                // Expired keys are otherwise retained indefinitely when searches
                // move to different systems. Bound both entry count and payload size.
                foreach (var item in entries)
                    if (item.Value.ExpiresUtc <= DateTimeOffset.UtcNow)
                        ((ICollection<KeyValuePair<string, Entry>>)entries).Remove(item);

                if (json.Length > 4 * 1024 * 1024) return;
                while (entries.Count >= 128 || entries.Sum(item => (long)item.Value.Json.Length) + json.Length > 16 * 1024 * 1024)
                {
                    var oldest = entries.OrderBy(item => item.Value.ExpiresUtc).FirstOrDefault();
                    if (oldest.Key is null) break;
                    ((ICollection<KeyValuePair<string, Entry>>)entries).Remove(oldest);
                }
                entries[key] = new Entry(json, DateTimeOffset.UtcNow + ttl);
            }
        }
    }
}
