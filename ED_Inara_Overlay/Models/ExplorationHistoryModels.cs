namespace ED_Inara_Overlay.Models;

public sealed record ExplorationHistoryGenusSnapshot(
    string GenusKey,
    string GenusName,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

public sealed record ExplorationHistoryOrganicSnapshot(
    string GenusKey,
    string GenusName,
    string SpeciesKey,
    string SpeciesName,
    string VariantKey,
    string VariantName,
    bool Completed,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

public sealed record ExplorationHistoryBodySnapshot(
    int BodyId,
    string BodyName,
    string BodyClass,
    bool Scanned,
    bool Mapped,
    bool EfficientlyMapped,
    bool FirstDiscovered,
    bool FirstMapped,
    int BiologicalSignals,
    int CompletedOrganics,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc)
{
    public IReadOnlyList<ExplorationHistoryGenusSnapshot> Genuses { get; init; } =
        Array.Empty<ExplorationHistoryGenusSnapshot>();
    public IReadOnlyList<ExplorationHistoryOrganicSnapshot> Organics { get; init; } =
        Array.Empty<ExplorationHistoryOrganicSnapshot>();
}

public sealed record ExplorationSystemHistorySnapshot(
    string Commander,
    long SystemAddress,
    string SystemName,
    DateTimeOffset? FirstVisitedUtc,
    DateTimeOffset? LastVisitedUtc,
    IReadOnlyList<ExplorationHistoryBodySnapshot> Bodies)
{
    public static ExplorationSystemHistorySnapshot Empty { get; } = new(
        string.Empty, 0, string.Empty, null, null,
        Array.Empty<ExplorationHistoryBodySnapshot>());
    public bool WasVisited => FirstVisitedUtc is not null;
}

public sealed record ExplorationHistoryImportState(
    bool IsRunning,
    int ProcessedFiles,
    int TotalFiles,
    long ProcessedLines,
    string CurrentFile,
    string Error)
{
    public static ExplorationHistoryImportState Idle { get; } = new(false, 0, 0, 0, string.Empty, string.Empty);
}

public sealed class ExplorationHistoryChangedEventArgs(
    ExplorationHistoryImportState importState) : EventArgs
{
    public ExplorationHistoryImportState ImportState { get; } = importState;
}