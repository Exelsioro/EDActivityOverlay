using System.Text;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Exploration;

public sealed class ExplorationVisitStateService : IDisposable
{
    internal const int DestinationStabilityMilliseconds = 1_200;

    private readonly object sync = new();
    private readonly ExplorationVisitQueueEngine engine = new();

    private System.Threading.Timer? destinationTimer;
    private GameStateSnapshot latestState = GameStateSnapshot.Empty;
    private ExplorationSystemCatalog latestCatalog =
        ExplorationSystemCatalog.Empty;
    private ExplorationVisitQueueSnapshot current =
        ExplorationVisitQueueSnapshot.Empty;

    private string relevantSignature = string.Empty;
    private int pendingDestinationBodyId = -1;
    private long pendingDestinationSystemAddress;
    private string pendingDestinationName = string.Empty;
    private int destinationGeneration;
    private int refreshGeneration;

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

                // If the player is still targeting the resumed body, allow the
                // normal stability window to make it active again.
                UpdatePendingDestinationLocked(latestState);
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
        int generation;

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
            generation = ++refreshGeneration;
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
            // A newer journal/status/data refresh may have completed while this
            // history/catalog build was running. Reject this result even when
            // both refreshes belong to the same star system.
            if (generation != refreshGeneration
                || !SameSystem(latestState, state))
            {
                return;
            }

            latestCatalog = catalog;

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
        if (!TryResolveCurrentSystemBodyDestination(
                state,
                out int bodyId,
                out string resolvedName))
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
                resolvedName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        destinationGeneration++;
        int generation = destinationGeneration;

        pendingDestinationBodyId = bodyId;
        pendingDestinationSystemAddress =
            state.DestinationSystemAddress;
        pendingDestinationName = resolvedName;

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

            if (!TryResolveCurrentSystemBodyDestination(
                    state,
                    out int resolvedBodyId,
                    out string resolvedName)
                || resolvedBodyId != pendingDestinationBodyId
                || state.DestinationSystemAddress
                    != pendingDestinationSystemAddress
                || !string.Equals(
                    resolvedName,
                    pendingDestinationName,
                    StringComparison.OrdinalIgnoreCase))
            {
                CancelPendingDestinationLocked();
                return;
            }

            int bodyId = pendingDestinationBodyId;
            CancelPendingDestinationLocked();

            if (engine.SelectDestinationBody(bodyId))
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

    private bool TryResolveCurrentSystemBodyDestination(
        GameStateSnapshot state,
        out int bodyId,
        out string bodyName)
    {
        int resolvedBodyId = state.DestinationBodyId;

        bodyId = resolvedBodyId;
        bodyName = string.Empty;

        if (resolvedBodyId < 0)
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

        bodyName = latestCatalog.Bodies
            .FirstOrDefault(item => item.BodyId == resolvedBodyId)
            ?.Name
            ?? state.ExplorationBodies
                .FirstOrDefault(item => item.BodyId == resolvedBodyId)
                ?.Name
            ?? FindQueueBodyName(resolvedBodyId)
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(bodyName))
        {
            return false;
        }

        // Status.json can expose a station/carrier with a Body id. Require its
        // visible destination name to match the known body name so that such a
        // target is not interpreted as switching exploration bodies.
        return string.IsNullOrWhiteSpace(state.DestinationName)
            || string.Equals(
                state.DestinationName.Trim(),
                bodyName.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    private string? FindQueueBodyName(int bodyId)
    {
        if (current.Active?.BodyId == bodyId)
        {
            return current.Active.BodyName;
        }

        return current.Recommended
            .Concat(current.Deferred)
            .Concat(current.Completed)
            .FirstOrDefault(item => item.BodyId == bodyId)
            ?.BodyName;
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
                .Append(body.Name)
                .Append(':')
                .Append(body.IsScanned ? '1' : '0')
                .Append(body.IsMapped ? '1' : '0')
                .Append(body.MappingEfficient ? '1' : '0')
                .Append(':')
                .Append(body.WasDiscovered ? '1' : '0')
                .Append(body.WasMapped ? '1' : '0')
                .Append(':')
                .Append(body.BodyType)
                .Append(':')
                .Append(body.BodyClass)
                .Append(':')
                .Append(body.Terraformable ? '1' : '0')
                .Append(':')
                .Append(body.Landable ? '1' : '0')
                .Append(':')
                .Append(body.DistanceFromArrivalLs)
                .Append(':')
                .Append(body.GravityG)
                .Append(':')
                .Append(body.BiologicalSignals)
                .Append(':')
                .Append(body.EstimatedScanValue)
                .Append(':')
                .Append(body.EstimatedMappingValue)
                .Append(':')
                .Append(body.EstimatedEfficientMappingValue)
                .Append(':')
                .Append(string.Join(
                    ",",
                    body.GenusKeys.Count > 0
                        ? body.GenusKeys.OrderBy(
                            value =>
                                value,
                            StringComparer.OrdinalIgnoreCase)
                        : body.Genuses.OrderBy(
                            value =>
                                value,
                            StringComparer.OrdinalIgnoreCase)));
        }
        foreach (OrganicScanProgressSnapshot organic in
                 state.OrganicProgress
                     .OrderBy(item => item.BodyId)
                     .ThenBy(item => item.GenusKey)
                     .ThenBy(item => item.SpeciesKey)
                     .ThenBy(item => item.Genus)
                     .ThenBy(item => item.Species))
        {
            result.Append("|O:")
                .Append(organic.BodyId)
                .Append(':')
                .Append(organic.GenusKey)
                .Append(':')
                .Append(organic.SpeciesKey)
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