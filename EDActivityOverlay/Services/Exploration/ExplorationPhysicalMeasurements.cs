using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Exploration;

/// <summary>UI measurements, preserving unknown values instead of displaying fallback zeroes.</summary>
internal sealed record ExplorationPhysicalMeasurements(double? GravityG, double? TemperatureKelvin, double? PressureAtmospheres)
{
    internal static bool IsPressureKnown(double value, bool? known) =>
        double.IsFinite(value) && value >= 0 && (known ?? value > 0);

    public static ExplorationPhysicalMeasurements From(ExplorationCatalogBody body) => new(
        Positive(body.GravityG), Positive(body.SurfaceTemperatureKelvin),
        IsPressureKnown(body.SurfacePressureAtmospheres, body.SurfacePressureKnown)
            ? body.SurfacePressureAtmospheres : null);

    private static double? Positive(double value) => double.IsFinite(value) && value > 0 ? value : null;
}
