namespace EDActivityOverlay.Models;

public sealed record BodyOrganicProgressStatus(
    string Genus,
    string Species,
    string Variant,
    int Stage,
    bool Completed,
    int ColonyRangeMeters,
    bool SeenThisSession,
    DateTimeOffset? UpdatedUtc)
{
    public string GenusKey { get; init; } = string.Empty;
    public string SpeciesKey { get; init; } = string.Empty;
    public string VariantKey { get; init; } = string.Empty;
}

public sealed record BodyExplorationProgress(
    int BodyId,
    string BodyName,
    bool FssScanned,
    bool DssMapped,
    bool DssEfficient,
    int BiologicalSignals,
    int CompletedBiologicalSignals,
    IReadOnlyList<string> KnownGenuses,
    IReadOnlyList<string> MissingGenuses,
    IReadOnlyList<BodyOrganicProgressStatus> Organics,
    bool HistoricalBiologyDetailIncomplete)
{
    public IReadOnlyList<string> KnownGenusKeys { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> MissingGenusKeys { get; init; } =
        Array.Empty<string>();

    public int RemainingBiologicalSignals =>
        Math.Max(
            0,
            BiologicalSignals - CompletedBiologicalSignals);

    public bool HasBiology => BiologicalSignals > 0;

    public bool BiologyComplete =>
        !HasBiology || RemainingBiologicalSignals == 0;

    public bool IsKnown =>
        FssScanned
        || DssMapped
        || BiologicalSignals > 0
        || Organics.Count > 0;
}