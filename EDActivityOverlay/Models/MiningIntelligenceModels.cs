namespace EDActivityOverlay.Models;

public enum MiningHudPhase
{
    Idle,
    Searching,
    ProspectDecision,
    Mining,
    NearFull,
    Full
}

public enum MiningFieldQuality
{
    Unknown,
    Good,
    Stable,
    Declining
}

public enum MiningLeaveRecommendation
{
    Unknown,
    Continue,
    FinishCurrentRock,
    LeaveNow,
    CargoFull
}

public sealed record MiningCollectorActivitySnapshot(
    bool Available,
    int Capacity,
    int EstimatedActive,
    int TopUpRecommended,
    TimeSpan AssumedLifetime)
{
    public static MiningCollectorActivitySnapshot Empty { get; } =
        new(false, 0, 0, 0, TimeSpan.Zero);
}

public sealed record MiningLimpetAdvice(
    bool Ready,
    int Remaining,
    double UsagePerRefinedTon,
    int EstimatedRequired,
    int SafeExcess,
    bool Low,
    bool Critical);

public sealed record MiningAdaptiveThresholdAdvice(
    bool Ready,
    double Baseline,
    double Suggested,
    double RecentHitRate,
    double RecentMedian);

public sealed record MiningLeaveAdvice(
    MiningLeaveRecommendation Recommendation,
    int FreeCargo,
    int EffectiveMineralRoom,
    TimeSpan? EstimatedTimeToFull);

public sealed record MiningIntelligenceSnapshot(
    MiningHudPhase Phase,
    MiningFieldQuality FieldQuality,
    MiningLimpetAdvice Limpets,
    MiningCollectorActivitySnapshot Collectors,
    MiningAdaptiveThresholdAdvice AdaptiveThreshold,
    MiningLeaveAdvice Leave)
{
    public static MiningIntelligenceSnapshot Empty { get; } =
        new(
            MiningHudPhase.Idle,
            MiningFieldQuality.Unknown,
            new MiningLimpetAdvice(false, 0, 0, 0, 0, false, false),
            MiningCollectorActivitySnapshot.Empty,
            new MiningAdaptiveThresholdAdvice(false, 0, 0, 0, 0),
            new MiningLeaveAdvice(
                MiningLeaveRecommendation.Unknown,
                0,
                0,
                null));
}

public sealed class MiningCollectorActivityChangedEventArgs(
    MiningCollectorActivitySnapshot current) : EventArgs
{
    public MiningCollectorActivitySnapshot Current { get; } = current;
}
