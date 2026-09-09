using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Mining;

/// <summary>
/// Best-effort collector activity tracker. Elite's Journal reports collector launches
/// but does not report every collision, target-completion death or range loss, so the
/// published count is intentionally approximate.
/// </summary>
public sealed class MiningCollectorTrackerService : IJournalDataConsumer, IDisposable
{
    private readonly object sync = new();
    private readonly List<DateTimeOffset> collectorLaunches = [];
    private bool started;
    private bool disposed;

    public static MiningCollectorTrackerService Instance { get; } = new();

    public event EventHandler<MiningCollectorActivityChangedEventArgs>? Changed;

    private MiningCollectorTrackerService()
    {
    }

    public MiningCollectorActivitySnapshot Current
    {
        get
        {
            lock (sync)
            {
                return MiningCollectorEstimator.Calculate(
                    MiningLoadoutService.Instance.Current,
                    collectorLaunches,
                    DateTimeOffset.UtcNow);
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

        JournalMonitorService.Instance.Events.Register(this);
        started = true;
    }

    public void OnJournalEvent(JournalEventReceivedEventArgs journalEvent)
    {
        bool changed = false;
        lock (sync)
        {
            string eventName = journalEvent.EventName.Trim().ToLowerInvariant();
            switch (eventName)
            {
                case "launchdrone":
                    if (GetString(journalEvent.Data, "Type")
                        .Equals("Collection", StringComparison.OrdinalIgnoreCase))
                    {
                        collectorLaunches.Add(journalEvent.Timestamp);
                        changed = true;
                    }
                    break;

                case "loadgame":
                case "fsdjump":
                case "carrierjump":
                case "supercruiseentry":
                case "docked":
                case "died":
                case "shutdown":
                    if (collectorLaunches.Count > 0)
                    {
                        collectorLaunches.Clear();
                        changed = true;
                    }
                    break;
            }
        }

        if (changed)
        {
            Changed?.Invoke(
                this,
                new MiningCollectorActivityChangedEventArgs(Current));
        }
    }

    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile)
    {
    }

    private static string GetString(
        System.Text.Json.JsonElement element,
        string property) =>
        element.TryGetProperty(property, out System.Text.Json.JsonElement value)
        && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

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

        lock (sync)
        {
            collectorLaunches.Clear();
        }
    }
}
