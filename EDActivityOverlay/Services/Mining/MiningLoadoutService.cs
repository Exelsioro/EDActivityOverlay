using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Mining;

public sealed class MiningLoadoutService :
    IJournalDataConsumer,
    IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<
        string,
        MiningLoadoutModuleInput> slots =
        new(StringComparer.OrdinalIgnoreCase);

    private MiningLoadoutSnapshot current =
        MiningLoadoutSnapshot.Empty;
    private string ship = string.Empty;
    private bool available;
    private bool started;
    private bool disposed;

    public static MiningLoadoutService Instance { get; } =
        new();

    public event EventHandler<MiningLoadoutChangedEventArgs>?
        Changed;

    public MiningLoadoutSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    internal MiningLoadoutService()
    {
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(
            disposed,
            this);

        if (started)
        {
            return;
        }

        JournalMonitorService.Instance.Events.Register(this);
        started = true;
    }

    public void OnJournalEvent(
        JournalEventReceivedEventArgs journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);

        MiningLoadoutSnapshot? published = null;
        lock (sync)
        {
            bool touched =
                ApplyJournalEvent(
                    journalEvent.EventName,
                    journalEvent.Data);
            if (!touched)
            {
                return;
            }

            MiningLoadoutSnapshot next =
                MiningLoadoutAnalyzer.Analyze(
                    ship,
                    available,
                    slots.Values);

            if (Equivalent(
                    current,
                    next))
            {
                return;
            }

            current = next;
            published = next;
        }

        Changed?.Invoke(
            this,
            new MiningLoadoutChangedEventArgs(
                published));
    }

    public void OnCompanionFile(
        CompanionFileReceivedEventArgs companionFile)
    {
        // Loadout is a Journal event. ModulesInfo.json contains
        // engineering/power state, not a complete ship loadout.
    }

    private bool ApplyJournalEvent(
        string? eventName,
        JsonElement root)
    {
        switch (
            eventName?.Trim().ToLowerInvariant())
        {
            case "loadout":
                return ApplyLoadout(root);

            case "modulebuy":
                return ApplySet(
                    root,
                    "Slot",
                    "BuyItem");

            case "moduleretrieve":
                return ApplySet(
                    root,
                    "Slot",
                    "RetrievedItem");

            case "modulesell":
                return ApplyRemove(
                    root,
                    "Slot");

            case "modulestore":
                return ApplyStore(root);

            case "moduleswap":
                return ApplySwap(root);

            case "loadgame":
            case "shipyardbuy":
            case "shipyardswap":
            case "massmodulestore":
                return ResetForPendingLoadout(root);

            default:
                return false;
        }
    }

    private bool ApplyLoadout(JsonElement root)
    {
        slots.Clear();
        ship = Text(
            root,
            "Ship",
            ship);

        if (root.TryGetProperty(
                "Modules",
                out JsonElement modules)
            && modules.ValueKind
                == JsonValueKind.Array)
        {
            foreach (JsonElement module
                     in modules.EnumerateArray())
            {
                string slot = Text(
                    module,
                    "Slot");
                string item = Text(
                    module,
                    "Item");
                if (string.IsNullOrWhiteSpace(slot)
                    || string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                slots[slot] =
                    new MiningLoadoutModuleInput(
                        slot,
                        item,
                        Boolean(
                            module,
                            "On",
                            true));
            }
        }

        available = true;
        return true;
    }

    private bool ApplySet(
        JsonElement root,
        string slotProperty,
        string itemProperty)
    {
        if (!available)
        {
            return false;
        }

        string slot = Text(
            root,
            slotProperty);
        string item = Text(
            root,
            itemProperty);
        if (string.IsNullOrWhiteSpace(slot)
            || string.IsNullOrWhiteSpace(item))
        {
            return false;
        }

        slots[slot] =
            new MiningLoadoutModuleInput(
                slot,
                item,
                true);
        return true;
    }

    private bool ApplyRemove(
        JsonElement root,
        string slotProperty)
    {
        if (!available)
        {
            return false;
        }

        string slot = Text(
            root,
            slotProperty);
        return !string.IsNullOrWhiteSpace(slot)
               && slots.Remove(slot);
    }

    private bool ApplyStore(JsonElement root)
    {
        if (!available)
        {
            return false;
        }

        string slot = Text(
            root,
            "Slot");
        if (string.IsNullOrWhiteSpace(slot))
        {
            return false;
        }

        string replacement = Text(
            root,
            "ReplacementItem");
        if (string.IsNullOrWhiteSpace(replacement))
        {
            return slots.Remove(slot);
        }

        slots[slot] =
            new MiningLoadoutModuleInput(
                slot,
                replacement,
                true);
        return true;
    }

    private bool ApplySwap(JsonElement root)
    {
        if (!available)
        {
            return false;
        }

        string fromSlot = Text(
            root,
            "FromSlot");
        string toSlot = Text(
            root,
            "ToSlot");
        if (string.IsNullOrWhiteSpace(fromSlot)
            || string.IsNullOrWhiteSpace(toSlot)
            || fromSlot.Equals(
                toSlot,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        slots.TryGetValue(
            fromSlot,
            out MiningLoadoutModuleInput? fromExisting);
        slots.TryGetValue(
            toSlot,
            out MiningLoadoutModuleInput? toExisting);

        string fromItem = Text(
            root,
            "FromItem",
            fromExisting?.Item ?? string.Empty);
        string toItem = Text(
            root,
            "ToItem",
            toExisting?.Item ?? string.Empty);

        if (string.IsNullOrWhiteSpace(fromItem))
        {
            slots.Remove(toSlot);
        }
        else
        {
            slots[toSlot] =
                new MiningLoadoutModuleInput(
                    toSlot,
                    fromItem,
                    fromExisting?.Enabled ?? true);
        }

        if (string.IsNullOrWhiteSpace(toItem))
        {
            slots.Remove(fromSlot);
        }
        else
        {
            slots[fromSlot] =
                new MiningLoadoutModuleInput(
                    fromSlot,
                    toItem,
                    toExisting?.Enabled ?? true);
        }

        return true;
    }

    private bool ResetForPendingLoadout(
        JsonElement root)
    {
        string nextShip = Text(
            root,
            "Ship",
            ship);

        bool changed =
            available
            || slots.Count > 0
            || !string.Equals(
                ship,
                nextShip,
                StringComparison.OrdinalIgnoreCase);

        slots.Clear();
        ship = nextShip;
        available = false;
        return changed;
    }

    private static bool Equivalent(
        MiningLoadoutSnapshot left,
        MiningLoadoutSnapshot right)
    {
        if (left.Available != right.Available
            || !string.Equals(
                left.Ship,
                right.Ship,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return left.Modules.SequenceEqual(
            right.Modules);
    }

    private static string Text(
        JsonElement root,
        string property,
        string fallback = "")
    {
        if (!root.TryGetProperty(
                property,
                out JsonElement value)
            || value.ValueKind
                != JsonValueKind.String)
        {
            return fallback;
        }

        return value.GetString()
               ?? fallback;
    }

    private static bool Boolean(
        JsonElement root,
        string property,
        bool fallback)
    {
        if (!root.TryGetProperty(
                property,
                out JsonElement value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
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
}
