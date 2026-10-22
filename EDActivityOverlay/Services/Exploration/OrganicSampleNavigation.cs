using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Exploration;

public sealed record OrganicSampleSeparation(
    SurfaceNavigationResult? NearestSample,
    int KnownSampleCount,
    int RequiredSampleCount,
    int RequiredSpacingMeters)
{
    public bool HasCompleteHistory => RequiredSampleCount > 0 && KnownSampleCount == RequiredSampleCount;
    public bool IsReady => HasCompleteHistory && RequiredSpacingMeters > 0
        && NearestSample?.IsFarEnough(RequiredSpacingMeters) == true;
    public double? EscapeBearingDegrees { get; init; }
    public double? EscapeRelativeTurnDegrees { get; init; }
    public double? EscapeDistanceMeters { get; init; }
}

/// <summary>Checks every prior sample; a heading is only an approximate way out, not a new colony location.</summary>
public static class OrganicSampleNavigation
{
    /// <summary>Physical sampling takes precedence over the selected exploration-queue destination.</summary>
    public static OrganicScanProgressSnapshot? ResolveActiveSurfaceSample(GameStateSnapshot state)
    {
        if (!(state.HasSurfacePosition || state.OnFootOnPlanet || state.InSrv || state.Landed)) return null;
        return state.OrganicProgress
            .Where(organic => !organic.Completed && organic.Stage is 1 or 2 && IsOnSampleBody(state, organic))
            .OrderByDescending(organic => organic.UpdatedUtc)
            .FirstOrDefault();
    }

    public static OrganicSampleSeparation Assess(GameStateSnapshot state, OrganicScanProgressSnapshot organic)
    {
        int required = organic.Completed ? 0 : Math.Clamp(organic.Stage, 0, 2);
        OrganicSampleLocation[] positions = KnownLocations(organic)
            .Where(position => position.SampleNumber <= required)
            .GroupBy(position => position.SampleNumber)
            .Select(group => group.Last())
            .ToArray();

        SurfaceNavigationResult? nearest = null;
        SurfaceNavigationResult[] navigation = [];
        double scannedRadius = state.ExplorationBodies.FirstOrDefault(body => body.BodyId == organic.BodyId)?.RadiusMeters ?? 0;
        double? radius = scannedRadius > 1_000 && double.IsFinite(scannedRadius) ? scannedRadius : state.PlanetRadiusMeters;
        if (IsOnSampleBody(state, organic) && ValidPosition(state.Latitude, state.Longitude) && radius is > 1_000
            && double.IsFinite(radius.Value))
        {
            navigation = positions.Select(position => SurfaceNavigationCalculator.Calculate(
                    state.Latitude, state.Longitude, state.HeadingDegrees, radius,
                    position.Latitude, position.Longitude))
                .Where(result => result is not null)
                .Select(result => result!)
                .ToArray();
            nearest = navigation.OrderBy(result => result.DistanceMeters).FirstOrDefault();
        }

        var separation = new OrganicSampleSeparation(nearest, positions.Length, required, organic.ColonyRangeMeters);
        if (!separation.HasCompleteHistory || separation.IsReady || nearest is null || organic.ColonyRangeMeters <= 0)
            return separation;

        (double bearing, double distance) = FindEscape(navigation, organic.ColonyRangeMeters);
        return separation with
        {
            EscapeBearingDegrees = bearing,
            EscapeRelativeTurnDegrees = state.HeadingDegrees is { } heading && double.IsFinite(heading)
                ? (bearing - heading % 360 + 540) % 360 - 180 : null,
            EscapeDistanceMeters = distance
        };
    }

    private static bool IsOnSampleBody(GameStateSnapshot state, OrganicScanProgressSnapshot organic)
    {
        if (!string.IsNullOrWhiteSpace(state.Commander) && !string.IsNullOrWhiteSpace(organic.Commander)
            && !state.Commander.Equals(organic.Commander, StringComparison.OrdinalIgnoreCase)) return false;
        if (state.SystemAddress != 0 && organic.SystemAddress != 0 && state.SystemAddress != organic.SystemAddress)
            return false;
        if ((state.SystemAddress == 0 || organic.SystemAddress == 0)
            && !string.IsNullOrWhiteSpace(state.StarSystem) && !string.IsNullOrWhiteSpace(organic.SystemName)
            && !state.StarSystem.Equals(organic.SystemName, StringComparison.OrdinalIgnoreCase)) return false;

        string bodyName = !string.IsNullOrWhiteSpace(organic.BodyName) ? organic.BodyName
            : state.ExplorationBodies.FirstOrDefault(body => body.BodyId == organic.BodyId)?.Name ?? string.Empty;
        return !string.IsNullOrWhiteSpace(state.CurrentBody) && !string.IsNullOrWhiteSpace(bodyName)
            && state.CurrentBody.Trim().Equals(bodyName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static (double Bearing, double Distance) FindEscape(
        IReadOnlyList<SurfaceNavigationResult> samples, double spacing)
    {
        if (samples.Count == 1)
            return (samples[0].DistanceMeters < 0.01 ? 0 : samples[0].EscapeBearingDegrees,
                Math.Max(0, spacing - samples[0].DistanceMeters));

        // Local tangent-plane hint for the union of the two exclusion circles.
        // Moving away from just the closest sample can send the player into the
        // other circle and make the arrow flip back and forth at their midpoint.
        // Search a bounded set of bearings; the exact great-circle distance checks
        // above, not this approximate route hint, determine sampling readiness.
        var centres = samples.Select(sample =>
        {
            double angle = sample.BearingDegrees * Math.PI / 180;
            return (X: sample.DistanceMeters * Math.Sin(angle), Y: sample.DistanceMeters * Math.Cos(angle));
        }).ToArray();
        double bestBearing = 0;
        double bestDistance = double.PositiveInfinity;
        IEnumerable<double> candidates = Enumerable.Range(0, 72).Select(index => index * 5d)
            .Concat(samples.Select(sample => sample.EscapeBearingDegrees));
        foreach (double bearing in candidates)
        {
            double angle = bearing * Math.PI / 180;
            double distance = 0;
            foreach (var centre in centres)
            {
                double along = centre.X * Math.Sin(angle) + centre.Y * Math.Cos(angle);
                double discriminant = spacing * spacing - centre.X * centre.X - centre.Y * centre.Y + along * along;
                if (discriminant < 0) continue;
                distance = Math.Max(distance, along + Math.Sqrt(discriminant));
            }
            // Deterministic tie-breaking avoids direction changes due to floating-point noise.
            if (distance < bestDistance - 0.01)
            {
                bestDistance = distance;
                bestBearing = bearing;
            }
        }
        return (bestBearing, bestDistance);
    }

    internal static IReadOnlyList<OrganicSampleLocation> Record(
        OrganicScanProgressSnapshot? previous,
        string scanType,
        DateTimeOffset timestamp,
        JournalEventOrigin origin,
        double? latitude,
        double? longitude)
    {
        bool first = scanType.Equals("Log", StringComparison.OrdinalIgnoreCase);
        bool second = scanType.Equals("Sample", StringComparison.OrdinalIgnoreCase);
        OrganicSampleLocation[] known = previous is null ? [] : KnownLocations(previous).ToArray();

        if (first)
        {
            // During bootstrap retain only the persisted sequence corresponding to this Log.
            // Historical journal lines do not contain sample coordinates.
            known = origin != JournalEventOrigin.Live
                ? known.Where(position => position.CollectedUtc >= timestamp).ToArray()
                : [];
        }

        if (origin != JournalEventOrigin.Live || (!first && !second)) return known;
        int number = first ? 1 : 2;
        var result = known.Where(position => position.SampleNumber != number).ToList();
        if (ValidPosition(latitude, longitude))
            result.Add(new OrganicSampleLocation(number, latitude!.Value, longitude!.Value, timestamp));
        return result.OrderBy(position => position.SampleNumber).ToArray();
    }

    private static IEnumerable<OrganicSampleLocation> KnownLocations(OrganicScanProgressSnapshot organic)
    {
        OrganicSampleLocation[] known = (organic.SampleLocations ?? [])
            .Where(position => position is not null && position.SampleNumber is 1 or 2
                && ValidPosition(position.Latitude, position.Longitude))
            .ToArray();
        if (known.Length > 0) return known;

        // Old saves only know the last position. Do not invent the first position at stage 2.
        return organic.Stage is 1 or 2 && ValidPosition(organic.LastSampleLatitude, organic.LastSampleLongitude)
            ? [new OrganicSampleLocation(organic.Stage, organic.LastSampleLatitude!.Value,
                organic.LastSampleLongitude!.Value, organic.UpdatedUtc)]
            : [];
    }

    private static bool ValidPosition(double? latitude, double? longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
}
