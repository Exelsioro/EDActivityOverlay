namespace EDActivityOverlay.Models;

public enum MiningModuleKind
{
    Unknown,
    MiningLaser,
    ProspectorController,
    CollectorController,
    MiningMultiLimpetController,
    Refinery,
    DetailedSurfaceScanner,
    PulseWaveAnalyzer,
    SeismicChargeLauncher,
    AbrasionBlaster,
    SubsurfaceDisplacementMissile
}

public enum MiningLoadoutMode
{
    Laser,
    Core,
    Subsurface,
    Surface
}

public enum MiningReadinessLevel
{
    Unknown,
    MissingRequired,
    Usable,
    FullKit
}

public enum MiningLoadoutAdvisory
{
    MissingProspector,
    ProspectorBelowA,
    MissingCollector,
    MissingDetailedSurfaceScanner,
    MissingPulseWaveAnalyzer
}

public sealed record MiningLoadoutModuleSnapshot(
    string Slot,
    string Item,
    MiningModuleKind Kind,
    int Size,
    string Rating,
    bool Enabled);

public sealed record MiningModeReadiness(
    MiningLoadoutMode Mode,
    MiningReadinessLevel Level,
    IReadOnlyList<MiningModuleKind> MissingRequired,
    IReadOnlyList<MiningLoadoutAdvisory> Advisories)
{
    public bool IsUsable =>
        Level is MiningReadinessLevel.Usable
            or MiningReadinessLevel.FullKit;

    public static MiningModeReadiness Unknown(MiningLoadoutMode mode) =>
        new(
            mode,
            MiningReadinessLevel.Unknown,
            Array.Empty<MiningModuleKind>(),
            Array.Empty<MiningLoadoutAdvisory>());
}

public sealed record MiningLoadoutSnapshot(
    bool Available,
    string Ship,
    IReadOnlyList<MiningLoadoutModuleSnapshot> Modules,
    bool HasProspector,
    string BestProspectorRating,
    bool HasAProspector,
    bool HasCollector,
    bool HasDetailedSurfaceScanner,
    bool HasPulseWaveAnalyzer,
    MiningModeReadiness Laser,
    MiningModeReadiness Core,
    MiningModeReadiness Subsurface,
    MiningModeReadiness Surface)
{
    public static MiningLoadoutSnapshot Empty { get; } =
        new(
            false,
            string.Empty,
            Array.Empty<MiningLoadoutModuleSnapshot>(),
            false,
            string.Empty,
            false,
            false,
            false,
            false,
            MiningModeReadiness.Unknown(MiningLoadoutMode.Laser),
            MiningModeReadiness.Unknown(MiningLoadoutMode.Core),
            MiningModeReadiness.Unknown(MiningLoadoutMode.Subsurface),
            MiningModeReadiness.Unknown(MiningLoadoutMode.Surface));

    public MiningModeReadiness ForMode(MiningLoadoutMode mode) =>
        mode switch
        {
            MiningLoadoutMode.Core => Core,
            MiningLoadoutMode.Subsurface => Subsurface,
            MiningLoadoutMode.Surface => Surface,
            _ => Laser
        };
}

public sealed class MiningLoadoutChangedEventArgs(
    MiningLoadoutSnapshot current) : EventArgs
{
    public MiningLoadoutSnapshot Current { get; } = current;
}
