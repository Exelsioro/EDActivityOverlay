using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Engineering;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Mining;

/// <summary>
/// Tracks only the raw-material delta acquired during the current mining session.
/// EngineeringService remains the sole owner of the actual material inventory,
/// wishlist and aggregated requirements.
/// </summary>
public sealed class MiningEngineeringMaterialTrackerService :
    IJournalDataConsumer,
    IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<string, MiningMaterialSessionGain> gains =
        new(StringComparer.OrdinalIgnoreCase);

    private Guid sessionId;
    private bool started;
    private bool disposed;

    public static MiningEngineeringMaterialTrackerService Instance { get; } =
        new();

    public event EventHandler<MiningEngineeringMaterialsChangedEventArgs>? Changed;

    private MiningEngineeringMaterialTrackerService()
    {
    }

    public MiningEngineeringMaterialsSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return MiningEngineeringMaterialProjector.Build(
                    sessionId,
                    gains.Values,
                    EngineeringService.Instance.Current);
            }
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            return;
        }

        MiningSessionService.Instance.Changed += OnMiningSessionChanged;
        EngineeringService.Instance.StateChanged += OnEngineeringStateChanged;
        JournalMonitorService.Instance.Events.Register(this);

        MiningSessionSnapshot current = MiningSessionService.Instance.Current;
        sessionId = current.IsActive
            ? current.SessionId
            : Guid.Empty;

        started = true;
    }

    public void OnJournalEvent(JournalEventReceivedEventArgs journalEvent)
    {
        if (journalEvent.Origin != JournalEventOrigin.Live
            || !journalEvent.EventName.Equals(
                "MaterialCollected",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        MiningSessionSnapshot mining = MiningSessionService.Instance.Current;
        if (!mining.IsActive)
        {
            return;
        }

        string rawName = GetString(journalEvent.Data, "Name");
        string materialId = MaterialName.Normalize(rawName);
        if (string.IsNullOrWhiteSpace(materialId))
        {
            return;
        }

        EngineeringSnapshot engineering = EngineeringService.Instance.Current;
        EngineeringMaterialCategory category =
            ResolveCategory(journalEvent.Data, materialId, engineering);

        // Asteroid mining contributes Horizons raw materials. If a future journal
        // event reuses MaterialCollected for another category, do not misattribute it.
        if (category != EngineeringMaterialCategory.Raw)
        {
            return;
        }

        int count = Math.Max(1, GetInt(journalEvent.Data, "Count", 1));
        string displayName = GetString(
            journalEvent.Data,
            "Name_Localised");

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = engineering.Inventory.TryGetValue(
                materialId,
                out MaterialInventoryEntry? inventory)
                ? inventory.Name
                : MaterialName.Friendly(rawName);
        }

        lock (sync)
        {
            if (sessionId != mining.SessionId)
            {
                gains.Clear();
                sessionId = mining.SessionId;
            }

            if (gains.TryGetValue(
                    materialId,
                    out MiningMaterialSessionGain? existing))
            {
                gains[materialId] = existing with
                {
                    DisplayName = displayName,
                    Count = existing.Count + count
                };
            }
            else
            {
                gains[materialId] =
                    new MiningMaterialSessionGain(
                        materialId,
                        displayName,
                        count);
            }
        }

        RaiseChanged();
    }

    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile)
    {
    }

    private void OnMiningSessionChanged(
        object? sender,
        MiningSessionChangedEventArgs e)
    {
        bool changed = false;

        lock (sync)
        {
            if (!e.Current.IsActive)
            {
                if (sessionId != Guid.Empty || gains.Count > 0)
                {
                    sessionId = Guid.Empty;
                    gains.Clear();
                    changed = true;
                }
            }
            else if (sessionId != e.Current.SessionId)
            {
                sessionId = e.Current.SessionId;
                gains.Clear();
                changed = true;
            }
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    private void OnEngineeringStateChanged(
        object? sender,
        EngineeringStateChangedEventArgs e)
    {
        lock (sync)
        {
            if (gains.Count == 0)
            {
                return;
            }
        }

        // A wishlist/requirement change can alter the progress projection without
        // changing the mining-session delta.
        RaiseChanged();
    }

    private void RaiseChanged() =>
        Changed?.Invoke(
            this,
            new MiningEngineeringMaterialsChangedEventArgs(Current));

    private static EngineeringMaterialCategory ResolveCategory(
        JsonElement data,
        string materialId,
        EngineeringSnapshot engineering)
    {
        string category = GetString(data, "Category");
        if (category.Equals("Raw", StringComparison.OrdinalIgnoreCase))
        {
            return EngineeringMaterialCategory.Raw;
        }

        return engineering.Inventory.TryGetValue(
            materialId,
            out MaterialInventoryEntry? inventory)
            ? inventory.Category
            : EngineeringMaterialCategory.Unknown;
    }

    private static string GetString(
        JsonElement element,
        string property) =>
        element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(
        JsonElement element,
        string property,
        int fallback) =>
        element.TryGetProperty(property, out JsonElement value)
        && value.TryGetInt32(out int result)
            ? result
            : fallback;

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
            MiningSessionService.Instance.Changed -= OnMiningSessionChanged;
            EngineeringService.Instance.StateChanged -= OnEngineeringStateChanged;
            started = false;
        }

        lock (sync)
        {
            sessionId = Guid.Empty;
            gains.Clear();
        }
    }
}

internal static class MiningEngineeringMaterialProjector
{
    public static MiningEngineeringMaterialsSnapshot Build(
        Guid sessionId,
        IEnumerable<MiningMaterialSessionGain> gains,
        EngineeringSnapshot engineering)
    {
        ArgumentNullException.ThrowIfNull(gains);
        ArgumentNullException.ThrowIfNull(engineering);

        Dictionary<string, MaterialRequirement> requirements =
            engineering.Requirements
                .GroupBy(
                    item => MaterialName.Normalize(item.MaterialId),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        Dictionary<string, MaterialInventoryEntry> inventory =
            engineering.Inventory.Values
                .GroupBy(
                    item => MaterialName.Normalize(item.Id),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        MiningEngineeringMaterialProgress[] items = gains
            .Where(item => item.Count > 0)
            .Select(item =>
            {
                string id = MaterialName.Normalize(item.MaterialId);

                requirements.TryGetValue(
                    id,
                    out MaterialRequirement? requirement);

                inventory.TryGetValue(
                    id,
                    out MaterialInventoryEntry? inventoryEntry);

                int available =
                    requirement?.Available
                    ?? inventoryEntry?.Count
                    ?? 0;

                int required =
                    requirement?.Required
                    ?? 0;

                int missing =
                    requirement?.Missing
                    ?? 0;

                string displayName =
                    !string.IsNullOrWhiteSpace(requirement?.Name)
                        ? requirement.Name
                        : !string.IsNullOrWhiteSpace(inventoryEntry?.Name)
                            ? inventoryEntry.Name
                            : item.DisplayName;

                return new MiningEngineeringMaterialProgress(
                    id,
                    displayName,
                    item.Count,
                    available,
                    required,
                    missing);
            })
            .OrderByDescending(item => item.IsEngineeringTarget)
            .ThenByDescending(item => item.Missing > 0)
            .ThenByDescending(item => item.GainedThisSession)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new MiningEngineeringMaterialsSnapshot(
            sessionId,
            items,
            items.Sum(item => item.GainedThisSession),
            items
                .Where(item => item.IsEngineeringTarget)
                .Sum(item => item.GainedThisSession));
    }
}
