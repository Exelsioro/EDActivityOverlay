using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal sealed class ExplorationSystemCache
{
    private readonly string directory;

    public ExplorationSystemCache(string? directory = null)
    {
        this.directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ED_Inara_Overlay",
            "exploration-system-cache");
    }

    public async Task<ExplorationSystemDataSnapshot?> LoadAsync(
        long systemAddress,
        string systemName,
        TimeSpan maximumAge,
        bool allowStale,
        CancellationToken cancellationToken)
    {
        string path = GetPath(systemAddress, systemName);
        try
        {
            if (!File.Exists(path)) return null;
            await using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ExplorationSystemDataSnapshot? snapshot = await JsonSerializer.DeserializeAsync<ExplorationSystemDataSnapshot>(
                stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (snapshot is null) return null;
            bool stale = DateTimeOffset.UtcNow - snapshot.FetchedUtc > maximumAge;
            return stale && !allowStale ? null : snapshot with { FromCache = true, IsStale = stale };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Logger.Warning($"Exploration cache read failed: {ex.Message}");
            return null;
        }
    }

    public async Task SaveAsync(ExplorationSystemDataSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string path = GetPath(snapshot.SystemAddress, snapshot.SystemName);
            string temporary = path + ".tmp";
            await using (FileStream stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot with { FromCache = false, IsStale = false },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Logger.Warning($"Exploration cache write failed: {ex.Message}");
        }
    }

    private string GetPath(long systemAddress, string systemName)
    {
        string key = systemAddress > 0
            ? systemAddress.ToString()
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(systemName))).ToLowerInvariant();
        return Path.Combine(directory, key + ".json");
    }
}
