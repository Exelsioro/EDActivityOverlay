using System.IO;
using System.Text.Json;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Exploration;

internal sealed class ExplorationPoiCache
{
    private readonly object sync = new();
    private readonly string path;
    private Dictionary<string, CacheEntry> entries;

    public ExplorationPoiCache(string? cachePath = null)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EDActivityOverlay");
        Directory.CreateDirectory(directory);
        path = cachePath ?? Path.Combine(directory, "exploration-poi-cache.json");
        entries = Load();
    }

    public bool TryGet(string key, TimeSpan maximumAge, out ExplorationPoiState state)
    {
        lock (sync)
        {
            if (entries.TryGetValue(key, out CacheEntry? entry)
                && DateTimeOffset.UtcNow - entry.StoredUtc <= maximumAge)
            {
                state = entry.State;
                return true;
            }
        }
        state = ExplorationPoiState.Idle;
        return false;
    }

    public void Put(string key, ExplorationPoiState state)
    {
        lock (sync)
        {
            entries[key] = new CacheEntry(DateTimeOffset.UtcNow, state);
            // Keep the cache bounded; one entry per recently visited coordinate/rating combination.
            entries = entries.OrderByDescending(pair => pair.Value.StoredUtc).Take(200)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(entries));
            }
            catch (Exception ex)
            {
                Logger.Logger.Warning($"Exploration POI cache could not be saved: {ex.Message}");
            }
        }
    }

    private Dictionary<string, CacheEntry> Load()
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
            return JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(path))
                   ?? new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        }
    }

    private sealed record CacheEntry(DateTimeOffset StoredUtc, ExplorationPoiState State);
}
