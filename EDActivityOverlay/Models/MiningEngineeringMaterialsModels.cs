namespace EDActivityOverlay.Models;

public sealed record MiningMaterialSessionGain(
    string MaterialId,
    string DisplayName,
    int Count);

public sealed record MiningEngineeringMaterialProgress(
    string MaterialId,
    string DisplayName,
    int GainedThisSession,
    int Available,
    int Required,
    int Missing)
{
    public bool IsEngineeringTarget => Required > 0;
    public bool IsComplete => IsEngineeringTarget && Missing == 0;
}

public sealed record MiningEngineeringMaterialsSnapshot(
    Guid SessionId,
    IReadOnlyList<MiningEngineeringMaterialProgress> Materials,
    int TotalGained,
    int TargetMaterialsGained)
{
    public static MiningEngineeringMaterialsSnapshot Empty { get; } =
        new(
            Guid.Empty,
            Array.Empty<MiningEngineeringMaterialProgress>(),
            0,
            0);

    public bool HasGains => TotalGained > 0;
}

public sealed class MiningEngineeringMaterialsChangedEventArgs(
    MiningEngineeringMaterialsSnapshot current) : EventArgs
{
    public MiningEngineeringMaterialsSnapshot Current { get; } = current;
}
