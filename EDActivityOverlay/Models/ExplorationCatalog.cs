namespace EDActivityOverlay.Models;

public static class ExplorationSpoilerModes
{
    public const string JournalOnly = "JournalOnly";
    public const string EnrichScanned = "EnrichScanned";
    public const string FullCatalog = "FullCatalog";

    public static string Normalize(string? value) => value switch
    {
        JournalOnly => JournalOnly,
        FullCatalog => FullCatalog,
        _ => EnrichScanned
    };
}

/// <summary>
/// Journal-backed discovery state. A false value is a candidate for a first
/// discovery/mapping bonus; an unknown value must never be treated as false.
/// </summary>
public static class ExplorationDiscoveryStatus
{
    public static bool IsFirstDiscoveryCandidate(ExplorationCatalogBody body) =>
        body.DiscoveryStatusKnown && !body.WasDiscovered;

    public static bool IsFirstMappingCandidate(ExplorationCatalogBody body) =>
        body.MappingStatusKnown && !body.WasMapped;
}

[Flags]
public enum ExplorationBodyHighlights
{
    None = 0,
    Valuable = 1,
    Biological = 2,
    Terraformable = 4,
    EarthLike = 8,
    WaterWorld = 16,
    AmmoniaWorld = 32,
    NeutronStar = 64,
    BlackHole = 128,
    Landable = 256
}

public sealed record ExplorationCatalogBody(
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
    bool Terraformable,
    long EstimatedScanValue,
    long EstimatedMappingValue,
    bool ScannedThisVisit,
    bool MappedThisVisit,
    bool EfficientlyMappedThisVisit,
    bool ScannedPreviously,
    bool MappedPreviously,
    bool EfficientlyMappedPreviously,
    int CompletedOrganics,
    bool WasDiscovered,
    bool WasMapped,
    int BiologicalSignals,
    IReadOnlyList<string> Genuses,
    ExplorationBodyHighlights Highlights,
    string Source)
{
    public bool DiscoveryStatusKnown { get; init; }
    public bool MappingStatusKnown { get; init; }
    public double SurfacePressureAtmospheres { get; init; }
    public int LastProbesUsed { get; init; }
    public int EfficiencyTarget { get; init; }
    public bool IsValuable => Highlights.HasFlag(ExplorationBodyHighlights.Valuable);
    public bool IsBiological => Highlights.HasFlag(ExplorationBodyHighlights.Biological);
    public bool IsNotable => (Highlights & ~ExplorationBodyHighlights.Landable) != ExplorationBodyHighlights.None;
}

public sealed record ExplorationSystemCatalog(
    string SystemName,
    int KnownBodyCount,
    string SpoilerMode,
    IReadOnlyList<ExplorationCatalogBody> Bodies)
{
    public static ExplorationSystemCatalog Empty { get; } = new(
        string.Empty, 0, ExplorationSpoilerModes.EnrichScanned,
        Array.Empty<ExplorationCatalogBody>());
}
