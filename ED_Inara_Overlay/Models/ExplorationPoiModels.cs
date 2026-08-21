namespace ED_Inara_Overlay.Models;

public enum ExplorationPoiStatus
{
    Disabled,
    Idle,
    Loading,
    Available,
    Unavailable
}

public sealed record ExplorationPoiSnapshot(
    string Source,
    string Id,
    string Name,
    string System,
    string Category,
    string Region,
    string Summary,
    string Url,
    double Rating,
    double DistanceLy,
    double X,
    double Y,
    double Z,
    DateTimeOffset FetchedUtc);

public sealed record ExplorationPoiState(
    ExplorationPoiStatus Status,
    ExplorationPoiSnapshot? Nearest,
    string Error)
{
    public static ExplorationPoiState Idle { get; } = new(ExplorationPoiStatus.Idle, null, string.Empty);
    public ExplorationPoiSnapshot? NearestCanonn { get; init; }
    public ExplorationPoiSnapshot? Closest => new[] { Nearest, NearestCanonn }
        .Where(item => item is not null)
        .Cast<ExplorationPoiSnapshot>()
        .OrderBy(item => item.DistanceLy)
        .FirstOrDefault();
}

public sealed class ExplorationPoiChangedEventArgs(ExplorationPoiState state) : EventArgs
{
    public ExplorationPoiState State { get; } = state;
}
