using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Exploration;

/// <summary>
/// Pins personal visit history to the destination announced by StartJump.
/// Status.json may advance to the following route hop while the current jump
/// animation is still in progress, so it cannot be the authority here.
/// </summary>
internal sealed class ExplorationJumpVisitTracker(
    Func<string, long, string, ExplorationSystemHistorySnapshot> loadHistory)
{
    private readonly object sync = new();
    private ExplorationJumpVisitStatusSnapshot current =
        ExplorationJumpVisitStatusSnapshot.Empty;

    public ExplorationJumpVisitStatusSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public bool Apply(
        JournalEventReceivedEventArgs journalEvent,
        GameStateSnapshot game)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        ArgumentNullException.ThrowIfNull(game);

        // Bootstrap contains completed jumps from the open journal. Replaying
        // them must not manufacture an "arrival" banner on application start.
        if (journalEvent.Origin != JournalEventOrigin.Live)
        {
            return false;
        }

        string eventName = journalEvent.EventName.Trim();
        if (eventName.Equals("StartJump", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyStartJump(journalEvent, game);
        }

        if (eventName.Equals("FSDJump", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyArrival(journalEvent);
        }

        if (eventName.Equals("LoadGame", StringComparison.OrdinalIgnoreCase)
            || eventName.Equals("Shutdown", StringComparison.OrdinalIgnoreCase)
            || eventName.Equals("Died", StringComparison.OrdinalIgnoreCase))
        {
            return Clear();
        }

        return false;
    }

    private bool ApplyStartJump(
        JournalEventReceivedEventArgs journalEvent,
        GameStateSnapshot game)
    {
        JsonElement root = journalEvent.Data;
        string jumpType = Text(root, "JumpType");
        if (!jumpType.Equals("Hyperspace", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string systemName = Text(root, "StarSystem");
        long systemAddress = Int64(root, "SystemAddress");
        if (string.IsNullOrWhiteSpace(systemName))
        {
            return false;
        }

        ExplorationSystemHistorySnapshot history =
            loadHistory(game.Commander, systemAddress, systemName);
        var next = new ExplorationJumpVisitStatusSnapshot(
            systemName,
            systemAddress,
            history.WasVisited,
            history.LastVisitedUtc,
            journalEvent.Timestamp,
            false);

        lock (sync)
        {
            current = next;
        }

        return true;
    }

    private bool ApplyArrival(JournalEventReceivedEventArgs journalEvent)
    {
        string systemName = Text(journalEvent.Data, "StarSystem");
        long systemAddress = Int64(journalEvent.Data, "SystemAddress");

        lock (sync)
        {
            if (!current.Available)
            {
                return false;
            }

            bool sameSystem = current.SystemAddress != 0 && systemAddress != 0
                ? current.SystemAddress == systemAddress
                : current.SystemName.Equals(systemName, StringComparison.OrdinalIgnoreCase);
            if (!sameSystem)
            {
                current = ExplorationJumpVisitStatusSnapshot.Empty;
                return true;
            }

            if (current.Arrived)
            {
                return false;
            }

            current = current with { Arrived = true };
            return true;
        }
    }

    private bool Clear()
    {
        lock (sync)
        {
            if (!current.Available)
            {
                return false;
            }

            current = ExplorationJumpVisitStatusSnapshot.Empty;
            return true;
        }
    }

    private static string Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static long Int64(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value)
        && value.TryGetInt64(out long parsed)
            ? parsed
            : 0;
}
