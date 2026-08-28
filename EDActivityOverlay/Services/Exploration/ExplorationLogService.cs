using System.IO;
using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Exploration;

public sealed class ExplorationLogService : IJournalDataConsumer, IDisposable
{
    private const int MaximumEntries = 500;
    private readonly object sync = new();
    private readonly string statePath;
    private readonly List<ExplorationLogEntry> entries = new();
    private readonly HashSet<Guid> sessionEntryIds = new();
    private string system = string.Empty;
    private bool started;

    public static ExplorationLogService Instance { get; } = new();
    public event EventHandler<ExplorationLogChangedEventArgs>? Changed;
    public IReadOnlyList<ExplorationLogEntry> Entries
    {
        get
        {
            lock (sync)
            {
                return entries
                    .Where(item => sessionEntryIds.Contains(item.Id))
                    .OrderByDescending(item => item.TimestampUtc)
                    .ToArray();
            }
        }
    }

    private ExplorationLogService()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EDActivityOverlay");
        Directory.CreateDirectory(directory);
        statePath = Path.Combine(directory, "exploration-log.json");
        Load();
    }

    public void Start()
    {
        if (started) return;
        started = true;
        JournalMonitorService.Instance.Events.Register(this);
    }

    public void OnJournalEvent(JournalEventReceivedEventArgs journalEvent)
    {
        if (journalEvent.Origin == JournalEventOrigin.Bootstrap)
        {
            return;
        }

        JsonElement root = journalEvent.Data;
        string eventName = journalEvent.EventName.ToLowerInvariant();
        if (eventName is "location" or "fsdjump" or "carrierjump")
        {
            system = Text(root, "StarSystem", system);
            Add(journalEvent.Timestamp, ExplorationLogKind.Visit, system, string.Empty, system, string.Empty, false);
            return;
        }
        string eventSystem = Text(root, "StarSystem", system);
        string body = Text(root, "BodyName");
        if (eventName == "scan")
        {
            string rawBodyClass = Text(root, "PlanetClass", Text(root, "StarType"));
            string bodyClass = Text(root, "PlanetClass_Localised", rawBodyClass);
            bool terraformable = Text(root, "TerraformState").Contains("Terraform", StringComparison.OrdinalIgnoreCase);
            if (terraformable || IsNotable(rawBodyClass))
            {
                Add(journalEvent.Timestamp, ExplorationLogKind.NotableBody, eventSystem, body,
                    bodyClass, terraformable ? "terraformable" : string.Empty, false);
            }
        }
        else if (eventName == "saascancomplete")
        {
            int used = Integer(root, "ProbesUsed");
            int target = Integer(root, "EfficiencyTarget");
            Add(journalEvent.Timestamp, ExplorationLogKind.Mapping, eventSystem, body,
                used > 0 && target > 0 && used <= target ? "efficient" : "mapped", $"{used}/{target}", false);
        }
        else if (eventName is "fssbodysignals" or "saasignalsfound")
        {
            int biological = BiologicalSignals(root);
            if (biological > 0)
                Add(journalEvent.Timestamp, ExplorationLogKind.Biology, eventSystem, body,
                    "signals", biological.ToString(), false);
        }
        else if (eventName == "scanorganic" && Text(root, "ScanType").Equals("Analyse", StringComparison.OrdinalIgnoreCase))
        {
            string subject = Text(root, "Variant_Localised",
                Text(root, "Species_Localised", Text(root, "Variant", Text(root, "Species"))));
            Add(journalEvent.Timestamp, ExplorationLogKind.Biology, eventSystem, body, subject, "completed", false);
        }
        else if (eventName == "codexentry" && Boolean(root, "IsNewEntry"))
        {
            Add(journalEvent.Timestamp, ExplorationLogKind.Codex, eventSystem, body,
                Text(root, "Name_Localised", Text(root, "Name")), Text(root, "Region_Localised", Text(root, "Region")), false);
        }
    }

    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile) { }

    public ExplorationLogEntry AddManualFinding(string commanderSystem, string body, string detail)
    {
        var entry = new ExplorationLogEntry(Guid.NewGuid(), DateTimeOffset.UtcNow,
            ExplorationLogKind.Manual, commanderSystem, body, body, detail, true);
        lock (sync)
        {
            entries.Add(entry);
            sessionEntryIds.Add(entry.Id);
            TrimAndSave();
        }
        RaiseChanged();
        return entry;
    }

    public void ToggleBookmark(Guid id)
    {
        lock (sync)
        {
            int index = entries.FindIndex(item => item.Id == id);
            if (index < 0) return;
            entries[index] = entries[index] with { Bookmarked = !entries[index].Bookmarked };
            Save();
        }
        RaiseChanged();
    }

    private void Add(DateTimeOffset time, ExplorationLogKind kind, string entrySystem, string body,
        string subject, string detail, bool bookmarked)
    {
        if (string.IsNullOrWhiteSpace(entrySystem) && string.IsNullOrWhiteSpace(body)) return;
        lock (sync)
        {
            if (entries.Any(item => item.TimestampUtc == time && item.Kind == kind
                                    && item.System.Equals(entrySystem, StringComparison.OrdinalIgnoreCase)
                                    && item.Body.Equals(body, StringComparison.OrdinalIgnoreCase))) return;
            var entry = new ExplorationLogEntry(
                Guid.NewGuid(),
                time,
                kind,
                entrySystem,
                body,
                subject,
                detail,
                bookmarked);

            entries.Add(entry);
            sessionEntryIds.Add(entry.Id);
            TrimAndSave();
        }
        RaiseChanged();
    }

    private void TrimAndSave()
    {
        if (entries.Count > MaximumEntries)
        {
            ExplorationLogEntry[] keep = entries
                .OrderByDescending(item => item.Bookmarked)
                .ThenByDescending(item => item.TimestampUtc)
                .Take(MaximumEntries)
                .ToArray();
            entries.Clear();
            entries.AddRange(keep);
        }
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(statePath)) return;
            entries.AddRange(JsonSerializer.Deserialize<List<ExplorationLogEntry>>(File.ReadAllText(statePath)) ?? []);
        }
        catch (Exception ex) { Logger.Logger.Warning($"Exploration log could not be loaded: {ex.Message}"); }
    }

    private void Save()
    {
        try { File.WriteAllText(statePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Logger.Logger.Warning($"Exploration log could not be saved: {ex.Message}"); }
    }

    private void RaiseChanged() => Changed?.Invoke(this, new ExplorationLogChangedEventArgs(Entries));
    private static bool IsNotable(string value) => new[]
    {
        "Earthlike", "Earth-like", "Water world", "Ammonia world", "Neutron", "Black hole", "BlackHole"
    }.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    private static int BiologicalSignals(JsonElement root)
    {
        if (!root.TryGetProperty("Signals", out JsonElement signals) || signals.ValueKind != JsonValueKind.Array) return 0;
        foreach (JsonElement signal in signals.EnumerateArray())
        {
            if ((Text(signal, "Type") + Text(signal, "Type_Localised")).Contains("Biolog", StringComparison.OrdinalIgnoreCase)
                || Text(signal, "Type_Localised").Contains("Биолог", StringComparison.OrdinalIgnoreCase))
                return Integer(signal, "Count");
        }
        return 0;
    }
    private static string Text(JsonElement root, string name, string fallback = "") =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback : fallback;
    private static int Integer(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : 0;
    private static bool Boolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    public void Dispose()
    {
        if (started) JournalMonitorService.Instance.Events.Unregister(this);
        started = false;
    }
}
