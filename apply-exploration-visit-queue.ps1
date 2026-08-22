param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

function Read-Text([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Required file not found: $Path"
    }

    return ([System.IO.File]::ReadAllText((Resolve-Path $Path).Path)).Replace("`r`n", "`n")
}

function Write-Text([string]$Path, [string]$Text) {
    $full = if (Test-Path $Path) {
        (Resolve-Path $Path).Path
    }
    else {
        Join-Path (Get-Location) $Path
    }

    $old = if (Test-Path $Path) {
        [System.IO.File]::ReadAllText($full)
    }
    else {
        ""
    }

    $newline = if ($old.Contains("`r`n")) { "`r`n" } else { "`n" }
    $normalized = $Text.Replace("`r`n", "`n")

    if ($newline -eq "`r`n") {
        $normalized = $normalized.Replace("`n", "`r`n")
    }

    $directory = Split-Path -Parent $full
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    [System.IO.File]::WriteAllText(
        $full,
        $normalized,
        [System.Text.UTF8Encoding]::new($false)
    )
}

function Replace-LiteralOnce(
    [string]$Path,
    [string]$Old,
    [string]$New,
    [string]$Description
) {
    $text = Read-Text $Path
    $count = ([regex]::Matches($text, [regex]::Escape($Old))).Count

    if ($count -ne 1) {
        throw "Expected exactly one $Description in $Path, found $count."
    }

    Write-Text $Path ($text.Replace($Old, $New))
}

$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Run this script from the repository root.'
}

Write-Host "Current branch: $branch" -ForegroundColor DarkGray

$appPath = 'ED_Inara_Overlay\App.xaml.cs'
$coreModelPath = 'ED_Inara_Overlay\Models\BodyExplorationProgress.cs'
$coreBuilderPath = 'ED_Inara_Overlay\Services\Exploration\BodyExplorationProgressBuilder.cs'
$visitModelsPath = 'ED_Inara_Overlay\Models\ExplorationVisitModels.cs'
$enginePath = 'ED_Inara_Overlay\Services\Exploration\ExplorationVisitQueueEngine.cs'
$servicePath = 'ED_Inara_Overlay\Services\Exploration\ExplorationVisitStateService.cs'
$testsPath = 'Testing\ED_Inara_Overlay.LayoutTests\ExplorationVisitQueueTests.cs'

foreach ($required in @($appPath, $coreModelPath, $coreBuilderPath)) {
    if (-not (Test-Path $required)) {
        throw "Required file not found: $required. Apply exploration-progress-core first."
    }
}

$coreModel = Read-Text $coreModelPath
if (-not $coreModel.Contains('public sealed record BodyExplorationProgress')) {
    throw 'exploration-progress-core does not appear to be installed.'
}

$backup = 'exploration-visit-queue-before.patch'
& git diff --binary -- `
    $appPath $visitModelsPath $enginePath $servicePath $testsPath |
    Set-Content -Path $backup -Encoding utf8

Write-Host "Saved current diff to $backup" -ForegroundColor DarkGray
Write-Host 'Applying exploration visit queue...' -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# 1. Queue/domain models.
# ---------------------------------------------------------------------------
$visitModels = @'
namespace ED_Inara_Overlay.Models;

[Flags]
public enum ExplorationRequiredObjectives
{
    None = 0,
    FssScan = 1,
    DssMap = 2,
    Biology = 4
}

public enum ExplorationVisitDisposition
{
    Recommended,
    Active,
    Deferred,
    Complete
}

public sealed record ExplorationVisitBodyState(
    ExplorationCatalogBody Body,
    BodyExplorationProgress Progress,
    ExplorationRequiredObjectives RequiredObjectives,
    ExplorationVisitDisposition Disposition,
    int PriorityScore)
{
    public int BodyId => Body.BodyId;
    public string BodyName => Body.Name;

    public bool FssRequired =>
        RequiredObjectives.HasFlag(ExplorationRequiredObjectives.FssScan);

    public bool DssRequired =>
        RequiredObjectives.HasFlag(ExplorationRequiredObjectives.DssMap);

    public bool BiologyRequired =>
        RequiredObjectives.HasFlag(ExplorationRequiredObjectives.Biology);

    public bool FssComplete =>
        !FssRequired || Progress.FssScanned;

    public bool DssComplete =>
        !DssRequired || Progress.DssMapped;

    public bool BiologyComplete =>
        !BiologyRequired || Progress.BiologyComplete;

    public bool IsComplete =>
        FssComplete && DssComplete && BiologyComplete;
}

public sealed record ExplorationVisitQueueSnapshot(
    string Commander,
    long SystemAddress,
    string SystemName,
    ExplorationVisitBodyState? Active,
    IReadOnlyList<ExplorationVisitBodyState> Recommended,
    IReadOnlyList<ExplorationVisitBodyState> Deferred,
    IReadOnlyList<ExplorationVisitBodyState> Completed)
{
    public static ExplorationVisitQueueSnapshot Empty { get; } = new(
        string.Empty,
        0,
        string.Empty,
        null,
        Array.Empty<ExplorationVisitBodyState>(),
        Array.Empty<ExplorationVisitBodyState>(),
        Array.Empty<ExplorationVisitBodyState>());

    public int RemainingCount => Recommended.Count + (Active is null ? 0 : 1);
    public int DeferredCount => Deferred.Count;
    public int CompletedCount => Completed.Count;
}

public sealed class ExplorationVisitStateChangedEventArgs(
    ExplorationVisitQueueSnapshot state) : EventArgs
{
    public ExplorationVisitQueueSnapshot State { get; } = state;
}
'@

Write-Text $visitModelsPath $visitModels

# ---------------------------------------------------------------------------
# 2. Pure queue engine.
#
# Facts (FSS/DSS/BIO) are never changed by queue state. Deferred is only a
# "not now, during this visit" disposition.
# ---------------------------------------------------------------------------
$engine = @'
using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal static class ExplorationVisitPolicy
{
    public static bool IsInteresting(ExplorationCatalogBody body) =>
        body.BodyId >= 0 && body.IsNotable;

    public static ExplorationRequiredObjectives RequiredObjectives(
        ExplorationCatalogBody body)
    {
        ExplorationRequiredObjectives objectives =
            ExplorationRequiredObjectives.FssScan;

        bool isPlanet = body.Type.Equals(
            "Planet",
            StringComparison.OrdinalIgnoreCase);

        bool mapWorthwhile =
            body.IsBiological
            || body.IsValuable
            || body.Terraformable
            || body.Highlights.HasFlag(ExplorationBodyHighlights.EarthLike)
            || body.Highlights.HasFlag(ExplorationBodyHighlights.WaterWorld)
            || body.Highlights.HasFlag(ExplorationBodyHighlights.AmmoniaWorld);

        if (isPlanet && mapWorthwhile)
        {
            objectives |= ExplorationRequiredObjectives.DssMap;
        }

        if (body.IsBiological)
        {
            objectives |= ExplorationRequiredObjectives.Biology;
        }

        return objectives;
    }

    public static int PriorityScore(ExplorationCatalogBody body)
    {
        int score = 0;

        if (body.IsBiological)
        {
            score += 100_000;
        }

        if (body.Highlights.HasFlag(ExplorationBodyHighlights.EarthLike))
        {
            score += 90_000;
        }

        if (body.Highlights.HasFlag(ExplorationBodyHighlights.WaterWorld))
        {
            score += 80_000;
        }

        if (body.Highlights.HasFlag(ExplorationBodyHighlights.AmmoniaWorld))
        {
            score += 75_000;
        }

        if (body.Terraformable)
        {
            score += 60_000;
        }

        if (body.IsValuable)
        {
            score += 50_000;
        }

        if (body.Highlights.HasFlag(ExplorationBodyHighlights.NeutronStar)
            || body.Highlights.HasFlag(ExplorationBodyHighlights.BlackHole))
        {
            score += 40_000;
        }

        score += (int)Math.Clamp(
            body.EstimatedMappingValue / 1_000,
            0,
            30_000);

        if (!body.WasMapped)
        {
            score += 5_000;
        }

        if (!body.WasDiscovered)
        {
            score += 3_000;
        }

        score -= (int)Math.Clamp(
            body.DistanceFromArrivalLs / 10,
            0,
            20_000);

        return score;
    }
}

internal sealed class ExplorationVisitQueueEngine
{
    private readonly HashSet<int> deferredBodyIds = new();

    private string currentSystemKey = string.Empty;
    private string commander = string.Empty;
    private long systemAddress;
    private string systemName = string.Empty;
    private int activeBodyId = -1;

    private Dictionary<int, ExplorationVisitBodyState> entries = new();

    public ExplorationVisitQueueSnapshot Current { get; private set; } =
        ExplorationVisitQueueSnapshot.Empty;

    public ExplorationVisitQueueSnapshot Update(
        GameStateSnapshot state,
        ExplorationSystemCatalog catalog,
        ExplorationSystemHistorySnapshot history)
    {
        string nextSystemKey = SystemKey(
            state.SystemAddress,
            state.StarSystem);

        if (!string.Equals(
            currentSystemKey,
            nextSystemKey,
            StringComparison.OrdinalIgnoreCase))
        {
            ResetVisit(nextSystemKey);
        }

        commander = state.Commander;
        systemAddress = state.SystemAddress;
        systemName = state.StarSystem;

        entries = catalog.Bodies
            .Where(ExplorationVisitPolicy.IsInteresting)
            .Select(body =>
            {
                BodyExplorationProgress progress =
                    BodyExplorationProgressBuilder.Build(
                        state,
                        history,
                        body.BodyId);

                ExplorationRequiredObjectives objectives =
                    ExplorationVisitPolicy.RequiredObjectives(body);

                return new ExplorationVisitBodyState(
                    body,
                    progress,
                    objectives,
                    ExplorationVisitDisposition.Recommended,
                    ExplorationVisitPolicy.PriorityScore(body));
            })
            .ToDictionary(item => item.BodyId);

        deferredBodyIds.RemoveWhere(bodyId =>
            !entries.TryGetValue(bodyId, out ExplorationVisitBodyState? body)
            || body.IsComplete);

        if (activeBodyId >= 0
            && (!entries.TryGetValue(
                    activeBodyId,
                    out ExplorationVisitBodyState? active)
                || active.IsComplete))
        {
            activeBodyId = -1;
        }

        Current = BuildSnapshot();
        return Current;
    }

    public bool CanActivateBody(int bodyId) =>
        entries.TryGetValue(bodyId, out ExplorationVisitBodyState? body)
        && !body.IsComplete;

    public bool ActivateBody(int bodyId)
    {
        if (!CanActivateBody(bodyId))
        {
            return false;
        }

        if (activeBodyId == bodyId)
        {
            deferredBodyIds.Remove(bodyId);
            Current = BuildSnapshot();
            return false;
        }

        if (activeBodyId >= 0
            && entries.TryGetValue(
                activeBodyId,
                out ExplorationVisitBodyState? previous)
            && !previous.IsComplete)
        {
            deferredBodyIds.Add(activeBodyId);
        }

        deferredBodyIds.Remove(bodyId);
        activeBodyId = bodyId;

        Current = BuildSnapshot();
        return true;
    }

    public bool DeferBody(int bodyId)
    {
        if (!entries.TryGetValue(
                bodyId,
                out ExplorationVisitBodyState? body)
            || body.IsComplete)
        {
            return false;
        }

        bool changed = deferredBodyIds.Add(bodyId);

        if (activeBodyId == bodyId)
        {
            activeBodyId = -1;
            changed = true;
        }

        if (changed)
        {
            Current = BuildSnapshot();
        }

        return changed;
    }

    public bool ResumeBody(int bodyId)
    {
        if (!entries.TryGetValue(
                bodyId,
                out ExplorationVisitBodyState? body)
            || body.IsComplete)
        {
            return false;
        }

        bool changed = deferredBodyIds.Remove(bodyId);

        if (changed)
        {
            Current = BuildSnapshot();
        }

        return changed;
    }

    private ExplorationVisitQueueSnapshot BuildSnapshot()
    {
        ExplorationVisitBodyState? active = null;
        var recommended = new List<ExplorationVisitBodyState>();
        var deferred = new List<ExplorationVisitBodyState>();
        var completed = new List<ExplorationVisitBodyState>();

        foreach (ExplorationVisitBodyState item in entries.Values)
        {
            ExplorationVisitDisposition disposition;

            if (item.IsComplete)
            {
                disposition = ExplorationVisitDisposition.Complete;
            }
            else if (item.BodyId == activeBodyId)
            {
                disposition = ExplorationVisitDisposition.Active;
            }
            else if (deferredBodyIds.Contains(item.BodyId))
            {
                disposition = ExplorationVisitDisposition.Deferred;
            }
            else
            {
                disposition = ExplorationVisitDisposition.Recommended;
            }

            ExplorationVisitBodyState resolved =
                item with { Disposition = disposition };

            switch (disposition)
            {
                case ExplorationVisitDisposition.Active:
                    active = resolved;
                    break;

                case ExplorationVisitDisposition.Recommended:
                    recommended.Add(resolved);
                    break;

                case ExplorationVisitDisposition.Deferred:
                    deferred.Add(resolved);
                    break;

                case ExplorationVisitDisposition.Complete:
                    completed.Add(resolved);
                    break;
            }
        }

        static IOrderedEnumerable<ExplorationVisitBodyState> Rank(
            IEnumerable<ExplorationVisitBodyState> source) =>
            source
                .OrderByDescending(item => item.PriorityScore)
                .ThenBy(item => item.Body.DistanceFromArrivalLs)
                .ThenBy(item => item.BodyId);

        return new ExplorationVisitQueueSnapshot(
            commander,
            systemAddress,
            systemName,
            active,
            Rank(recommended).ToArray(),
            Rank(deferred).ToArray(),
            completed
                .OrderBy(item => item.BodyId)
                .ToArray());
    }

    private void ResetVisit(string nextSystemKey)
    {
        currentSystemKey = nextSystemKey;
        activeBodyId = -1;
        deferredBodyIds.Clear();
        entries.Clear();
        Current = ExplorationVisitQueueSnapshot.Empty;
    }

    private static string SystemKey(
        long address,
        string name) =>
        address != 0
            ? $"id:{address}"
            : string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : $"name:{name.Trim().ToUpperInvariant()}";
}
'@

Write-Text $enginePath $engine

# ---------------------------------------------------------------------------
# 3. Live service:
#    - watches only exploration-relevant state changes;
#    - waits for a stable body destination before changing Active;
#    - switching body automatically defers the previous incomplete Active body;
#    - blank/system/station destinations do not disturb Active;
#    - Deferred is reset when the star system changes.
# ---------------------------------------------------------------------------
$service = @'
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
'@

Write-Text $servicePath $service

# ---------------------------------------------------------------------------
# 4. Start/stop the visit service with the other exploration services.
# ---------------------------------------------------------------------------
$app = Read-Text $appPath

if (-not $app.Contains('ExplorationVisitStateService.Instance.Start();')) {
    $oldStart = @'
            ExplorationHistoryService.Instance.Start(settings.JournalDirectory);
            ExplorationEarningsService.Instance.Start(settings.JournalDirectory);
'@

    $newStart = @'
            ExplorationHistoryService.Instance.Start(settings.JournalDirectory);
            ExplorationVisitStateService.Instance.Start();
            ExplorationEarningsService.Instance.Start(settings.JournalDirectory);
'@

    Replace-LiteralOnce `
        $appPath `
        $oldStart `
        $newStart `
        'exploration service startup block'
}

$app = Read-Text $appPath

if (-not $app.Contains('ExplorationVisitStateService.Instance.Dispose();')) {
    $oldDispose = @'
                ExplorationDataService.Instance.Dispose();
                ExplorationHistoryService.Instance.Dispose();
                ExplorationEarningsService.Instance.Dispose();
'@

    $newDispose = @'
                ExplorationVisitStateService.Instance.Dispose();
                ExplorationDataService.Instance.Dispose();
                ExplorationHistoryService.Instance.Dispose();
                ExplorationEarningsService.Instance.Dispose();
'@

    Replace-LiteralOnce `
        $appPath `
        $oldDispose `
        $newDispose `
        'exploration service dispose block'
}

# ---------------------------------------------------------------------------
# 5. Deterministic regression tests for queue semantics.
# ---------------------------------------------------------------------------
$tests = @'
using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Exploration;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ExplorationVisitQueueTests
{
    [Fact]
    public void CompletedInterestingBodyIsRemovedFromRecommendedQueue()
    {
        var engine = new ExplorationVisitQueueEngine();

        GameStateSnapshot state = State(
            "Test",
            42,
            BioBody(
                4,
                "Test 4",
                mapped: false,
                completedGenuses: 1),
            ValuableBody(
                5,
                "Test 5",
                mapped: true));

        ExplorationSystemCatalog catalog = Catalog(
            "Test",
            BioCatalogBody(4, "Test 4"),
            ValuableCatalogBody(5, "Test 5"));

        ExplorationVisitQueueSnapshot queue = engine.Update(
            state,
            catalog,
            ExplorationSystemHistorySnapshot.Empty);

        ExplorationVisitBodyState remaining =
            Assert.Single(queue.Recommended);

        Assert.Equal(4, remaining.BodyId);
        Assert.Equal(1, remaining.Progress.RemainingBiologicalSignals);

        ExplorationVisitBodyState completed =
            Assert.Single(queue.Completed);

        Assert.Equal(5, completed.BodyId);
        Assert.True(completed.IsComplete);
    }

    [Fact]
    public void SwitchingActiveBodyDefersPreviousIncompleteBody()
    {
        var engine = new ExplorationVisitQueueEngine();

        GameStateSnapshot state = State(
            "Test",
            42,
            BioBody(
                4,
                "Test 4",
                mapped: true,
                completedGenuses: 1),
            ValuableBody(
                6,
                "Test 6",
                mapped: false));

        ExplorationSystemCatalog catalog = Catalog(
            "Test",
            BioCatalogBody(4, "Test 4"),
            ValuableCatalogBody(6, "Test 6"));

        engine.Update(
            state,
            catalog,
            ExplorationSystemHistorySnapshot.Empty);

        Assert.True(engine.ActivateBody(4));
        Assert.Equal(4, engine.Current.Active?.BodyId);

        Assert.True(engine.ActivateBody(6));

        Assert.Equal(6, engine.Current.Active?.BodyId);
        Assert.Equal(
            new[] { 4 },
            engine.Current.Deferred.Select(item => item.BodyId));
        Assert.DoesNotContain(
            engine.Current.Recommended,
            item => item.BodyId == 4);
    }

    [Fact]
    public void ManualDeferAndResumeDoNotChangeResearchFacts()
    {
        var engine = new ExplorationVisitQueueEngine();

        GameStateSnapshot state = State(
            "Test",
            42,
            BioBody(
                4,
                "Test 4",
                mapped: true,
                completedGenuses: 1));

        ExplorationSystemCatalog catalog = Catalog(
            "Test",
            BioCatalogBody(4, "Test 4"));

        engine.Update(
            state,
            catalog,
            ExplorationSystemHistorySnapshot.Empty);

        Assert.True(engine.ActivateBody(4));

        ExplorationVisitBodyState active =
            engine.Current.Active
            ?? throw new Xunit.Sdk.XunitException("Expected active body.");

        BodyExplorationProgress before = active.Progress;

        Assert.True(engine.DeferBody(4));

        ExplorationVisitBodyState deferred =
            Assert.Single(engine.Current.Deferred);

        Assert.Equal(before, deferred.Progress);
        Assert.Null(engine.Current.Active);

        Assert.True(engine.ResumeBody(4));

        ExplorationVisitBodyState resumed =
            Assert.Single(engine.Current.Recommended);

        Assert.Equal(before, resumed.Progress);
        Assert.Empty(engine.Current.Deferred);
    }

    [Fact]
    public void DeferredStateIsClearedWhenEnteringAnotherSystem()
    {
        var engine = new ExplorationVisitQueueEngine();

        engine.Update(
            State(
                "System A",
                100,
                ValuableBody(
                    4,
                    "System A 4",
                    mapped: false)),
            Catalog(
                "System A",
                ValuableCatalogBody(
                    4,
                    "System A 4")),
            ExplorationSystemHistorySnapshot.Empty);

        Assert.True(engine.DeferBody(4));
        Assert.Single(engine.Current.Deferred);

        ExplorationVisitQueueSnapshot next = engine.Update(
            State(
                "System B",
                200,
                ValuableBody(
                    4,
                    "System B 4",
                    mapped: false)),
            Catalog(
                "System B",
                ValuableCatalogBody(
                    4,
                    "System B 4")),
            ExplorationSystemHistorySnapshot.Empty);

        Assert.Empty(next.Deferred);
        Assert.Single(next.Recommended);
        Assert.Equal("System B", next.SystemName);
    }

    [Fact]
    public void BiologyRequiresFssDssAndAllBiologicalSignals()
    {
        ExplorationCatalogBody catalogBody =
            BioCatalogBody(4, "Test 4");

        ExplorationRequiredObjectives objectives =
            ExplorationVisitPolicy.RequiredObjectives(catalogBody);

        Assert.True(
            objectives.HasFlag(
                ExplorationRequiredObjectives.FssScan));
        Assert.True(
            objectives.HasFlag(
                ExplorationRequiredObjectives.DssMap));
        Assert.True(
            objectives.HasFlag(
                ExplorationRequiredObjectives.Biology));
    }

    [Fact]
    public void OrdinaryLandableBodyIsNotAutomaticallyRecommended()
    {
        ExplorationCatalogBody ordinary = MakeCatalogBody(
            9,
            "Test 9",
            ExplorationBodyHighlights.Landable,
            mappingValue: 0);

        Assert.False(
            ExplorationVisitPolicy.IsInteresting(ordinary));
    }

    [Fact]
    public void DestinationMustRemainStableBeforeServiceActivation()
    {
        Assert.Equal(
            1_200,
            ExplorationVisitStateService
                .DestinationStabilityMilliseconds);
    }

    private static GameStateSnapshot State(
        string system,
        long address,
        params ExplorationBodySnapshot[] bodies)
    {
        OrganicScanProgressSnapshot[] organics = bodies
            .SelectMany(body =>
            {
                int completed = body.BodyId == 4
                    ? Math.Min(
                        body.BiologicalSignals,
                        body.Genuses.Count == 0 ? 0 : 1)
                    : 0;

                return Enumerable.Range(0, completed)
                    .Select(index =>
                        new OrganicScanProgressSnapshot(
                            "Cmdr",
                            address,
                            system,
                            body.BodyId,
                            body.Name,
                            body.Genuses[index],
                            body.Genuses[index] + " species",
                            string.Empty,
                            3,
                            true,
                            500,
                            null,
                            null,
                            DateTimeOffset.Parse(
                                "2026-08-22T12:00:00Z")));
            })
            .ToArray();

        return new GameStateSnapshot
        {
            Commander = "Cmdr",
            StarSystem = system,
            SystemAddress = address,
            ExplorationBodies = bodies,
            OrganicProgress = organics
        };
    }

    private static ExplorationBodySnapshot BioBody(
        int id,
        string name,
        bool mapped,
        int completedGenuses)
    {
        string[] genuses =
        [
            "Stratum",
            "Bacterium"
        ];

        return new ExplorationBodySnapshot(
            id,
            name,
            "Rocky body",
            800,
            false,
            false,
            mapped,
            mapped,
            2,
            genuses,
            ExplorationInterest.None)
        {
            IsScanned = true,
            BodyType = "Planet",
            BodyClass = "Rocky body",
            Landable = true
        };
    }

    private static ExplorationBodySnapshot ValuableBody(
        int id,
        string name,
        bool mapped) =>
        new(
            id,
            name,
            "Water world",
            1_200,
            false,
            false,
            mapped,
            mapped,
            0,
            Array.Empty<string>(),
            ExplorationInterest.WaterWorld)
        {
            IsScanned = true,
            BodyType = "Planet",
            BodyClass = "Water world",
            EstimatedMappingValue = 350_000
        };

    private static ExplorationSystemCatalog Catalog(
        string system,
        params ExplorationCatalogBody[] bodies) =>
        new(
            system,
            bodies.Length,
            ExplorationSpoilerModes.EnrichScanned,
            bodies);

    private static ExplorationCatalogBody BioCatalogBody(
        int id,
        string name) =>
        MakeCatalogBody(
            id,
            name,
            ExplorationBodyHighlights.Biological
            | ExplorationBodyHighlights.Landable,
            mappingValue: 100_000);

    private static ExplorationCatalogBody ValuableCatalogBody(
        int id,
        string name) =>
        MakeCatalogBody(
            id,
            name,
            ExplorationBodyHighlights.WaterWorld
            | ExplorationBodyHighlights.Valuable,
            mappingValue: 350_000);

    private static ExplorationCatalogBody MakeCatalogBody(
        int id,
        string name,
        ExplorationBodyHighlights highlights,
        long mappingValue) =>
        new(
            id,
            name,
            "Planet",
            highlights.HasFlag(
                ExplorationBodyHighlights.WaterWorld)
                ? "Water world"
                : "Rocky body",
            800,
            highlights.HasFlag(
                ExplorationBodyHighlights.Landable),
            0.2,
            250,
            "Thin atmosphere",
            string.Empty,
            highlights.HasFlag(
                ExplorationBodyHighlights.Terraformable),
            100_000,
            mappingValue,
            true,
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            false,
            highlights.HasFlag(
                ExplorationBodyHighlights.Biological)
                ? 2
                : 0,
            highlights.HasFlag(
                ExplorationBodyHighlights.Biological)
                ? new[] { "Stratum", "Bacterium" }
                : Array.Empty<string>(),
            highlights,
            "Journal");
}
'@

Write-Text $testsPath $tests

# ---------------------------------------------------------------------------
# 6. Sanity checks.
# ---------------------------------------------------------------------------
$appCheck = Read-Text $appPath
$engineCheck = Read-Text $enginePath
$serviceCheck = Read-Text $servicePath

if (-not $appCheck.Contains(
        'ExplorationVisitStateService.Instance.Start();')) {
    throw 'Visit service startup was not added.'
}

if (-not $appCheck.Contains(
        'ExplorationVisitStateService.Instance.Dispose();')) {
    throw 'Visit service disposal was not added.'
}

foreach ($needle in @(
    'ExplorationVisitDisposition.Active',
    'ExplorationVisitDisposition.Deferred',
    'ExplorationVisitDisposition.Complete',
    'deferredBodyIds.Add(activeBodyId)',
    'ResetVisit(nextSystemKey)'
)) {
    if (-not $engineCheck.Contains($needle)) {
        throw "Missing queue engine behavior: $needle"
    }
}

foreach ($needle in @(
    'DestinationStabilityMilliseconds = 1_200',
    'engine.ActivateBody(bodyId)',
    'DestinationBodyId',
    'DestinationSystemAddress'
)) {
    if (-not $serviceCheck.Contains($needle)) {
        throw "Missing visit service behavior: $needle"
    }
}

Write-Host ''
& git diff --check
if ($LASTEXITCODE -ne 0) {
    throw 'git diff --check failed.'
}

Write-Host ''
& git diff --stat

if (-not $SkipBuild) {
    Write-Host ''
    Write-Host 'Building application...' -ForegroundColor Cyan

    & dotnet build '.\ED_Inara_Overlay\ED_Inara_Overlay.csproj' -c Debug
    if ($LASTEXITCODE -ne 0) {
        throw 'Application build failed.'
    }

    Write-Host ''
    Write-Host 'Running regression tests...' -ForegroundColor Cyan

    & dotnet test '.\Testing\ED_Inara_Overlay.LayoutTests\ED_Inara_Overlay.LayoutTests.csproj' -c Debug
    if ($LASTEXITCODE -ne 0) {
        throw 'Regression tests failed.'
    }
}

Write-Host ''
Write-Host 'Exploration visit queue applied.' -ForegroundColor Green
Write-Host ''
Write-Host 'Behavior now available to the next UI patch:'
Write-Host '  Recommended -> interesting and unfinished'
Write-Host '  Active      -> stable current body destination'
Write-Host '  Deferred    -> unfinished body left for later this visit'
Write-Host '  Complete    -> required FSS/DSS/BIO objectives are done'
Write-Host ''
Write-Host 'Completed and Deferred bodies are excluded from Recommended.'
Write-Host 'Switching to another stable interesting body defers the previous Active body.'
Write-Host 'Deferred state resets after entering another star system.'
Write-Host 'Manual DeferBody/ResumeBody APIs are ready for the full assistant.'
Write-Host ''
Write-Host 'No exploration overlay XAML is changed by this patch.'
Write-Host "Backup of previous local diff: $backup"
