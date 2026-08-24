namespace EDActivityOverlay.Models;

public enum ExplorationLogKind
{
    Visit,
    NotableBody,
    Mapping,
    Biology,
    Codex,
    Manual
}

public sealed record ExplorationLogEntry(
    Guid Id,
    DateTimeOffset TimestampUtc,
    ExplorationLogKind Kind,
    string System,
    string Body,
    string Subject,
    string Detail,
    bool Bookmarked);

public sealed record ExplorationEarningsState(
    long UniversalCartographicsEstimate,
    long ExobiologyMinimumEstimate,
    long ExobiologyMaximumEstimate,
    DateTimeOffset? LastUniversalCartographicsSaleUtc,
    DateTimeOffset? LastExobiologySaleUtc,
    bool IsRebuilding)
{
    public static ExplorationEarningsState Empty { get; } = new(0, 0, 0, null, null, false);
}

public sealed class ExplorationEarningsChangedEventArgs(ExplorationEarningsState state) : EventArgs
{
    public ExplorationEarningsState State { get; } = state;
}

public sealed class ExplorationLogChangedEventArgs(IReadOnlyList<ExplorationLogEntry> entries) : EventArgs
{
    public IReadOnlyList<ExplorationLogEntry> Entries { get; } = entries;
}
