using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Mining;

public sealed class MiningSessionService : IJournalDataConsumer, IDisposable
{
    private readonly object sync = new();
    private readonly MiningSessionAccumulator accumulator;
    private readonly MiningSessionRepository repository;
    private MiningSessionSnapshot current = MiningSessionSnapshot.Empty;
    private bool started;
    private bool disposed;

    public static MiningSessionService Instance { get; } = new();

    public event EventHandler<MiningSessionChangedEventArgs>? Changed;

    public MiningSessionSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    private MiningSessionService()
        : this(new MiningSessionRepository())
    {
    }

    internal MiningSessionService(MiningSessionRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        accumulator = new MiningSessionAccumulator();
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

    public IReadOnlyList<MiningSessionSnapshot> LoadRecentSessions(int limit = 50) =>
        repository.LoadRecent(limit);

    public void OnJournalEvent(JournalEventReceivedEventArgs journalEvent)
    {
        MiningAccumulatorResult result;
        lock (sync)
        {
            result = accumulator.Apply(journalEvent);
            current = result.Current;
        }

        MiningSessionSnapshot? completed = result.CompletedSession;
        if (completed is not null
            && journalEvent.Origin == JournalEventOrigin.Live)
        {
            repository.Save(completed);
        }

        if (result.Changed)
        {
            Changed?.Invoke(this, new MiningSessionChangedEventArgs(result.Current, completed));
        }
    }

    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile)
    {
        MiningAccumulatorResult result;
        lock (sync)
        {
            result = accumulator.ApplyCompanion(companionFile);
            current = result.Current;
        }

        if (result.Changed)
        {
            Changed?.Invoke(this, new MiningSessionChangedEventArgs(result.Current));
        }
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
