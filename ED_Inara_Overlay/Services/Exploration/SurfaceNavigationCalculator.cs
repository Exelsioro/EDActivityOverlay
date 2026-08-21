namespace ED_Inara_Overlay.Services.Exploration;

public sealed record SurfaceNavigationResult(double DistanceMeters, double BearingDegrees, double RelativeTurnDegrees)
{
    public double EscapeBearingDegrees => (BearingDegrees + 180) % 360;
    public double EscapeRelativeTurnDegrees => (RelativeTurnDegrees + 720) % 360 - 180;

    public bool IsFarEnough(int requiredDistanceMeters) =>
        requiredDistanceMeters <= 0 || DistanceMeters >= requiredDistanceMeters;
}

public static class SurfaceNavigationCalculator
{
    public static SurfaceNavigationResult? Calculate(
        double? latitude,
        double? longitude,
        double? headingDegrees,
        double? planetRadiusMeters,
        double? targetLatitude,
        double? targetLongitude)
    {
        if (latitude is null || longitude is null || targetLatitude is null || targetLongitude is null)
        {
            return null;
        }

        double radius = planetRadiusMeters is > 1_000 ? planetRadiusMeters.Value : 6_371_000;
        double lat1 = DegreesToRadians(latitude.Value);
        double lat2 = DegreesToRadians(targetLatitude.Value);
        double deltaLat = lat2 - lat1;
        double deltaLon = DegreesToRadians(targetLongitude.Value - longitude.Value);
        double a = Math.Pow(Math.Sin(deltaLat / 2), 2)
                   + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLon / 2), 2);
        double distance = radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0, 1 - a)));

        double y = Math.Sin(deltaLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2)
                   - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLon);
        double bearing = NormalizeDegrees(RadiansToDegrees(Math.Atan2(y, x)));
        double relative = headingDegrees is null
            ? 0
            : NormalizeSignedDegrees(bearing - headingDegrees.Value);
        return new SurfaceNavigationResult(distance, bearing, relative);
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180;
    private static double RadiansToDegrees(double value) => value * 180 / Math.PI;
    private static double NormalizeDegrees(double value) => (value % 360 + 360) % 360;
    private static double NormalizeSignedDegrees(double value) => (value + 540) % 360 - 180;
}
