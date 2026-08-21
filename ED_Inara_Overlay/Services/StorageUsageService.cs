using System.IO;

namespace ED_Inara_Overlay.Services;

public sealed record StorageUsageSnapshot(
    long InstallationBytes,
    long PersistentDataBytes,
    long DatabaseBytes,
    long CacheBytes,
    string PersistentDataDirectory,
    string CacheDirectory);

/// <summary>Reports application storage and removes only reproducible cache files.</summary>
public static class StorageUsageService
{
    public static string PersistentDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ED_Inara_Overlay");

    public static string CacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ED_Inara_Overlay");

    public static StorageUsageSnapshot Measure()
    {
        long persistent = DirectorySize(PersistentDataDirectory);
        long cache = DirectorySize(CacheDirectory);
        long database = FileFamilySize(Path.Combine(PersistentDataDirectory, "companion.db"));
        return new StorageUsageSnapshot(
            DirectorySize(AppContext.BaseDirectory), persistent, database, cache,
            PersistentDataDirectory, CacheDirectory);
    }

    public static int CleanupExpiredCaches(TimeSpan maximumAge)
    {
        int deleted = 0;
        string systemCache = Path.Combine(CacheDirectory, "exploration-system-cache");
        if (!Directory.Exists(systemCache)) return deleted;
        DateTime threshold = DateTime.UtcNow - maximumAge;
        foreach (string file in SafeFiles(systemCache))
        {
            try
            {
                FileInfo info = new(file);
                if (info.LastWriteTimeUtc <= threshold || info.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    info.Delete();
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Logger.Warning($"Cache cleanup skipped '{file}': {ex.Message}");
            }
        }
        return deleted;
    }

    private static long FileFamilySize(string path)
    {
        long total = 0;
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                var info = new FileInfo(path + suffix);
                if (info.Exists) total += info.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }

    private static long DirectorySize(string path)
    {
        long total = 0;
        foreach (string file in SafeFiles(path))
        {
            try { total += new FileInfo(file).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }

    private static IEnumerable<string> SafeFiles(string path)
    {
        if (!Directory.Exists(path)) yield break;
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(current);
                directories = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string file in files) yield return file;
            foreach (string directory in directories) pending.Push(directory);
        }
    }
}
