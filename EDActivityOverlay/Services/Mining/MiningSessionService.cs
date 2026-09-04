using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Mining;

public sealed class MiningSessionService : IJournalDataConsumer, IDisposable
{
    private readonly object sync = new();
    private readonly MiningSessionAccumulator accumulator;
    private readonly MiningSessionRepository repository;
    private readonly Func<MiningDestinationSnapshot> destinationProvider;
    private MiningSessionSnapshot current = MiningSessionSnapshot.Empty;
    private Guid destinationSessionId = Guid.Empty;
    private MiningSessionDestinationContext linkedDestination =
        MiningSessionDestinationContext.Empty;
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

    internal MiningSessionService(
        MiningSessionRepository repository,
        Func<MiningDestinationSnapshot>? destinationProvider = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.destinationProvider =
            destinationProvider ?? (() => MiningDestinationService.Instance.Current);
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
        MiningSessionSnapshot publishedCurrent;
        MiningSessionSnapshot? completed;
        lock (sync)
        {
            result = accumulator.Apply(journalEvent);

            // Complete the old session before processing an idle/new current
            // snapshot so its captured destination context cannot be lost.
            completed = result.CompletedSession is null
                ? null
                : EnrichSession(result.CompletedSession);

            publishedCurrent = EnrichSession(result.Current);
            current = publishedCurrent;

            if (completed is not null
                && publishedCurrent.State == MiningSessionState.Idle)
            {
                ResetDestinationLink();
            }
        }

        if (completed is not null
            && journalEvent.Origin == JournalEventOrigin.Live)
        {
            repository.Save(completed);
        }

        if (result.Changed)
        {
            Changed?.Invoke(
                this,
                new MiningSessionChangedEventArgs(
                    publishedCurrent,
                    completed));
        }
    }

    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile)
    {
        MiningAccumulatorResult result;
        MiningSessionSnapshot publishedCurrent;
        lock (sync)
        {
            result = accumulator.ApplyCompanion(companionFile);
            publishedCurrent = EnrichSession(result.Current);
            current = publishedCurrent;
        }

        if (result.Changed)
        {
            Changed?.Invoke(
                this,
                new MiningSessionChangedEventArgs(
                    publishedCurrent));
        }
    }

    private MiningSessionSnapshot EnrichSession(
        MiningSessionSnapshot session)
    {
        session = EnrichRingContext(session);
        if (session.State == MiningSessionState.Idle)
        {
            return session;
        }

        if (destinationSessionId != session.SessionId)
        {
            destinationSessionId = session.SessionId;
            linkedDestination = MiningSessionDestinationContext.Empty;
        }

        if (!linkedDestination.Available)
        {
            linkedDestination = MiningSessionDestinationLinker.Capture(
                session,
                destinationProvider());
        }
        else
        {
            linkedDestination = MiningSessionDestinationLinker.Reconcile(
                session,
                linkedDestination);
        }

        return session with
        {
            DestinationContext = linkedDestination
        };
    }

    private void ResetDestinationLink()
    {
        destinationSessionId = Guid.Empty;
        linkedDestination = MiningSessionDestinationContext.Empty;
    }

    private static MiningSessionSnapshot EnrichRingContext(
        MiningSessionSnapshot session)
    {
        if (session.State == MiningSessionState.Idle)
        {
            return session;
        }

        MiningRingContextSnapshot ring =
            MiningRingContextService.Instance.Resolve(
                session.RingName,
                session.BodyName,
                session.SystemAddress,
                session.SystemName);

        if (!ring.Available)
        {
            return session;
        }

        return session with
        {
            RingName = string.IsNullOrWhiteSpace(session.RingName)
                ? ring.RingName
                : session.RingName,
            RingClass = ring.RingClass,
            ReserveLevel = ring.ReserveLevel,
            HotspotCommodityIds = ring.HotspotCommodityIds.ToArray()
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
