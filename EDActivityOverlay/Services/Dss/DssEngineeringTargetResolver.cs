using System;
using System.Collections.Generic;
using System.Globalization;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssEngineeringTargetResolution(
    int OfficialTargetCount,
    int TargetCount,
    double ScannerRadiusMultiplier,
    double StockCapAngularRadius,
    double ActualCapAngularRadius,
    double PredictedCoverage,
    bool Reduced);

/// <summary>
/// Reduces the official BODY EfficiencyTarget only when the Journal provides a
/// trustworthy PatchRadius/OriginalPatchRadius ratio.
///
/// The official count calibrates the unknown stock cap angle. Engineering then
/// scales that angle. Every integer N from 2..official is evaluated, using the
/// planner's exact polyhedral layouts where available and arbitrary-N layouts
/// otherwise.
/// </summary>
internal static class DssEngineeringTargetResolver
{
    private const double MinimumCoverageFraction = 0.90d;

    private static readonly object CacheGate =
        new();

    private static readonly Dictionary<
        string,
        DssEngineeringTargetResolution> Cache =
        new(StringComparer.Ordinal);

    public static DssEngineeringTargetResolution Resolve(
        int officialTargetCount,
        string targetSource,
        DssModuleSnapshot dssModule)
    {
        int official =
            Math.Clamp(
                officialTargetCount,
                DssSphericalPlacementPlanner.MinimumTargetCount,
                DssSphericalPlacementPlanner.MaximumTargetCount);

        double multiplier =
            DssSphericalCapCoverage
                .ResolveProbeRadiusMultiplier(
                    dssModule);

        bool bodyTarget =
            targetSource.Equals(
                "BODY",
                StringComparison.OrdinalIgnoreCase);

        if (!bodyTarget
            || multiplier <= 1.0005d)
        {
            return
                new DssEngineeringTargetResolution(
                    official,
                    official,
                    multiplier,
                    0d,
                    0d,
                    0d,
                    false);
        }

        string cacheKey =
            BuildCacheKey(
                official,
                dssModule,
                multiplier);

        lock (CacheGate)
        {
            if (Cache.TryGetValue(
                    cacheKey,
                    out DssEngineeringTargetResolution? cached))
            {
                return cached;
            }
        }

        IReadOnlyList<SphericalPoint> stockLayout =
            DssSphericalPlacementPlanner
                .GenerateOptimalSphericalPoints(
                    official);

        double stockAlpha =
            DssSphericalCapCoverage
                .SolveCapAngularRadiusForCoverage(
                    stockLayout,
                    MinimumCoverageFraction);

        double actualAlpha =
            Math.Clamp(
                stockAlpha * multiplier,
                0d,
                Math.PI / 2d);

        int selected =
            official;

        double selectedCoverage =
            DssSphericalCapCoverage
                .EvaluateUnionCoverage(
                    stockLayout,
                    actualAlpha);

        for (int candidate =
                 DssSphericalPlacementPlanner.MinimumTargetCount;
             candidate <= official;
             candidate++)
        {
            IReadOnlyList<SphericalPoint> layout =
                DssSphericalPlacementPlanner
                    .GenerateOptimalSphericalPoints(
                        candidate);

            double coverage =
                DssSphericalCapCoverage
                    .EvaluateUnionCoverage(
                        layout,
                        actualAlpha);

            if (coverage
                < MinimumCoverageFraction)
            {
                continue;
            }

            selected = candidate;
            selectedCoverage = coverage;
            break;
        }

        var result =
            new DssEngineeringTargetResolution(
                official,
                selected,
                multiplier,
                stockAlpha,
                actualAlpha,
                selectedCoverage,
                selected < official);

        lock (CacheGate)
        {
            Cache[cacheKey] = result;
        }

        if (result.Reduced)
        {
            Logger.Logger.Info(
                $"DSS PLAN engineering reduction: " +
                $"official={result.OfficialTargetCount}/BODY; " +
                $"patch={dssModule.PatchRadius:0.###}; " +
                $"original={dssModule.OriginalPatchRadius:0.###}; " +
                $"multiplier={result.ScannerRadiusMultiplier:0.###}x; " +
                $"stockAlpha={result.StockCapAngularRadius * 180d / Math.PI:0.00}deg; " +
                $"actualAlpha={result.ActualCapAngularRadius * 180d / Math.PI:0.00}deg; " +
                $"optimal={result.TargetCount}; " +
                $"coverage={result.PredictedCoverage:0.000}.");
        }

        return result;
    }

    private static string BuildCacheKey(
        int official,
        DssModuleSnapshot dssModule,
        double multiplier) =>
        official.ToString(
            CultureInfo.InvariantCulture)
        + "|"
        + dssModule.PatchRadius.ToString(
            "R",
            CultureInfo.InvariantCulture)
        + "|"
        + dssModule.OriginalPatchRadius.ToString(
            "R",
            CultureInfo.InvariantCulture)
        + "|"
        + multiplier.ToString(
            "R",
            CultureInfo.InvariantCulture);
}
