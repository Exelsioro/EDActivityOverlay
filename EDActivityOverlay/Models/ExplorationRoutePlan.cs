namespace EDActivityOverlay.Models;

public sealed record ExplorationRouteBody(string Name, long ScanValue, long MappingValue);

public sealed record ExplorationRouteStop(
    string System,
    IReadOnlyList<ExplorationRouteBody> Bodies,
    double DistanceLy,
    bool Neutron,
    bool Refuel,
    bool Inject)
{
    public long EstimatedValue => Bodies.Sum(body => body.ScanValue + body.MappingValue);
}

public sealed record ExplorationRoutePlan(
    string SourceFile,
    string Kind,
    DateTimeOffset ImportedUtc,
    int CurrentIndex,
    IReadOnlyList<ExplorationRouteStop> Stops)
{
    public static ExplorationRoutePlan Empty { get; } = new(
        string.Empty, string.Empty, DateTimeOffset.MinValue, 0, Array.Empty<ExplorationRouteStop>());

    public ExplorationRouteStop? CurrentStop => Stops.Count == 0
        ? null : Stops[Math.Clamp(CurrentIndex, 0, Stops.Count - 1)];
    public ExplorationRouteStop? NextStop => CurrentIndex + 1 < Stops.Count ? Stops[CurrentIndex + 1] : null;
}

public sealed class ExplorationRouteChangedEventArgs(ExplorationRoutePlan plan) : EventArgs
{
    public ExplorationRoutePlan Plan { get; } = plan;
}

public sealed record SpanshRoadToRichesRequest(
    string Source,
    string Destination,
    double JumpRange,
    int Radius,
    int MaximumSystems,
    int MaximumDistance,
    long MinimumValue,
    bool UseMappingValue,
    bool Loop,
    bool AvoidThargoids);

public enum SpanshRouteCalculationStatus { Idle, Validating, Calculating, Completed, Failed }

public sealed record SpanshRouteCalculationState(
    SpanshRouteCalculationStatus Status,
    string Message,
    ExplorationRoutePlan? Preview)
{
    public static SpanshRouteCalculationState Idle { get; } = new(SpanshRouteCalculationStatus.Idle, string.Empty, null);
}

public sealed class SpanshRouteCalculationChangedEventArgs(SpanshRouteCalculationState state) : EventArgs
{
    public SpanshRouteCalculationState State { get; } = state;
}
