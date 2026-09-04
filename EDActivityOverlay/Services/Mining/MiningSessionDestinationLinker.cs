using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Mining;

internal static class MiningSessionDestinationLinker
{
    public static MiningSessionDestinationContext Capture(
        MiningSessionSnapshot session,
        MiningDestinationSnapshot destination)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(destination);

        if (session.State == MiningSessionState.Idle
            || !destination.Available
            || !SystemMatches(session.SystemName, destination.SystemName))
        {
            return MiningSessionDestinationContext.Empty;
        }

        if (!string.IsNullOrWhiteSpace(session.RingName)
            && !RingMatches(
                session.SystemName,
                session.RingName,
                destination.SystemName,
                destination.RingName))
        {
            return MiningSessionDestinationContext.Empty;
        }

        return new MiningSessionDestinationContext
        {
            SystemName = destination.SystemName,
            BodyName = destination.BodyName,
            RingName = destination.RingName,
            Confirmed =
                !string.IsNullOrWhiteSpace(session.RingName)
                && RingMatches(
                    session.SystemName,
                    session.RingName,
                    destination.SystemName,
                    destination.RingName),
            PrimaryCommodityId = destination.PrimaryCommodityId,
            TargetCommodityIds = destination.TargetCommodityIds.ToArray(),
            OverlapMultiplier = Math.Max(0, destination.OverlapMultiplier),
            ResType = destination.ResType.ToString(),
            QualityCommodityId = destination.QualityCommodityId,
            MeasuredAverageContentPercent =
                Math.Max(0, destination.MeasuredAverageContentPercent),
            QualitySource = destination.QualitySource,
            SelectedUtc = destination.SelectedUtc == DateTimeOffset.MinValue
                ? null
                : destination.SelectedUtc
        };
    }

    public static MiningSessionDestinationContext Reconcile(
        MiningSessionSnapshot session,
        MiningSessionDestinationContext context)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Available
            || session.State == MiningSessionState.Idle
            || !SystemMatches(session.SystemName, context.SystemName))
        {
            return MiningSessionDestinationContext.Empty;
        }

        if (string.IsNullOrWhiteSpace(session.RingName))
        {
            return context with { Confirmed = false };
        }

        if (!RingMatches(
                session.SystemName,
                session.RingName,
                context.SystemName,
                context.RingName))
        {
            return MiningSessionDestinationContext.Empty;
        }

        return context with { Confirmed = true };
    }

    internal static bool RingMatches(
        string sessionSystem,
        string sessionRing,
        string destinationSystem,
        string destinationRing)
    {
        string actual = NormalizeRing(sessionSystem, sessionRing);
        string expected = NormalizeRing(destinationSystem, destinationRing);

        return actual.Length > 0
               && expected.Length > 0
               && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SystemMatches(string left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRing(string system, string ring) =>
        string.Join(
            " ",
            MiningDestinationSnapshot.ShortRingName(system, ring)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries))
        .Trim();
}
