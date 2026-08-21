using System.Text.Json;

namespace ED_Inara_Overlay.Services.Exploration;

internal sealed class ExplorationHistoryAccumulator(ExplorationHistoryRepository repository)
{
    private readonly Dictionary<int, (bool WasDiscovered, bool WasMapped, string Name)> scanFlags = new();
    private string commander = string.Empty;
    private long systemAddress;
    private string systemName = string.Empty;

    public bool Apply(JsonElement root)
    {
        string eventName = GetString(root, "event").ToLowerInvariant();
        DateTimeOffset timestamp = GetTimestamp(root);
        switch (eventName)
        {
            case "commander":
            case "loadgame":
                commander = GetString(root, "Name", GetString(root, "Commander", commander));
                return false;
            case "location":
            case "fsdjump":
            case "carrierjump":
                SetSystem(root);
                repository.RecordVisit(commander, systemAddress, systemName, timestamp);
                return true;
            case "scan":
            {
                SetSystemIfPresent(root);
                int bodyId = GetInt(root, "BodyID", -1);
                string bodyName = GetString(root, "BodyName");
                bool wasDiscovered = GetBool(root, "WasDiscovered");
                bool wasMapped = GetBool(root, "WasMapped");
                if (bodyId >= 0) scanFlags[bodyId] = (wasDiscovered, wasMapped, bodyName);
                repository.RecordBody(
                    commander, systemAddress, systemName, bodyId, bodyName,
                    GetString(root, "PlanetClass", GetString(root, "StarType")), timestamp,
                    scanned: true,
                    firstDiscovered: !wasDiscovered);
                return true;
            }
            case "saascancomplete":
            {
                SetSystemIfPresent(root);
                int bodyId = GetInt(root, "BodyID", -1);
                scanFlags.TryGetValue(bodyId, out var previous);
                int used = GetInt(root, "ProbesUsed");
                int target = GetInt(root, "EfficiencyTarget");
                repository.RecordBody(
                    commander, systemAddress, systemName, bodyId,
                    GetString(root, "BodyName", previous.Name), string.Empty, timestamp,
                    mapped: true,
                    efficient: target > 0 && used > 0 && used <= target,
                    firstMapped: !string.IsNullOrWhiteSpace(previous.Name) && !previous.WasMapped);
                return true;
            }
            case "fssbodysignals":
            case "saasignalsfound":
            {
                SetSystemIfPresent(root);
                repository.RecordBody(
                    commander, systemAddress, systemName,
                    GetInt(root, "BodyID", -1), GetString(root, "BodyName"), string.Empty, timestamp,
                    biologicalSignals: ReadBiologicalSignals(root));
                return true;
            }
            case "scanorganic":
            {
                SetSystemIfPresent(root);
                int bodyId = GetInt(root, "Body", GetInt(root, "BodyID", -1));
                scanFlags.TryGetValue(bodyId, out var previous);
                string variant = GetString(root, "Variant");
                string species = GetString(root, "Species");
                repository.RecordOrganic(
                    commander, systemAddress, systemName, bodyId, previous.Name,
                    string.IsNullOrWhiteSpace(variant) ? species : variant,
                    GetLocalized(root, string.IsNullOrWhiteSpace(variant) ? "Species" : "Variant"),
                    GetString(root, "ScanType").Equals("Analyse", StringComparison.OrdinalIgnoreCase),
                    timestamp);
                return true;
            }
            default:
                return false;
        }
    }

    private void SetSystem(JsonElement root)
    {
        systemName = GetString(root, "StarSystem", systemName);
        systemAddress = GetLong(root, "SystemAddress", systemAddress);
        scanFlags.Clear();
    }

    private void SetSystemIfPresent(JsonElement root)
    {
        string candidate = GetString(root, "StarSystem");
        if (!string.IsNullOrWhiteSpace(candidate)) systemName = candidate;
        long address = GetLong(root, "SystemAddress");
        if (address > 0) systemAddress = address;
    }

    private static int ReadBiologicalSignals(JsonElement root)
    {
        if (!root.TryGetProperty("Signals", out JsonElement signals) || signals.ValueKind != JsonValueKind.Array) return 0;
        foreach (JsonElement signal in signals.EnumerateArray())
        {
            string type = GetString(signal, "Type") + " " + GetString(signal, "Type_Localised");
            if (type.Contains("Biological", StringComparison.OrdinalIgnoreCase)
                || type.Contains("Биолог", StringComparison.OrdinalIgnoreCase))
            {
                return GetInt(signal, "Count");
            }
        }
        return 0;
    }

    private static string GetLocalized(JsonElement root, string name)
    {
        string localized = GetString(root, name + "_Localised");
        return string.IsNullOrWhiteSpace(localized) ? GetString(root, name) : localized;
    }

    private static string GetString(JsonElement root, string name, string fallback = "") =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int GetInt(JsonElement root, string name, int fallback = 0) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : fallback;

    private static long GetLong(JsonElement root, string name, long fallback = 0) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long result) ? result : fallback;

    private static bool GetBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static DateTimeOffset GetTimestamp(JsonElement root) =>
        DateTimeOffset.TryParse(GetString(root, "timestamp"), out DateTimeOffset value)
            ? value
            : DateTimeOffset.UtcNow;
}
