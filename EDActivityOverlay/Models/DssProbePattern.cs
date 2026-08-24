namespace EDActivityOverlay.Models;

public enum DssAimZone
{
    Disc,
    Limb,
    FarSide
}

public sealed record DssAimPoint(
    int Sequence,
    double X,
    double Y,
    DssAimZone Zone);

public sealed record DssProbePattern(
    int EfficiencyTarget,
    IReadOnlyList<DssAimPoint> Points,
    string StrategyKey,
    string AdjustmentKey)
{
    public static DssProbePattern Empty { get; } = new(
        0, Array.Empty<DssAimPoint>(), string.Empty, string.Empty);
}
