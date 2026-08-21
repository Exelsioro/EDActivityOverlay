using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

/// <summary>
/// Reproducible starting layouts for the DSS. Coordinates are relative to the visible
/// planetary disc: 1.0 is the limb and values above 1.0 are aimed beyond it to wrap
/// a probe towards the far side. The game does not publish the target before completion,
/// so callers must use the target displayed by the DSS HUD.
/// </summary>
public static class DssProbePatternCatalog
{
    public const int MinimumTarget = 2;
    public const int MaximumTarget = 12;

    public static DssProbePattern Get(int target)
    {
        target = Math.Clamp(target, MinimumTarget, MaximumTarget);
        if (target <= 4) return Ring(target, 0.48, DssAimZone.Disc, "Loc_DSS_STRATEGY_SMALL");
        if (target <= 7) return CenterAndRing(target, 0.72, "Loc_DSS_STRATEGY_MEDIUM");
        if (target <= 9) return CenterAndRing(target, 0.88, "Loc_DSS_STRATEGY_LARGE");
        return DoubleRing(target);
    }

    private static DssProbePattern Ring(int count, double radius, DssAimZone zone, string strategyKey) =>
        new(count, CreateRing(count, 1, radius, zone, -90).ToArray(), strategyKey, "Loc_DSS_ADJUSTMENT_HINT");

    private static DssProbePattern CenterAndRing(int count, double radius, string strategyKey)
    {
        var points = new List<DssAimPoint> { new(1, 0, 0, DssAimZone.Disc) };
        points.AddRange(CreateRing(count - 1, 2, radius, DssAimZone.Limb, -90));
        return new DssProbePattern(count, points, strategyKey, "Loc_DSS_ADJUSTMENT_HINT");
    }

    private static DssProbePattern DoubleRing(int count)
    {
        int innerCount = count / 2;
        int outerCount = count - innerCount;
        var points = new List<DssAimPoint>();
        points.AddRange(CreateRing(innerCount, 1, 0.57, DssAimZone.Disc, -90));
        points.AddRange(CreateRing(outerCount, innerCount + 1, 1.14, DssAimZone.FarSide,
            -90 + 180.0 / outerCount));
        return new DssProbePattern(count, points, "Loc_DSS_STRATEGY_VERY_LARGE", "Loc_DSS_ADJUSTMENT_HINT");
    }

    private static IEnumerable<DssAimPoint> CreateRing(
        int count,
        int firstSequence,
        double radius,
        DssAimZone zone,
        double startDegrees)
    {
        for (int index = 0; index < count; index++)
        {
            double angle = (startDegrees + index * 360.0 / count) * Math.PI / 180.0;
            yield return new DssAimPoint(
                firstSequence + index,
                Math.Cos(angle) * radius,
                Math.Sin(angle) * radius,
                zone);
        }
    }
}
