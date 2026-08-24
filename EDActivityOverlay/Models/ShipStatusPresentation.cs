namespace EDActivityOverlay.Models;

public enum ShipStatusAdvisoryKind
{
    None,
    FuelCaution,
    FuelCritical,
    NoScoopableStars,
    HazardousNextStar
}

public sealed record ShipStatusPresentation(
    string CurrentSystem,
    string NextSystem,
    string NextStarClass,
    int RemainingJumps,
    bool NextStarScoopable,
    double FuelPercent,
    ShipStatusAdvisoryKind Advisory);
