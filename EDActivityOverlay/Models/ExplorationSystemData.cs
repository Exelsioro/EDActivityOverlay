namespace EDActivityOverlay.Models;

public sealed record ExternalExplorationBodySnapshot(
    int BodyId,
    string Name,
    string Type,
    string Subtype,
    double DistanceFromArrivalLs,
    bool Landable,
    double GravityG,
    double SurfaceTemperatureKelvin,
    string Atmosphere,
    string Volcanism,
    string TerraformingState,
    long EstimatedScanValue,
    long EstimatedMappingValue,
    int LandmarkCount)
{
    public double EarthMasses { get; init; }
    public double SolarMasses { get; init; }
    public double SurfacePressureAtmospheres { get; init; }
    public bool ValuesCalculatedLocally { get; init; }
}

public sealed record ExplorationSystemDataSnapshot(
    long SystemAddress,
    string SystemName,
    string Source,
    DateTimeOffset SourceUpdatedUtc,
    DateTimeOffset FetchedUtc,
    bool FromCache,
    bool IsStale,
    int BodyCount,
    long EstimatedScanValue,
    long EstimatedMappingValue,
    double? X,
    double? Y,
    double? Z,
    bool NeedsPermit,
    IReadOnlyList<ExternalExplorationBodySnapshot> Bodies)
{
    public static ExplorationSystemDataSnapshot Empty { get; } = new(
        0, string.Empty, string.Empty, DateTimeOffset.MinValue, DateTimeOffset.MinValue,
        false, false, 0, 0, 0, null, null, null, false,
        Array.Empty<ExternalExplorationBodySnapshot>());
}

public enum ExplorationDataStatus
{
    Disabled,
    Idle,
    Loading,
    Available,
    Unavailable
}

public sealed record ExplorationDataState(
    ExplorationDataStatus Status,
    ExplorationSystemDataSnapshot? System,
    string Error)
{
    public static ExplorationDataState Idle { get; } = new(ExplorationDataStatus.Idle, null, string.Empty);
}

public sealed class ExplorationDataChangedEventArgs(ExplorationDataState state) : EventArgs
{
    public ExplorationDataState State { get; } = state;
}
