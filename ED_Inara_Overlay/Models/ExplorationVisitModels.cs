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