namespace ED_Inara_Overlay.Models;

public enum FuelRouteSeverity
{
    Unknown,
    Safe,
    Caution,
    Critical
}

public sealed record FuelRouteAssessment(
    FuelRouteSeverity Severity,
    double FuelPercent,
    int RemainingJumps,
    int? JumpsToNextScoopable,
    string NextScoopableSystem,
    double? EstimatedFuelToNextScoopable,
    double EmergencyReserve,
    bool IsEstimate);

