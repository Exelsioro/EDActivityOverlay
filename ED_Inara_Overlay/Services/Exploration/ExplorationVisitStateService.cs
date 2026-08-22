using System.Text;
using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services;
using ED_Inara_Overlay.Services.Journal;

namespace ED_Inara_Overlay.Services.Exploration;

public sealed class ExplorationVisitStateService : IDisposable
{
    internal const int DestinationStabilityMilliseconds = 1_200;

    private readonly object sync = new();
    private readonly ExplorationVisitQueueEngine engine = new();

    private System.Threading.Timer? destinationTimer;
    private GameStateSnapshot latestState = GameStateSnapshot.Empty;
    private ExplorationVisitQueueSnapshot current =
        ExplorationVisitQueueSnapshot.Empty;

    private string relevantSignature = string.Empty;
    private int pendingDestinationBodyId = -1;
    private long pendingDestinationSystemAddress;
    private string pendingDestinationName = string.Empty;
    private int destinationGeneration;

    private bool started;
    private bool disposed;

    public static ExplorationVisitStateService Instance { get; } = new();

    public event EventHandler<ExplorationVisitStateChangedEventArgs>? Changed;

    public ExplorationVisitQueueSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    private ExplorationVisitStateService()
    {
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (started)
        {
            return;
        }

        started = true;

        JournalMonitorService.Instance.StateChanged += OnJournalStateChanged;
        ExplorationDataService.Instance.DataChanged += OnExplorationDataChanged;
        ExplorationHistoryService.Instance.HistoryChanged += OnHistoryChanged;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;

        Refresh(
            JournalMonitorService.Instance.Current,
            force: true);
    }

    public bool DeferBody(int bodyId)
    {
        ExplorationVisitQueueSnapshot? changed = null;

        lock (sync)
        {
            if (engine.DeferBody(bodyId))
            {
                if (pendingDestinationBodyId == bodyId)
                {
                    CancelPendingDestinationLocked();
                }

                current = engine.Current;
                changed = current;
            }
        }

        if (changed is not null)
        {
            RaiseChanged(changed);
            return true;
        }

        return false;
    }

    public bool ResumeBody(int bodyId)
    {
        ExplorationVisitQueueSnapshot? changed = null;

        lock (sync)
        {
            if (engine.ResumeBody(bodyId))
            {
                current = engine.Current;
                changed = current;
            }
        }

        if (changed is not null)
        {
            RaiseChanged(changed);
            return true;
        }

        return false;
    }

    public void Refresh()
    {
        Refresh(
            JournalMonitorService.Instance.Current,
            force: true);
    }

    private void OnJournalStateChanged(
        object? sender,
        GameStateChangedEventArgs e) =>
        Refresh(e.State, force: false);

    private void OnExplorationDataChanged(
        object? sender,
        ExplorationDataChangedEventArgs e) =>
        Refresh(
            JournalMonitorService.Instance.Current,
            force: true);

    private void OnHistoryChanged(
        object? sender,
        ExplorationHistoryChangedEventArgs e) =>
        Refresh(
            JournalMonitorService.Instance.Current,
            force: true);

    private void OnSettingsChanged(
        object? sender,
        SettingsChangedEventArgs e) =>
        Refresh(
            JournalMonitorService.Instance.Current,
            force: true);

    private void Refresh(
        GameStateSnapshot state,
        bool force)
    {
        string signature = BuildRelevantSignature(state);

        lock (sync)
        {
            latestState = state;

            if (!force
                && string.Equals(
                    relevantSignature,
                    signature,
                    StringComparison.Ordinal))
            {
                return;
            }

            relevantSignature = signature;
        }

        ExplorationSystemHistorySnapshot history =
            ExplorationHistoryService.Instance.LoadSystem(state);

        ExplorationSystemCatalog catalog =
            ExplorationSystemCatalogBuilder.Build(
                state,
                ExplorationDataService.Instance.Current,
                SettingsService.Instance.Settings.ExplorationSpoilerMode,
                history);

        ExplorationVisitQueueSnapshot next;

        lock (sync)
        {
            // A newer journal/status update changed systems while history/catalog
            // were being loaded. Ignore this stale refresh.
            if (!SameSystem(latestState, state))
            {
                return;
            }

            next = engine.Update(
                state,
                catalog,
                history);

            current = next;
            UpdatePendingDestinationLocked(state);
        }

        RaiseChanged(next);
    }

    private void UpdatePendingDestinationLocked(
        GameStateSnapshot state)
    {
        int bodyId = state.DestinationBodyId;

        if (!IsCurrentSystemBodyDestination(state)
            || !engine.CanActivateBody(bodyId))
        {
            CancelPendingDestinationLocked();
            return;
        }

        if (current.Active?.BodyId == bodyId)
        {
            CancelPendingDestinationLocked();
            return;
        }

        if (pendingDestinationBodyId == bodyId
            && pendingDestinationSystemAddress
                == state.DestinationSystemAddress
            && string.Equals(
                pendingDestinationName,
                state.DestinationName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        destinationGeneration++;
        int generation = destinationGeneration;

        pendingDestinationBodyId = bodyId;
        pendingDestinationSystemAddress =
            state.DestinationSystemAddress;
        pendingDestinationName =
            state.DestinationName;

        destinationTimer?.Dispose();
        destinationTimer = new System.Threading.Timer(
            _ => ConfirmPendingDestination(generation),
            null,
            DestinationStabilityMilliseconds,
            System.Threading.Timeout.Infinite);
    }

    private void ConfirmPendingDestination(int generation)
    {
        ExplorationVisitQueueSnapshot? changed = null;

        lock (sync)
        {
            if (disposed
                || generation != destinationGeneration
                || pendingDestinationBodyId < 0)
            {
                return;
            }

            GameStateSnapshot state = latestState;

            if (!IsCurrentSystemBodyDestination(state)
                || state.DestinationBodyId
                    != pendingDestinationBodyId
                || state.DestinationSystemAddress
                    != pendingDestinationSystemAddress
                || !string.Equals(
                    state.DestinationName,
                    pendingDestinationName,
                    StringComparison.OrdinalIgnoreCase))
            {
                CancelPendingDestinationLocked();
                return;
            }

            int bodyId = pendingDestinationBodyId;
            CancelPendingDestinationLocked();

            if (engine.ActivateBody(bodyId))
            {
                current = engine.Current;
                changed = current;
            }
        }

        if (changed is not null)
        {
            RaiseChanged(changed);
        }
    }

    private bool IsCurrentSystemBodyDestination(
        GameStateSnapshot state)
    {
        if (state.DestinationBodyId < 0)
        {
            return false;
        }

        if (state.DestinationSystemAddress != 0
            && state.SystemAddress != 0
            && state.DestinationSystemAddress
                != state.SystemAddress)
        {
            return false;
        }

        ExplorationVisitBodyState? candidate =
            current.Active?.BodyId == state.DestinationBodyId
                ? current.Active
                : current.Recommended
                    .Concat(current.Deferred)
                    .FirstOrDefault(
                        item => item.BodyId
                            == state.DestinationBodyId);

        if (candidate is null)
        {
            return false;
        }

        // Body id is the primary identity. Matching the name as well prevents a
        // station/carrier destination attached to a body from being interpreted
        // as selecting the planet itself.
        return string.IsNullOrWhiteSpace(state.DestinationName)
            || string.Equals(
                state.DestinationName.Trim(),
                candidate.BodyName.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    private void CancelPendingDestinationLocked()
    {
        destinationGeneration++;
        pendingDestinationBodyId = -1;
        pendingDestinationSystemAddress = 0;
        pendingDestinationName = string.Empty;

        destinationTimer?.Dispose();
        destinationTimer = null;
    }

    private static bool SameSystem(
        GameStateSnapshot left,
        GameStateSnapshot right)
    {
        if (left.SystemAddress != 0
            && right.SystemAddress != 0)
        {
            return left.SystemAddress == right.SystemAddress;
        }

        return string.Equals(
            left.StarSystem,
            right.StarSystem,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRelevantSignature(
        GameStateSnapshot state)
    {
        var result = new StringBuilder();

        result.Append(state.Commander)
            .Append('|')
            .Append(state.SystemAddress)
            .Append('|')
            .Append(state.StarSystem)
            .Append('|')
            .Append(state.DestinationSystemAddress)
            .Append('|')
            .Append(state.DestinationBodyId)
            .Append('|')
            .Append(state.DestinationName);

        foreach (ExplorationBodySnapshot body in
                 state.ExplorationBodies.OrderBy(item => item.BodyId))
        {
            result.Append("|B:")
                .Append(body.BodyId)
                .Append(':')
                .Append(body.IsScanned ? '1' : '0')
                .Append(body.IsMapped ? '1' : '0')
                .Append(body.MappingEfficient ? '1' : '0')
                .Append(':')
                .Append(body.BiologicalSignals)
                .Append(':')
                .Append(body.EstimatedMappingValue)
                .Append(':')
                .Append(string.Join(
                    ",",
                    body.Genuses.OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase)));
        }

        foreach (OrganicScanProgressSnapshot organic in
                 state.OrganicProgress
                     .OrderBy(item => item.BodyId)
                     .ThenBy(item => item.Genus)
                     .ThenBy(item => item.Species))
        {
            result.Append("|O:")
                .Append(organic.BodyId)
                .Append(':')
                .Append(organic.Genus)
                .Append(':')
                .Append(organic.Species)
                .Append(':')
                .Append(organic.Stage)
                .Append(':')
                .Append(organic.Completed ? '1' : '0');
        }

        return result.ToString();
    }

    private void RaiseChanged(
        ExplorationVisitQueueSnapshot value) =>
        Changed?.Invoke(
            this,
            new ExplorationVisitStateChangedEventArgs(value));

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (started)
        {
            JournalMonitorService.Instance.StateChanged -=
                OnJournalStateChanged;
            ExplorationDataService.Instance.DataChanged -=
                OnExplorationDataChanged;
            ExplorationHistoryService.Instance.HistoryChanged -=
                OnHistoryChanged;
            SettingsService.Instance.SettingsChanged -=
                OnSettingsChanged;

            started = false;
        }

        lock (sync)
        {
            CancelPendingDestinationLocked();
        }
    }
}