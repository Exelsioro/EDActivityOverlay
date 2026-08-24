using System.Collections.ObjectModel;

namespace EDActivityOverlay.Models;

public sealed record NavRouteStar(
    string System,
    string StarClass,
    double? X = null,
    double? Y = null,
    double? Z = null)
{
    public bool IsScoopable => StarClass is "O" or "B" or "A" or "F" or "G" or "K" or "M";
    public bool IsNeutron => StarClass.Equals("N", StringComparison.OrdinalIgnoreCase);
    public bool IsWhiteDwarf => StarClass.StartsWith("D", StringComparison.OrdinalIgnoreCase);

    public double? DistanceTo(NavRouteStar other) =>
        X is null || Y is null || Z is null || other.X is null || other.Y is null || other.Z is null
            ? null
            : Math.Sqrt(Math.Pow(other.X.Value - X.Value, 2)
                        + Math.Pow(other.Y.Value - Y.Value, 2)
                        + Math.Pow(other.Z.Value - Z.Value, 2));
}

public sealed record MarketItemSnapshot(
    string Name,
    int BuyPrice,
    int SellPrice,
    int Supply,
    int Demand);

public sealed record ProspectedMaterialSnapshot(string Name, double Proportion);

public sealed record ProspectedAsteroidSnapshot(
    string Content,
    double Remaining,
    string MotherlodeMaterial,
    IReadOnlyList<ProspectedMaterialSnapshot> Materials)
{
    public bool HasMotherlode => !string.IsNullOrWhiteSpace(MotherlodeMaterial);
}

public enum ExplorationInterest
{
    None,
    Terraformable,
    EarthLike,
    WaterWorld,
    AmmoniaWorld,
    NeutronStar,
    BlackHole
}

public sealed record ExplorationBodySnapshot(
    int BodyId,
    string Name,
    string Description,
    double DistanceFromArrivalLs,
    bool WasDiscovered,
    bool WasMapped,
    bool IsMapped,
    bool MappingEfficient,
    int BiologicalSignals,
    IReadOnlyList<string> Genuses,
    ExplorationInterest Interest)
{
    public bool IsScanned { get; init; }
    public bool IsNotable => Interest != ExplorationInterest.None;
    public bool Landable { get; init; }
    public double GravityG { get; init; }
    public double SurfaceTemperatureKelvin { get; init; }
    public double SurfacePressureAtmospheres { get; init; }
    public double RadiusMeters { get; init; }
    public string Atmosphere { get; init; } = string.Empty;
    public string Volcanism { get; init; } = string.Empty;
    public string BodyType { get; init; } = string.Empty;
    public string BodyClass { get; init; } = string.Empty;
    public bool Terraformable { get; init; }
    public double EarthMasses { get; init; }
    public double SolarMasses { get; init; }
    public long EstimatedScanValue { get; init; }
    public long EstimatedMappingValue { get; init; }
    public long EstimatedEfficientMappingValue { get; init; }
    public IReadOnlyList<BiologyEstimateSnapshot> BiologyEstimates { get; init; } =
        Array.Empty<BiologyEstimateSnapshot>();
    public IReadOnlyList<string> GenusKeys { get; init; } =
        Array.Empty<string>();
    public int LastProbesUsed { get; init; }
    public int EfficiencyTarget { get; init; }

    public long MinimumBiologyValue => BiologyEstimates.Sum(item => item.MinimumValue);
    public long MaximumBiologyValue => BiologyEstimates.Sum(item => item.MaximumValue);
    public bool HasMappingResult => LastProbesUsed > 0 && EfficiencyTarget > 0;
}

public sealed record BiologyEstimateSnapshot(
    string Genus,
    string CatalogKey,
    int ColonyRangeMeters,
    long MinimumValue,
    long MaximumValue);

public sealed record OrganicScanProgressSnapshot(
    string Commander,
    long SystemAddress,
    string SystemName,
    int BodyId,
    string BodyName,
    string Genus,
    string Species,
    string Variant,
    int Stage,
    bool Completed,
    int ColonyRangeMeters,
    double? LastSampleLatitude,
    double? LastSampleLongitude,
    DateTimeOffset UpdatedUtc)
{
    public string GenusKey { get; init; } = string.Empty;
    public string SpeciesKey { get; init; } = string.Empty;
    public string VariantKey { get; init; } = string.Empty;
}

public sealed record GameStateSnapshot
{
    /// <summary>Selected Elite UI screen from Status.json; Galaxy Map is 6.</summary>
    public int GuiFocus { get; init; }
    public static GameStateSnapshot Empty { get; } = new();

    public bool JournalAvailable { get; init; }
    public string JournalDirectory { get; init; } = string.Empty;
    public DateTimeOffset? LastEventUtc { get; init; }
    public string Commander { get; init; } = string.Empty;
    public string Ship { get; init; } = string.Empty;
    public string ShipName { get; init; } = string.Empty;
    public string StarSystem { get; init; } = string.Empty;
    public long SystemAddress { get; init; }
    public double? SystemX { get; init; }
    public double? SystemY { get; init; }
    public double? SystemZ { get; init; }
    public string Station { get; init; } = string.Empty;
    public long? MarketId { get; init; }
    public int CargoCapacity { get; init; }
    public int CargoUsed { get; init; }
    public long Balance { get; init; }
    public bool Docked { get; init; }
    public bool LandingGearDown { get; init; }
    public bool ShieldsUp { get; init; }
    public bool InSupercruise { get; init; }
    public bool FsdCharging { get; init; }
    public bool FsdMassLocked { get; init; }
    public bool FsdCooldown { get; init; }
    public bool HardpointsDeployed { get; init; }
    public bool LightsOn { get; init; }
    public bool CargoScoopDeployed { get; init; }
    public bool SilentRunning { get; init; }
    public bool FuelScooping { get; init; }
    public bool OverHeating { get; init; }
    public bool NightVision { get; init; }
    public bool IsInDanger { get; init; }
    public bool LowFuel { get; init; }
    public bool Landed { get; init; }
    public bool InSrv { get; init; }
    public bool OnFoot { get; init; }
    public bool OnFootOnPlanet { get; init; }
    public bool GlideMode { get; init; }
    public bool HasSurfacePosition { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? AltitudeMeters { get; init; }
    public double? HeadingDegrees { get; init; }
    public double? PlanetRadiusMeters { get; init; }
    public double? SurfaceGravityG { get; init; }
    public double? Oxygen { get; init; }
    public double? Health { get; init; }
    public double? TemperatureKelvin { get; init; }
    public string CurrentBody { get; init; } = string.Empty;
    public string LegalState { get; init; } = string.Empty;
    // Backwards-compatible destination label used by existing UI.
    public string Destination { get; init; } = string.Empty;

    // Full Status.json Destination identity. Body is -1 for a system/station
    // target or when Elite does not expose a body destination.
    public long DestinationSystemAddress { get; init; }
    public int DestinationBodyId { get; init; } = -1;
    public string DestinationName { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, int> Cargo { get; init; } =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
    public IReadOnlyDictionary<string, MarketItemSnapshot> Market { get; init; } =
        new ReadOnlyDictionary<string, MarketItemSnapshot>(new Dictionary<string, MarketItemSnapshot>());
    public string MarketSystem { get; init; } = string.Empty;
    public string MarketStation { get; init; } = string.Empty;
    public DateTimeOffset? MarketUpdatedUtc { get; init; }
    public double FuelMain { get; init; }
    public double FuelReservoir { get; init; }
    public double FuelCapacityMain { get; init; }
    public double FuelCapacityReservoir { get; init; }
    public double LastJumpFuelUsed { get; init; }
    public double LastJumpDistanceLy { get; init; }
    public double FuelPerLightYearEstimate { get; init; }
    public double MaxJumpRangeLy { get; init; }
    public IReadOnlyList<NavRouteStar> NavRoute { get; init; } = Array.Empty<NavRouteStar>();
    public int SystemBodyCount { get; init; }
    public double FssProgress { get; init; }
    public int NonBodySignals { get; init; }
    public int ScannedBodies { get; init; }
    public int MappedBodies { get; init; }
    public int EfficientMappings { get; init; }
    public int BiologicalSignals { get; init; }
    public int BiologicalBodies { get; init; }
    public string LastOrganicSpecies { get; init; } = string.Empty;
    public string LastOrganicGenus { get; init; } = string.Empty;
    public string LastOrganicVariant { get; init; } = string.Empty;
    public string LastOrganicScanType { get; init; } = string.Empty;
    public int LastOrganicBodyId { get; init; } = -1;
    public int OrganicSampleStage { get; init; }
    public int CompletedOrganicSamples { get; init; }
    public int NewCodexEntries { get; init; }
    public IReadOnlyList<ExplorationBodySnapshot> ExplorationBodies { get; init; } =
        Array.Empty<ExplorationBodySnapshot>();
    public IReadOnlyList<OrganicScanProgressSnapshot> OrganicProgress { get; init; } =
        Array.Empty<OrganicScanProgressSnapshot>();
    public ProspectedAsteroidSnapshot? LastProspectedAsteroid { get; init; }
    public int CrackedAsteroids { get; init; }
    public IReadOnlyDictionary<string, int> RefinedMiningCargo { get; init; } =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());

    public int RefinedMiningUnits => RefinedMiningCargo.Values.Sum();

    public int FreeCargo => CargoCapacity > 0 ? Math.Max(0, CargoCapacity - CargoUsed) : 0;

    public IReadOnlyList<OrganicScanProgressSnapshot> GetOrganicProgressForBody(int bodyId) =>
        bodyId < 0
            ? Array.Empty<OrganicScanProgressSnapshot>()
            : OrganicProgress
                .Where(item => item.BodyId == bodyId)
                .OrderByDescending(item => item.UpdatedUtc)
                .ToArray();

    public OrganicScanProgressSnapshot? GetActiveOrganicForBody(int bodyId) =>
        GetOrganicProgressForBody(bodyId)
            .Where(item => !item.Completed)
            .OrderByDescending(item => item.UpdatedUtc)
            .FirstOrDefault();

    public int GetCompletedBiologicalSignalsForBody(int bodyId) =>
        GetOrganicProgressForBody(bodyId)
            .Where(item => item.Completed)
            .Select(item =>
                !string.IsNullOrWhiteSpace(item.GenusKey)
                    ? item.GenusKey
                    : !string.IsNullOrWhiteSpace(item.Genus)
                        ? item.Genus
                        : !string.IsNullOrWhiteSpace(item.SpeciesKey)
                            ? item.SpeciesKey
                            : item.Species)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    public int GetRemainingBiologicalSignalsForBody(int bodyId)
    {
        ExplorationBodySnapshot? body = ExplorationBodies.FirstOrDefault(item => item.BodyId == bodyId);
        return body is null
            ? 0
            : Math.Max(0, body.BiologicalSignals - GetCompletedBiologicalSignalsForBody(bodyId));
    }

    // Compatibility property for the compact surface view. Prefer the current
    // navigation body; fall back to the body of the latest organic event.
    public OrganicScanProgressSnapshot? ActiveOrganic
    {
        get
        {
            int bodyId = DestinationBodyId >= 0 ? DestinationBodyId : LastOrganicBodyId;
            return GetActiveOrganicForBody(bodyId);
        }
    }

    // Compatibility property. New code should use the per-body method.
    public int RemainingBiologicalSignals
    {
        get
        {
            int bodyId = DestinationBodyId >= 0 ? DestinationBodyId : LastOrganicBodyId;
            if (bodyId >= 0) return GetRemainingBiologicalSignalsForBody(bodyId);

            int completed = OrganicProgress
                .Where(item => item.Completed)
                .Select(item => $"{item.BodyId}|{(!string.IsNullOrWhiteSpace(item.Genus) ? item.Genus : item.Species)}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return Math.Max(0, BiologicalSignals - completed);
        }
    }

    public bool IsLive => JournalAvailable
        && LastEventUtc is { } timestamp
        && DateTimeOffset.UtcNow - timestamp < TimeSpan.FromMinutes(10);
}

public sealed class GameStateChangedEventArgs(GameStateSnapshot state) : EventArgs
{
    public GameStateSnapshot State { get; } = state;
}

public sealed class JournalEventReceivedEventArgs(
    string eventName,
    DateTimeOffset timestamp,
    System.Text.Json.JsonElement data) : EventArgs
{
    public string EventName { get; } = eventName;
    public DateTimeOffset Timestamp { get; } = timestamp;
    public System.Text.Json.JsonElement Data { get; } = data;
}
