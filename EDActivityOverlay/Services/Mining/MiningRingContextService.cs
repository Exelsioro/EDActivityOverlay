using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Mining;

public sealed record MiningRingContextSnapshot(
    long SystemAddress,
    string SystemName,
    string RingName,
    string RingClass,
    string ReserveLevel,
    IReadOnlyList<string> HotspotCommodityIds)
{
    public static MiningRingContextSnapshot Empty { get; } = new(
        0,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        Array.Empty<string>());

    public bool Available => !string.IsNullOrWhiteSpace(RingName);
    public bool HasHotspots => HotspotCommodityIds.Count > 0;
}

public sealed class MiningRingContextChangedEventArgs(
    MiningRingContextSnapshot current) : EventArgs
{
    public MiningRingContextSnapshot Current { get; } = current;
}

public sealed class MiningRingContextService : IJournalDataConsumer, IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<string, MutableRingContext> rings =
        new(StringComparer.OrdinalIgnoreCase);

    private long currentSystemAddress;
    private string currentSystemName = string.Empty;
    private bool started;
    private bool disposed;

    public static MiningRingContextService Instance { get; } = new();

    public event EventHandler<MiningRingContextChangedEventArgs>? Changed;

    internal MiningRingContextService()
    {
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            return;
        }

        JournalMonitorService.Instance.Events.Register(this);
        started = true;
    }

    public MiningRingContextSnapshot Resolve(
        string? ringName,
        long systemAddress,
        string? systemName) =>
        Resolve(ringName, null, systemAddress, systemName);

    public MiningRingContextSnapshot Resolve(
        string? ringName,
        string? bodyName,
        long systemAddress,
        string? systemName)
    {
        string requestedRing = ringName?.Trim() ?? string.Empty;
        string requestedBody = bodyName?.Trim() ?? string.Empty;

        lock (sync)
        {
            if (!string.IsNullOrWhiteSpace(requestedRing))
            {
                if (rings.TryGetValue(Key(systemAddress, requestedRing), out MutableRingContext? exact))
                {
                    return exact.ToSnapshot();
                }

                MutableRingContext? byName = rings.Values
                    .Where(item => item.RingName.Equals(requestedRing, StringComparison.OrdinalIgnoreCase))
                    .Where(item => SameSystem(item, systemAddress, systemName))
                    .OrderByDescending(item => item.SystemAddress != 0 && item.SystemAddress == systemAddress)
                    .FirstOrDefault();

                return byName?.ToSnapshot()
                    ?? new MiningRingContextSnapshot(
                        systemAddress,
                        systemName?.Trim() ?? string.Empty,
                        requestedRing,
                        string.Empty,
                        string.Empty,
                        Array.Empty<string>());
            }

            MutableRingContext[] systemRings = rings.Values
                .Where(item => SameSystem(item, systemAddress, systemName))
                .ToArray();

            if (!string.IsNullOrWhiteSpace(requestedBody))
            {
                MutableRingContext[] bodyRings = systemRings
                    .Where(item => IsRingOfBody(item.RingName, requestedBody))
                    .ToArray();

                MiningRingContextSnapshot? inferred = ResolveUnambiguous(bodyRings);
                if (inferred is not null)
                {
                    return inferred;
                }

                // A planet can have several rings. A single DSS-hotspot-bearing ring is
                // stronger evidence than guessing the A/B ring from the parent body.
                MutableRingContext[] hotspotRings = bodyRings
                    .Where(item => item.Hotspots.Count > 0)
                    .ToArray();
                inferred = ResolveUnambiguous(hotspotRings);
                if (inferred is not null)
                {
                    return inferred;
                }

                return MiningRingContextSnapshot.Empty;
            }

            return ResolveUnambiguous(systemRings)
                ?? MiningRingContextSnapshot.Empty;
        }
    }

    private static MiningRingContextSnapshot? ResolveUnambiguous(
        IReadOnlyList<MutableRingContext> candidates) =>
        candidates.Count == 1
            ? candidates[0].ToSnapshot()
            : null;

    private static bool SameSystem(
        MutableRingContext item,
        long systemAddress,
        string? systemName)
    {
        if (systemAddress != 0 && item.SystemAddress != 0)
        {
            return item.SystemAddress == systemAddress;
        }

        string name = systemName?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(item.SystemName)
            || item.SystemName.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRingOfBody(string ringName, string bodyName)
    {
        if (string.IsNullOrWhiteSpace(ringName) || string.IsNullOrWhiteSpace(bodyName))
        {
            return false;
        }

        if (ringName.Equals(bodyName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ringName.StartsWith(bodyName + " ", StringComparison.OrdinalIgnoreCase)
               && ringName.EndsWith(" Ring", StringComparison.OrdinalIgnoreCase);
    }

    public void OnJournalEvent(JournalEventReceivedEventArgs journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);

        MiningRingContextSnapshot? changed = null;
        lock (sync)
        {
            switch (journalEvent.EventName.Trim().ToLowerInvariant())
            {
                case "location":
                case "fsdjump":
                case "carrierjump":
                    currentSystemAddress = Int64(journalEvent.Data, "SystemAddress", currentSystemAddress);
                    currentSystemName = Text(journalEvent.Data, "StarSystem", currentSystemName);
                    break;

                case "scan":
                    changed = ApplyScan(journalEvent.Data);
                    break;

                case "saasignalsfound":
                    changed = ApplySaaSignals(journalEvent.Data);
                    break;
            }
        }

        if (changed is not null)
        {
            Changed?.Invoke(this, new MiningRingContextChangedEventArgs(changed));
        }
    }

    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile)
    {
    }

    private MiningRingContextSnapshot? ApplyScan(JsonElement root)
    {
        if (!root.TryGetProperty("Rings", out JsonElement source)
            || source.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        long systemAddress = Int64(root, "SystemAddress", currentSystemAddress);
        string systemName = Text(root, "StarSystem", currentSystemName);
        string reserve = Text(root, "ReserveLevel");
        MiningRingContextSnapshot? last = null;

        foreach (JsonElement ring in source.EnumerateArray())
        {
            string name = Text(ring, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string key = Key(systemAddress, name);
            if (!rings.TryGetValue(key, out MutableRingContext? context))
            {
                context = new MutableRingContext
                {
                    SystemAddress = systemAddress,
                    SystemName = systemName,
                    RingName = name
                };
                rings[key] = context;
            }

            context.SystemAddress = systemAddress;
            context.SystemName = systemName;
            context.RingName = name;
            context.RingClass = Text(ring, "RingClass", context.RingClass);
            context.ReserveLevel = reserve.Length > 0 ? reserve : context.ReserveLevel;
            last = context.ToSnapshot();
        }

        return last;
    }

    private MiningRingContextSnapshot? ApplySaaSignals(JsonElement root)
    {
        string ringName = Text(root, "BodyName");
        if (string.IsNullOrWhiteSpace(ringName))
        {
            return null;
        }

        long systemAddress = Int64(root, "SystemAddress", currentSystemAddress);
        string key = Key(systemAddress, ringName);
        if (!rings.TryGetValue(key, out MutableRingContext? context))
        {
            context = new MutableRingContext
            {
                SystemAddress = systemAddress,
                SystemName = currentSystemName,
                RingName = ringName
            };
            rings[key] = context;
        }

        if (root.TryGetProperty("Signals", out JsonElement source)
            && source.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement signal in source.EnumerateArray())
            {
                string raw = Text(signal, "Type");
                string localized = Text(signal, "Type_Localised");
                string candidate = CleanHotspotType(raw);

                MiningTargetOption? option = MiningTargetCatalog.Find(candidate)
                    ?? MiningTargetCatalog.Find(localized);
                if (option is not null && !string.IsNullOrWhiteSpace(option.CommodityId))
                {
                    context.Hotspots.Add(option.CommodityId);
                }
            }
        }

        return context.ToSnapshot();
    }

    internal static string CleanHotspotType(string? value)
    {
        string text = value?.Trim() ?? string.Empty;
        const string prefix = "$SAA_SignalType_";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[prefix.Length..];
        }

        text = text.Trim().TrimEnd(';');
        if (text.Equals("LowTemperatureDiamonds", StringComparison.OrdinalIgnoreCase))
        {
            return "LowTemperatureDiamond";
        }

        if (text.Equals("VoidOpals", StringComparison.OrdinalIgnoreCase)
            || text.Equals("VoidOpal", StringComparison.OrdinalIgnoreCase))
        {
            return "Opal";
        }

        return text;
    }

    private static string Key(long systemAddress, string ringName) =>
        $"{systemAddress}:{ringName.Trim()}";

    private static string Text(JsonElement root, string property, string fallback = "")
    {
        if (!root.TryGetProperty(property, out JsonElement value))
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? fallback
            : fallback;
    }

    private static long Int64(JsonElement root, string property, long fallback)
    {
        if (!root.TryGetProperty(property, out JsonElement value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long numeric))
        {
            return numeric;
        }

        return value.ValueKind == JsonValueKind.String
               && long.TryParse(value.GetString(), out numeric)
            ? numeric
            : fallback;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (started)
        {
            JournalMonitorService.Instance.Events.Unregister(this);
            started = false;
        }
    }

    private sealed class MutableRingContext
    {
        public long SystemAddress { get; set; }
        public string SystemName { get; set; } = string.Empty;
        public string RingName { get; set; } = string.Empty;
        public string RingClass { get; set; } = string.Empty;
        public string ReserveLevel { get; set; } = string.Empty;
        public HashSet<string> Hotspots { get; } = new(StringComparer.OrdinalIgnoreCase);

        public MiningRingContextSnapshot ToSnapshot() => new(
            SystemAddress,
            SystemName,
            RingName,
            RingClass,
            ReserveLevel,
            Hotspots.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
