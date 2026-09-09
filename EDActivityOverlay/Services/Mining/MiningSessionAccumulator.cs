using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.Services.Mining;

internal sealed record MiningAccumulatorResult(
    bool Changed,
    MiningSessionSnapshot Current,
    MiningSessionSnapshot? CompletedSession = null);

internal sealed class MiningSessionAccumulator
{
    private string commander = string.Empty;
    private long systemAddress;
    private string systemName = string.Empty;
    private int bodyId = -1;
    private string bodyName = string.Empty;
    private string ringName = string.Empty;
    private int cargoUsed;
    private int cargoCapacity;
    private int limpetsRemaining;
    private ActiveSession? active;

    public MiningSessionSnapshot Current =>
        active?.ToSnapshot(MiningSessionState.Active, null, MiningSessionEndReason.None)
        ?? MiningSessionSnapshot.Empty;

    public MiningAccumulatorResult Apply(JournalEventReceivedEventArgs journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);

        string eventName = journalEvent.EventName.Trim().ToLowerInvariant();
        JsonElement root = journalEvent.Data;
        DateTimeOffset timestamp = journalEvent.Timestamp;

        switch (eventName)
        {
            case "loadgame":
            {
                MiningAccumulatorResult boundary = Finish(timestamp, MiningSessionEndReason.LoadGame);
                commander = GetString(root, "Commander", commander);
                systemAddress = 0;
                systemName = string.Empty;
                bodyId = -1;
                bodyName = string.Empty;
                ringName = string.Empty;
                cargoUsed = 0;
                cargoCapacity = 0;
                limpetsRemaining = 0;
                return boundary;
            }
            case "location":
                return ApplyLocation(root, timestamp);
            case "fsdjump":
            {
                MiningAccumulatorResult boundary = Finish(timestamp, MiningSessionEndReason.Jump);
                UpdateSystem(root);
                ClearBodyContext();
                return boundary;
            }
            case "carrierjump":
            {
                MiningAccumulatorResult boundary = Finish(timestamp, MiningSessionEndReason.CarrierJump);
                UpdateSystem(root);
                ClearBodyContext();
                return boundary;
            }
            case "supercruiseentry":
            {
                MiningAccumulatorResult boundary = Finish(timestamp, MiningSessionEndReason.SupercruiseEntry);
                ClearBodyContext();
                return boundary;
            }
            case "supercruiseexit":
                ApplyBodyContext(root);
                return ActiveContextChanged();
            case "docked":
                return Finish(timestamp, MiningSessionEndReason.Docked);
            case "died":
            {
                MiningAccumulatorResult boundary = Finish(timestamp, MiningSessionEndReason.Died);
                cargoUsed = 0;
                limpetsRemaining = 0;
                return boundary;
            }
            case "shutdown":
                return Finish(timestamp, MiningSessionEndReason.Shutdown);
            case "loadout":
                return ApplyLoadout(root);
            case "cargo":
                return ApplyCargo(root);
            case "launchdrone":
                return ApplyLaunchDrone(root, timestamp);
            case "prospectedasteroid":
                return ApplyProspect(root, timestamp);
            case "miningrefined":
                return ApplyRefinement(root, timestamp);
            case "asteroidcracked":
                return ApplyAsteroidCracked(timestamp);
            default:
                return Unchanged();
        }
    }

    public MiningAccumulatorResult ApplyCompanion(CompanionFileReceivedEventArgs companionFile)
    {
        ArgumentNullException.ThrowIfNull(companionFile);
        return companionFile.FileName.Equals("Cargo.json", StringComparison.OrdinalIgnoreCase)
            ? ApplyCargo(companionFile.Data)
            : Unchanged();
    }

    private MiningAccumulatorResult ApplyLocation(JsonElement root, DateTimeOffset timestamp)
    {
        long nextAddress = TryGetInt64(root, "SystemAddress", systemAddress);
        string nextName = GetString(root, "StarSystem", systemName);
        bool changedSystem = active is not null
            && !SameSystem(active.SystemAddress, active.SystemName, nextAddress, nextName);

        MiningAccumulatorResult boundary = changedSystem
            ? Finish(timestamp, MiningSessionEndReason.SystemChanged)
            : Unchanged();

        systemAddress = nextAddress;
        systemName = nextName;
        if (changedSystem)
        {
            ClearBodyContext();
        }
        else if (active is not null)
        {
            active.SystemAddress = systemAddress;
            active.SystemName = systemName;
        }

        return boundary;
    }

    private void UpdateSystem(JsonElement root)
    {
        systemAddress = TryGetInt64(root, "SystemAddress", systemAddress);
        systemName = GetString(root, "StarSystem", systemName);
    }

    private void ApplyBodyContext(JsonElement root)
    {
        bodyId = TryGetInt32(root, "BodyID", bodyId);
        string nextBody = GetString(root, "Body", bodyName);
        string bodyType = GetString(root, "BodyType");
        bodyName = nextBody;
        ringName = IsRing(bodyType, nextBody) ? nextBody : string.Empty;

        if (active is not null)
        {
            active.BodyId = bodyId;
            active.BodyName = bodyName;
            active.RingName = ringName;
        }
    }

    private MiningAccumulatorResult ApplyLoadout(JsonElement root)
    {
        int nextCapacity = TryGetInt32(root, "CargoCapacity", cargoCapacity);
        if (nextCapacity == cargoCapacity)
        {
            return Unchanged();
        }

        cargoCapacity = Math.Max(0, nextCapacity);
        if (active is null)
        {
            return Unchanged();
        }

        active.CargoCapacity = cargoCapacity;
        return Changed();
    }

    private MiningAccumulatorResult ApplyCargo(JsonElement root)
    {
        int nextCargoUsed = TryGetInt32(root, "Count", cargoUsed);
        int nextLimpets = 0;
        bool inventoryPresent = false;

        if (root.TryGetProperty("Inventory", out JsonElement inventory)
            && inventory.ValueKind == JsonValueKind.Array)
        {
            inventoryPresent = true;
            nextCargoUsed = 0;
            foreach (JsonElement item in inventory.EnumerateArray())
            {
                int count = Math.Max(0, TryGetInt32(item, "Count"));
                nextCargoUsed += count;
                string commodityId = CommodityIdentity.Normalize(GetString(item, "Name"));
                if (commodityId.Equals("drones", StringComparison.OrdinalIgnoreCase))
                {
                    nextLimpets += count;
                }
            }
        }

        if (!inventoryPresent)
        {
            nextCargoUsed = Math.Max(0, nextCargoUsed);
            nextLimpets = limpetsRemaining;
        }

        bool changed = nextCargoUsed != cargoUsed || nextLimpets != limpetsRemaining;
        cargoUsed = nextCargoUsed;
        limpetsRemaining = nextLimpets;

        if (!changed || active is null)
        {
            return Unchanged();
        }

        active.CargoUsed = cargoUsed;
        active.LimpetsRemaining = limpetsRemaining;
        return Changed();
    }

    private MiningAccumulatorResult ApplyLaunchDrone(JsonElement root, DateTimeOffset timestamp)
    {
        string type = GetString(root, "Type");
        if (type.Equals("Prospector", StringComparison.OrdinalIgnoreCase))
        {
            ActiveSession session = EnsureActive(timestamp);
            session.ProspectorsLaunched++;
            session.LastActivityUtc = timestamp;
            return Changed();
        }

        if (type.Equals("Collection", StringComparison.OrdinalIgnoreCase)
            && active is not null)
        {
            active.CollectorsLaunched++;
            active.LastActivityUtc = timestamp;
            return Changed();
        }

        return Unchanged();
    }

    private MiningAccumulatorResult ApplyProspect(JsonElement root, DateTimeOffset timestamp)
    {
        ActiveSession session = EnsureActive(timestamp);
        int sequence = session.Prospects.Count + 1;

        CommodityValue motherlode = ReadCommodity(root, "MotherlodeMaterial");
        var materials = new List<MiningProspectMaterialSnapshot>();
        if (root.TryGetProperty("Materials", out JsonElement source)
            && source.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement material in source.EnumerateArray())
            {
                CommodityValue commodity = ReadCommodity(material, "Name");
                if (string.IsNullOrWhiteSpace(commodity.Id)
                    && string.IsNullOrWhiteSpace(commodity.DisplayName))
                {
                    continue;
                }

                materials.Add(new MiningProspectMaterialSnapshot(
                    commodity.Id,
                    commodity.DisplayName,
                    Math.Max(0, TryGetDouble(material, "Proportion"))));
            }
        }

        session.Prospects.Add(new MiningProspectSnapshot(
            sequence,
            timestamp,
            GetLocalizedString(root, "Content"),
            Math.Max(0, TryGetDouble(root, "Remaining")),
            motherlode.Id,
            motherlode.DisplayName,
            materials
                .OrderByDescending(item => item.Proportion)
                .ThenBy(item => item.CommodityId, StringComparer.OrdinalIgnoreCase)
                .ToArray()));
        session.LastActivityUtc = timestamp;
        return Changed();
    }

    private MiningAccumulatorResult ApplyRefinement(JsonElement root, DateTimeOffset timestamp)
    {
        CommodityValue commodity = ReadCommodity(root, "Type");
        if (string.IsNullOrWhiteSpace(commodity.Id)
            && string.IsNullOrWhiteSpace(commodity.DisplayName))
        {
            return Unchanged();
        }

        ActiveSession session = EnsureActive(timestamp);
        session.Refinements.Add(new MiningRefinementSnapshot(
            session.Refinements.Count + 1,
            timestamp,
            commodity.Id,
            commodity.DisplayName));
        session.LastActivityUtc = timestamp;
        return Changed();
    }

    private MiningAccumulatorResult ApplyAsteroidCracked(DateTimeOffset timestamp)
    {
        ActiveSession session = EnsureActive(timestamp);
        session.CrackedAsteroids++;
        session.LastActivityUtc = timestamp;
        return Changed();
    }

    private ActiveSession EnsureActive(DateTimeOffset timestamp)
    {
        if (active is not null)
        {
            return active;
        }

        active = new ActiveSession
        {
            SessionId = CreateSessionId(commander, timestamp, systemAddress, systemName),
            StartedUtc = timestamp,
            LastActivityUtc = timestamp,
            Commander = commander,
            SystemAddress = systemAddress,
            SystemName = systemName,
            BodyId = bodyId,
            BodyName = bodyName,
            RingName = ringName,
            CargoUsed = cargoUsed,
            CargoCapacity = cargoCapacity,
            LimpetsRemaining = limpetsRemaining
        };
        return active;
    }

    private MiningAccumulatorResult Finish(DateTimeOffset timestamp, MiningSessionEndReason reason)
    {
        if (active is null)
        {
            return Unchanged();
        }

        MiningSessionSnapshot completed = active.ToSnapshot(
            MiningSessionState.Finished,
            timestamp,
            reason);
        active = null;

        return new MiningAccumulatorResult(
            Changed: true,
            Current: MiningSessionSnapshot.Empty,
            CompletedSession: completed.HasMiningEvidence ? completed : null);
    }

    private void ClearBodyContext()
    {
        bodyId = -1;
        bodyName = string.Empty;
        ringName = string.Empty;
    }

    private MiningAccumulatorResult ActiveContextChanged() =>
        active is null ? Unchanged() : Changed();

    private MiningAccumulatorResult Changed() =>
        new(true, Current);

    private MiningAccumulatorResult Unchanged() =>
        new(false, Current);

    private static bool SameSystem(
        long leftAddress,
        string leftName,
        long rightAddress,
        string rightName) =>
        leftAddress != 0 && rightAddress != 0
            ? leftAddress == rightAddress
            : string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase);

    private static bool IsRing(string bodyType, string body) =>
        bodyType.Contains("Ring", StringComparison.OrdinalIgnoreCase)
        || body.EndsWith(" Ring", StringComparison.OrdinalIgnoreCase);

    private static Guid CreateSessionId(
        string commander,
        DateTimeOffset startedUtc,
        long address,
        string system)
    {
        string identity = string.Join(
            "\n",
            commander.Trim().ToUpperInvariant(),
            startedUtc.ToUniversalTime().ToString("O"),
            address.ToString(System.Globalization.CultureInfo.InvariantCulture),
            system.Trim().ToUpperInvariant());
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static CommodityValue ReadCommodity(JsonElement root, string propertyName)
    {
        string raw = GetString(root, propertyName);
        string id = CommodityIdentity.Normalize(raw);
        string display = GetString(root, propertyName + "_Localised");
        if (string.IsNullOrWhiteSpace(display))
        {
            display = raw.StartsWith('$') ? id : raw;
        }
        return new CommodityValue(id, display.Trim());
    }

    private static string GetLocalizedString(JsonElement root, string propertyName)
    {
        string localized = GetString(root, propertyName + "_Localised");
        return string.IsNullOrWhiteSpace(localized)
            ? GetString(root, propertyName)
            : localized;
    }

    private static string GetString(JsonElement root, string propertyName, string fallback = "")
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return fallback;
        }
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static int TryGetInt32(JsonElement root, string propertyName, int fallback = 0)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return fallback;
        }
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)
            ? result
            : fallback;
    }

    private static long TryGetInt64(JsonElement root, string propertyName, long fallback = 0)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return fallback;
        }
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long result)
            ? result
            : fallback;
    }

    private static double TryGetDouble(JsonElement root, string propertyName, double fallback = 0)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return fallback;
        }
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result)
            ? result
            : fallback;
    }

    private readonly record struct CommodityValue(string Id, string DisplayName);

    private sealed class ActiveSession
    {
        public Guid SessionId { get; init; }
        public DateTimeOffset StartedUtc { get; init; }
        public DateTimeOffset LastActivityUtc { get; set; }
        public string Commander { get; init; } = string.Empty;
        public long SystemAddress { get; set; }
        public string SystemName { get; set; } = string.Empty;
        public int BodyId { get; set; } = -1;
        public string BodyName { get; set; } = string.Empty;
        public string RingName { get; set; } = string.Empty;
        public int ProspectorsLaunched { get; set; }
        public int CollectorsLaunched { get; set; }
        public int CrackedAsteroids { get; set; }
        public int CargoUsed { get; set; }
        public int CargoCapacity { get; set; }
        public int LimpetsRemaining { get; set; }
        public List<MiningProspectSnapshot> Prospects { get; } = new();
        public List<MiningRefinementSnapshot> Refinements { get; } = new();

        public MiningSessionSnapshot ToSnapshot(
            MiningSessionState state,
            DateTimeOffset? endedUtc,
            MiningSessionEndReason endReason) =>
            new(
                SessionId,
                state,
                StartedUtc,
                LastActivityUtc,
                endedUtc,
                endReason,
                Commander,
                SystemAddress,
                SystemName,
                BodyId,
                BodyName,
                RingName,
                ProspectorsLaunched,
                CollectorsLaunched,
                CrackedAsteroids,
                CargoUsed,
                CargoCapacity,
                LimpetsRemaining,
                Prospects.ToArray(),
                Refinements.ToArray());
    }
}
