using System.IO;
using System.Text.Json;

namespace EDActivityOverlay.Services.Mining;

public sealed record MiningDestinationSnapshot
{
    public string SystemName { get; init; } = string.Empty;
    public string BodyName { get; init; } = string.Empty;
    public string RingDisplayName { get; init; } = string.Empty;
    public string RingName { get; init; } = string.Empty;
    public string RingClass { get; init; } = string.Empty;
    public string ReserveLevel { get; init; } = string.Empty;
    public double DistanceLy { get; init; }
    public double DistanceToArrivalLs { get; init; }
    public string PrimaryCommodityId { get; init; } = string.Empty;
    public IReadOnlyList<string> TargetCommodityIds { get; init; } = Array.Empty<string>();
    public int OverlapMultiplier { get; init; }
    public MiningResSiteType ResType { get; init; }
    public string QualityCommodityId { get; init; } = string.Empty;
    public double MeasuredAverageContentPercent { get; init; }
    public string QualitySource { get; init; } = string.Empty;
    public DateTimeOffset SelectedUtc { get; init; }

    public static MiningDestinationSnapshot Empty { get; } = new();

    public bool Available =>
        !string.IsNullOrWhiteSpace(SystemName)
        && !string.IsNullOrWhiteSpace(RingName);

    public static MiningDestinationSnapshot FromCandidate(
        MiningLocationCandidate candidate,
        DateTimeOffset? selectedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        MiningResSiteType bestRes = candidate.SpecialSites
            .Select(item => item.ResType)
            .DefaultIfEmpty(MiningResSiteType.None)
            .Max();

        int overlap = candidate.SpecialSites
            .Select(item => item.OverlapMultiplier)
            .DefaultIfEmpty(0)
            .Max();

        MiningLocationQualitySite? bestQuality = candidate.QualitySites
            .OrderByDescending(item => item.AverageContentPercent)
            .FirstOrDefault();

        return new MiningDestinationSnapshot
        {
            SystemName = candidate.SystemName.Trim(),
            BodyName = ShortBodyName(candidate.SystemName, candidate.BodyName),
            RingDisplayName = RingDesignation(
                candidate.SystemName,
                candidate.BodyName,
                candidate.RingName),
            RingName = candidate.RingName.Trim(),
            RingClass = candidate.RingClass.Trim(),
            ReserveLevel = candidate.ReserveLevel.Trim(),
            DistanceLy = Math.Max(0, candidate.DistanceLy),
            DistanceToArrivalLs = Math.Max(0, candidate.DistanceToArrivalLs),
            PrimaryCommodityId = candidate.PrimaryCommodityId,
            TargetCommodityIds = candidate.HotspotCounts.Keys
                .Select(item => MiningTargetCatalog.Find(item)?.CommodityId ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            OverlapMultiplier = Math.Max(0, overlap),
            ResType = bestRes,
            QualityCommodityId = bestQuality?.CommodityId ?? string.Empty,
            MeasuredAverageContentPercent =
                Math.Max(0, bestQuality?.AverageContentPercent ?? 0),
            QualitySource = bestQuality?.Source ?? string.Empty,
            SelectedUtc = selectedUtc ?? DateTimeOffset.UtcNow
        };
    }

    internal static string ShortBodyName(string systemName, string bodyName)
    {
        string system = systemName?.Trim() ?? string.Empty;
        string body = bodyName?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(system)
            && body.StartsWith(system, StringComparison.OrdinalIgnoreCase))
        {
            body = body[system.Length..].Trim();
        }

        return body;
    }

    internal static string ShortRingName(string systemName, string ringName)
    {
        string system = systemName?.Trim() ?? string.Empty;
        string ring = ringName?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(system)
            && ring.StartsWith(system, StringComparison.OrdinalIgnoreCase))
        {
            ring = ring[system.Length..].Trim();
        }

        return ring;
    }

    internal static string RingDesignation(
        string systemName,
        string bodyName,
        string ringName)
    {
        string body = ShortBodyName(systemName, bodyName);
        string ring = ShortRingName(systemName, ringName);

        if (!string.IsNullOrWhiteSpace(body)
            && ring.StartsWith(body, StringComparison.OrdinalIgnoreCase))
        {
            string suffix = ring[body.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                return suffix;
            }
        }

        return ring;
    }
}

public sealed class MiningDestinationChangedEventArgs(
    MiningDestinationSnapshot current) : EventArgs
{
    public MiningDestinationSnapshot Current { get; } = current;
}

public sealed class MiningDestinationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object sync = new();
    private readonly string storagePath;
    private MiningDestinationSnapshot current;

    public static MiningDestinationService Instance { get; } = new();

    public event EventHandler<MiningDestinationChangedEventArgs>? Changed;

    public MiningDestinationSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public MiningDestinationService()
        : this(DefaultStoragePath())
    {
    }

    internal MiningDestinationService(string storagePath)
    {
        this.storagePath = storagePath;
        current = Load(storagePath);
    }

    public void Select(MiningLocationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Publish(MiningDestinationSnapshot.FromCandidate(candidate), persist: true);
    }

    public void Clear()
    {
        Publish(MiningDestinationSnapshot.Empty, persist: true);
    }

    private void Publish(MiningDestinationSnapshot value, bool persist)
    {
        lock (sync)
        {
            current = value;
        }

        if (persist)
        {
            Persist(value);
        }

        Changed?.Invoke(this, new MiningDestinationChangedEventArgs(value));
    }

    private void Persist(MiningDestinationSnapshot value)
    {
        try
        {
            string? directory = Path.GetDirectoryName(storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!value.Available)
            {
                if (File.Exists(storagePath))
                {
                    File.Delete(storagePath);
                }

                return;
            }

            File.WriteAllText(
                storagePath,
                JsonSerializer.Serialize(value, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"Mining destination persistence failed: {ex.Message}");
        }
    }

    private static MiningDestinationSnapshot Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return MiningDestinationSnapshot.Empty;
            }

            MiningDestinationSnapshot? loaded =
                JsonSerializer.Deserialize<MiningDestinationSnapshot>(
                    File.ReadAllText(path),
                    JsonOptions);

            return loaded?.Available == true
                ? loaded
                : MiningDestinationSnapshot.Empty;
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"Mining destination load failed: {ex.Message}");
            return MiningDestinationSnapshot.Empty;
        }
    }

    private static string DefaultStoragePath()
    {
        string appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        return Path.Combine(
            appData,
            "EDActivityOverlay",
            "mining-destination.json");
    }
}
