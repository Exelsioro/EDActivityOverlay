using System.IO;
using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Exploration;

/// <summary>Reconstructs estimated unsold exploration data from Player Journal events.</summary>
public sealed class ExplorationEarningsService : IJournalDataConsumer, IDisposable
{
    private sealed record BodyValue(
        ExplorationValueEstimate Estimate,
        bool? WasDiscovered,
        bool? WasMapped,
        long Value);

    private readonly object sync = new();
    private readonly Dictionary<string, BodyValue> bodies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Minimum, long Maximum)> organics = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> queuedEvents = new();
    private CancellationTokenSource? cancellation;
    private bool started;
    private bool rebuilding;
    private int rebuildGeneration;
    private bool journalEnabled;
    private string configuredJournalDirectory = string.Empty;
    private string currentSystem = string.Empty;
    private long currentAddress;
    private DateTimeOffset? lastUcSale;
    private DateTimeOffset? lastBioSale;
    private string commander = string.Empty;

    public static ExplorationEarningsService Instance { get; } = new();
    public ExplorationEarningsState Current { get; private set; } = ExplorationEarningsState.Empty;
    public event EventHandler<ExplorationEarningsChangedEventArgs>? Changed;

    private ExplorationEarningsService() { }

    public void Start(string journalDirectory)
    {
        if (started) return;
        started = true;
        JournalMonitorService.Instance.Events.Register(this);
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        journalEnabled = SettingsService.Instance.Settings.EnableJournalIntegration;
        configuredJournalDirectory = journalDirectory?.Trim() ?? string.Empty;
        if (journalEnabled) BeginRebuild(journalDirectory ?? string.Empty);
    }

    private void BeginRebuild(string journalDirectory)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        var rebuildCancellation = new CancellationTokenSource();
        cancellation = rebuildCancellation;
        int generation;
        lock (sync)
        {
            generation = ++rebuildGeneration;
            queuedEvents.Clear();
            rebuilding = true;
        }
        Publish();
        string directory = string.IsNullOrWhiteSpace(journalDirectory)
            ? JournalPathResolver.GetDefaultJournalDirectory()
            : journalDirectory;
        _ = RebuildAsync(directory, generation, rebuildCancellation.Token);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        string directory = e.Settings.JournalDirectory?.Trim() ?? string.Empty;
        if (journalEnabled == e.Settings.EnableJournalIntegration
            && string.Equals(configuredJournalDirectory, directory, StringComparison.OrdinalIgnoreCase)) return;
        journalEnabled = e.Settings.EnableJournalIntegration;
        configuredJournalDirectory = directory;
        if (journalEnabled)
        {
            BeginRebuild(directory);
            return;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        lock (sync)
        {
            rebuildGeneration++;
            queuedEvents.Clear();
            rebuilding = false;
        }
        Publish();
    }

    private async Task RebuildAsync(string directory, int generation, CancellationToken token)
    {
        var rebuilt = new Accumulator();
        try
        {
            if (Directory.Exists(directory))
            {
                foreach (string path in Directory.EnumerateFiles(directory, "Journal.*.log")
                             .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    using var reader = new StreamReader(stream);
                    while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                    {
                        if (!string.IsNullOrWhiteSpace(line)) rebuilt.Apply(line);
                    }
                }
            }

            lock (sync)
            {
                if (generation != rebuildGeneration) return;
                bodies.Clear();
                foreach (var item in rebuilt.Bodies) bodies[item.Key] = item.Value;
                organics.Clear();
                foreach (var item in rebuilt.Organics) organics[item.Key] = item.Value;
                currentSystem = rebuilt.CurrentSystem;
                currentAddress = rebuilt.CurrentAddress;
                commander = rebuilt.Commander;
                lastUcSale = rebuilt.LastUcSale;
                lastBioSale = rebuilt.LastBioSale;
                foreach (string line in queuedEvents) ApplyLine(line);
                queuedEvents.Clear();
                rebuilding = false;
            }
            Publish();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Exploration earnings rebuild failed: {ex.Message}");
            lock (sync)
            {
                if (generation != rebuildGeneration) return;
                rebuilding = false;
            }
            Publish();
        }
    }

    public void OnJournalEvent(JournalEventReceivedEventArgs journalEvent)
    {
        // This service performs its own full journal rebuild. Bootstrap events
        // from JournalMonitorService would otherwise be applied twice.
        if (journalEvent.Origin == JournalEventOrigin.Bootstrap)
        {
            return;
        }

        if (!journalEnabled) return;
        string line = journalEvent.Data.GetRawText();
        bool publish;
        lock (sync)
        {
            if (rebuilding) queuedEvents.Add(line);
            else ApplyLine(line);
            publish = !rebuilding;
        }
        if (publish) Publish();
    }

    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile) { }

    private void ApplyLine(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        Apply(document.RootElement, bodies, organics, ref currentSystem, ref currentAddress,
            ref lastUcSale, ref lastBioSale, ref commander);
    }

    private void Publish()
    {
        ExplorationEarningsState state;
        lock (sync)
        {
            state = new ExplorationEarningsState(
                bodies.Values.Sum(item => item.Value),
                organics.Values.Sum(item => item.Minimum),
                organics.Values.Sum(item => item.Maximum),
                lastUcSale, lastBioSale, rebuilding);
            Current = state;
        }
        Changed?.Invoke(this, new ExplorationEarningsChangedEventArgs(state));
    }

    private static void Apply(
        JsonElement root,
        IDictionary<string, BodyValue> bodyValues,
        IDictionary<string, (long Minimum, long Maximum)> organicValues,
        ref string system,
        ref long address,
        ref DateTimeOffset? ucSale,
        ref DateTimeOffset? bioSale,
        ref string commander)
    {
        string eventName = String(root, "event").ToLowerInvariant();
        DateTimeOffset timestamp = DateTimeOffset.TryParse(String(root, "timestamp"), out var parsed)
            ? parsed : DateTimeOffset.UtcNow;
        if (eventName is "loadgame" or "commander")
        {
            string nextCommander = String(root, "Commander", String(root, "Name", commander)).Trim();
            if (!string.IsNullOrWhiteSpace(nextCommander)
                && !string.Equals(nextCommander, commander, StringComparison.OrdinalIgnoreCase))
            {
                bodyValues.Clear();
                organicValues.Clear();
                ucSale = null;
                bioSale = null;
                system = string.Empty;
                address = 0;
                commander = nextCommander;
            }
            return;
        }
        if (eventName is "location" or "fsdjump" or "carrierjump")
        {
            system = String(root, "StarSystem", system);
            address = Long(root, "SystemAddress", address);
            return;
        }
        if (eventName is "sellexplorationdata" or "multisellexplorationdata")
        {
            if (!ApplyExplorationSale(root, bodyValues)) bodyValues.Clear();
            ucSale = timestamp;
            return;
        }
        if (eventName == "sellorganicdata")
        {
            if (!ApplyOrganicSale(root, organicValues)) organicValues.Clear();
            bioSale = timestamp;
            return;
        }
        if (eventName == "scan")
        {
            string starType = String(root, "StarType");
            string bodyClass = String(root, "PlanetClass", starType);
            string bodyType = string.IsNullOrWhiteSpace(starType) ? "Planet" : "Star";
            bool terraformable = String(root, "TerraformState").Contains("Terraform", StringComparison.OrdinalIgnoreCase);
            var estimate = ExplorationValueCalculator.Estimate(
                bodyType, bodyClass, terraformable,
                Double(root, "MassEM"), Double(root, "StellarMass"));
            bool? wasDiscovered = NullableBool(root, "WasDiscovered");
            bool? wasMapped = NullableBool(root, "WasMapped");
            string key = BodyKey(root, address, system);
            long scanValue = ExplorationValueCalculator.SelectScanValue(estimate, wasDiscovered);
            if (bodyValues.TryGetValue(key, out BodyValue? previous))
            {
                bodyValues[key] = previous with
                {
                    Estimate = estimate,
                    WasDiscovered = wasDiscovered ?? previous.WasDiscovered,
                    WasMapped = wasMapped ?? previous.WasMapped,
                    // A later basic scan must not erase a higher DSS value.
                    Value = Math.Max(previous.Value, scanValue)
                };
            }
            else
            {
                bodyValues[key] = new BodyValue(estimate, wasDiscovered, wasMapped, scanValue);
            }
            return;
        }
        if (eventName == "saascancomplete")
        {
            string key = BodyKey(root, address, system);
            if (bodyValues.TryGetValue(key, out BodyValue? body))
            {
                int probes = Int(root, "ProbesUsed");
                int target = Int(root, "EfficiencyTarget");
                long mapped = ExplorationValueCalculator.SelectMappingValue(
                    body.Estimate, body.WasDiscovered, body.WasMapped,
                    target > 0 && probes > 0 && probes <= target);
                bodyValues[key] = body with { Value = Math.Max(body.Value, mapped) };
            }
            return;
        }
        if (eventName == "scanorganic" && String(root, "ScanType").Equals("Analyse", StringComparison.OrdinalIgnoreCase))
        {
            string genus = String(root, "Genus_Localised", String(root, "Genus"));
            BiologyEstimateSnapshot estimate = ExobiologyCatalog.Estimate(String(root, "Genus"), genus);
            string species = String(root, "Variant", String(root, "Species", genus));
            organicValues[$"{address}|{system}|{Int(root, "Body", -1)}|{species}"] =
                (estimate.MinimumValue, estimate.MaximumValue);
        }
    }

    private sealed class Accumulator
    {
        public Dictionary<string, BodyValue> Bodies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, (long Minimum, long Maximum)> Organics { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string CurrentSystem = string.Empty;
        public long CurrentAddress;
        public string Commander = string.Empty;
        public DateTimeOffset? LastUcSale;
        public DateTimeOffset? LastBioSale;
        public void Apply(string line)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                ExplorationEarningsService.Apply(document.RootElement, Bodies, Organics,
                    ref CurrentSystem, ref CurrentAddress, ref LastUcSale, ref LastBioSale, ref Commander);
            }
            catch (JsonException) { }
        }
    }

    internal static ExplorationEarningsState CalculateForJournalLines(IEnumerable<string> lines)
    {
        var accumulator = new Accumulator();
        foreach (string line in lines) accumulator.Apply(line);
        return new ExplorationEarningsState(
            accumulator.Bodies.Values.Sum(item => item.Value),
            accumulator.Organics.Values.Sum(item => item.Minimum),
            accumulator.Organics.Values.Sum(item => item.Maximum),
            accumulator.LastUcSale, accumulator.LastBioSale, false);
    }

    private static string BodyKey(JsonElement root, long address, string system) =>
        $"{address}|{system}|{Int(root, "BodyID", -1)}|{String(root, "BodyName")}";
    private static string String(JsonElement root, string name, string fallback = "") =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback : fallback;
    private static int Int(JsonElement root, string name, int fallback = 0) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : fallback;
    private static long Long(JsonElement root, string name, long fallback = 0) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long result) ? result : fallback;
    private static double? Double(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double result) ? result : null;
    private static bool? NullableBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static bool ApplyExplorationSale(
        JsonElement root,
        IDictionary<string, BodyValue> bodyValues)
    {
        var systems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool scoped = false;

        if (root.TryGetProperty("Systems", out JsonElement systemList)
            && systemList.ValueKind == JsonValueKind.Array)
        {
            scoped = true;
            foreach (JsonElement item in systemList.EnumerateArray())
                AddSystemIdentity(item, systems);
        }
        if (root.TryGetProperty("Discovered", out JsonElement discovered)
            && discovered.ValueKind == JsonValueKind.Array)
        {
            scoped = true;
            foreach (JsonElement item in discovered.EnumerateArray())
                AddDiscoveredIdentity(item, systems, bodies);
        }
        string system = String(root, "System", String(root, "StarSystem"));
        if (!string.IsNullOrWhiteSpace(system))
        {
            scoped = true;
            systems.Add(system);
        }
        if (!scoped) return false;

        foreach (string key in bodyValues.Keys.ToArray())
        {
            string[] parts = key.Split('|', 4);
            if (parts.Length < 4) continue;
            if (systems.Contains(parts[1]) || bodies.Contains(parts[3])) bodyValues.Remove(key);
        }
        return true;
    }

    private static void AddSystemIdentity(JsonElement item, ISet<string> systems)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            string value = item.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value)) systems.Add(value);
            return;
        }
        if (item.ValueKind != JsonValueKind.Object) return;
        string system = String(item, "SystemName", String(item, "StarSystem", String(item, "System")));
        if (!string.IsNullOrWhiteSpace(system)) systems.Add(system);
    }

    private static void AddDiscoveredIdentity(
        JsonElement item,
        ISet<string> systems,
        ISet<string> bodies)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            string value = item.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value)) bodies.Add(value);
            return;
        }
        if (item.ValueKind != JsonValueKind.Object) return;
        string system = String(item, "SystemName", String(item, "StarSystem", String(item, "System")));
        string body = String(item, "BodyName", String(item, "Body"));
        if (!string.IsNullOrWhiteSpace(system)) systems.Add(system);
        if (!string.IsNullOrWhiteSpace(body)) bodies.Add(body);
    }

    private static bool ApplyOrganicSale(
        JsonElement root,
        IDictionary<string, (long Minimum, long Maximum)> organicValues)
    {
        if (!root.TryGetProperty("BioData", out JsonElement bioData)
            || bioData.ValueKind != JsonValueKind.Array)
            return false;
        if (bioData.GetArrayLength() == 0) return false;

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in bioData.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            foreach (string property in new[] { "Variant", "Species", "Genus" })
            {
                string value = String(item, property);
                if (!string.IsNullOrWhiteSpace(value)) identities.Add(value);
            }
        }
        if (identities.Count == 0) return true;

        foreach (string key in organicValues.Keys.ToArray())
        {
            string species = key.Split('|').LastOrDefault() ?? string.Empty;
            if (identities.Contains(species)) organicValues.Remove(key);
        }
        // Legacy numeric species identifiers cannot always be joined to our
        // codex keys; unmatched entries deliberately remain an estimate.
        return true;
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (started) JournalMonitorService.Instance.Events.Unregister(this);
        SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
        started = false;
    }
}
