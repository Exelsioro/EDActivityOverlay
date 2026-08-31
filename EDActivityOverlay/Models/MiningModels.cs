namespace EDActivityOverlay.Models;

public enum MiningSessionState
{
    Idle,
    Active,
    Finished
}

public enum MiningSessionEndReason
{
    None,
    SupercruiseEntry,
    Jump,
    CarrierJump,
    Docked,
    Died,
    Shutdown,
    LoadGame,
    SystemChanged
}

public enum MiningProspectDecision
{
    NoTarget,
    Mine,
    Skip,
    Core
}

public enum MiningExtractionMethod
{
    Unknown,
    Laser,
    Core
}

public sealed record MiningProspectAdvice(
    MiningProspectDecision Decision,
    MiningExtractionMethod RecommendedMethod,
    MiningExtractionMethod TargetMethod,
    string TargetCommodity,
    string MatchedDisplayName,
    double? TargetProportion,
    bool TargetFound,
    bool MotherlodeMatches);

public sealed record MiningTargetStatistics(
    int Prospected,
    int TargetBearing,
    int Accepted,
    double HitRate,
    double AcceptanceRate,
    double AverageProportion,
    double MedianProportion,
    double BestProportion);

public sealed record MiningProspectMaterialSnapshot(
    string CommodityId,
    string DisplayName,
    double Proportion);

public sealed record MiningProspectSnapshot(
    int Sequence,
    DateTimeOffset Timestamp,
    string Content,
    double Remaining,
    string MotherlodeCommodityId,
    string MotherlodeDisplayName,
    IReadOnlyList<MiningProspectMaterialSnapshot> Materials)
{
    public bool HasMotherlode =>
        !string.IsNullOrWhiteSpace(MotherlodeCommodityId)
        || !string.IsNullOrWhiteSpace(MotherlodeDisplayName);
}

public sealed record MiningRefinementSnapshot(
    int Sequence,
    DateTimeOffset Timestamp,
    string CommodityId,
    string DisplayName);

public sealed record MiningSessionSnapshot(
    Guid SessionId,
    MiningSessionState State,
    DateTimeOffset StartedUtc,
    DateTimeOffset LastActivityUtc,
    DateTimeOffset? EndedUtc,
    MiningSessionEndReason EndReason,
    string Commander,
    long SystemAddress,
    string SystemName,
    int BodyId,
    string BodyName,
    string RingName,
    int ProspectorsLaunched,
    int CollectorsLaunched,
    int CrackedAsteroids,
    int CargoUsed,
    int CargoCapacity,
    int LimpetsRemaining,
    IReadOnlyList<MiningProspectSnapshot> Prospects,
    IReadOnlyList<MiningRefinementSnapshot> Refinements)
{
    public static MiningSessionSnapshot Empty { get; } = new(
        Guid.Empty,
        MiningSessionState.Idle,
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue,
        null,
        MiningSessionEndReason.None,
        string.Empty,
        0,
        string.Empty,
        -1,
        string.Empty,
        string.Empty,
        0,
        0,
        0,
        0,
        0,
        0,
        Array.Empty<MiningProspectSnapshot>(),
        Array.Empty<MiningRefinementSnapshot>());

    public bool IsActive => State == MiningSessionState.Active;
    public int ProspectedAsteroids => Prospects.Count;
    public int RefinedTons => Refinements.Count;
    public bool HasMiningEvidence =>
        ProspectedAsteroids > 0
        || RefinedTons > 0
        || CrackedAsteroids > 0;

    public TimeSpan Duration =>
        State == MiningSessionState.Idle
            ? TimeSpan.Zero
            : (EndedUtc ?? LastActivityUtc) - StartedUtc;

    public IReadOnlyDictionary<string, int> RefinedByCommodity =>
        Refinements
            .Where(item => !string.IsNullOrWhiteSpace(item.CommodityId))
            .GroupBy(item => item.CommodityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
}

public sealed class MiningSessionChangedEventArgs(
    MiningSessionSnapshot current,
    MiningSessionSnapshot? completedSession = null) : EventArgs
{
    public MiningSessionSnapshot Current { get; } = current;
    public MiningSessionSnapshot? CompletedSession { get; } = completedSession;
}
