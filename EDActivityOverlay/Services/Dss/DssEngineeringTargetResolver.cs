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
/// Resolves the effective DSS batch size from an authoritative native
/// efficiency target (HUD_CV first, BODY as fallback) and the actual Journal
/// PatchRadius/OriginalPatchRadius ratio.
///
/// The official count calibrates the unknown stock cap angle. Engineering is
/// then applied to spherical-cap AREA, not directly to angular radius.
/// Every integer N from 2..official is evaluated against the resulting actual
/// footprint. Low-N cases keep the original 90% threshold; high-N cases add a
/// small model-uncertainty reserve so a useful final rear probe is sent in the
/// base batch instead of after the correction-settling delay.
///
/// SETTINGS/fallback targets are never silently reduced, because they are not
/// an authoritative native calibration. Their cap angles are still calculated so the
/// correction model has a usable footprint.
/// </summary>
internal static class DssEngineeringTargetResolver
{
    private const double MinimumCoverageFraction = 0.90d;

    // High-N layouts accumulate more overlap/projection uncertainty than the
    // low-N polyhedral cases. Requiring the same bare 90% model threshold for
    // N18/N21 repeatedly produced a "base complete -> wait -> one useful rear
    // correction" sequence. That is slower than sending one additional base
    // probe up front because correction mode must wait for native impacts and
    // coverage settling.
    //
    // Keep the proven low-N behaviour unchanged. Starting above native N=9,
    // raise the required model coverage by 0.4 percentage points per official
    // probe, capped at +4 pp. Examples:
    //   N7  -> 90.0%  (validated N7 -> base N6 remains possible)
    //   N9  -> 90.0%
    //   N18 -> 93.6%  (26/20 case moves N15 -> N16)
    //   N21 -> 94.0%  (26/20 case moves N17 -> N18)
    private const int CoverageReserveStartOfficialCount = 9;
    private const double CoverageReservePerOfficialProbe = 0.004d;
    private const double MaximumCoverageReserve = 0.04d;


    private static readonly object CacheGate =
        new();

    private static readonly Dictionary<
        string,
        DssEngineeringTargetResolution> Cache =
        new(StringComparer.Ordinal);

    internal static double ResolveRequiredCoverageFraction(
        int officialTargetCount)
    {
        int excess =
            Math.Max(
                0,
                officialTargetCount
                - CoverageReserveStartOfficialCount);

        double reserve =
            Math.Min(
                MaximumCoverageReserve,
                excess
                * CoverageReservePerOfficialProbe);

        return
            MinimumCoverageFraction
            + reserve;
    }

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

        bool authoritativeTarget =
            targetSource.Equals(
                "BODY",
                StringComparison.OrdinalIgnoreCase)
            || targetSource.Equals(
                "HUD_CV",
                StringComparison.OrdinalIgnoreCase);

        string cacheKey =
            BuildCacheKey(
                official,
                authoritativeTarget,
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
            DssSphericalCapCoverage
                .ScaleCapAngularRadiusByArea(
                    stockAlpha,
                    multiplier);

        int selected =
            official;

        double selectedCoverage =
            DssSphericalCapCoverage
                .EvaluateUnionCoverage(
                    stockLayout,
                    actualAlpha);

        bool mayReduce =
            authoritativeTarget
            && multiplier > 1.0005d;

        if (mayReduce)
        {
            double requiredCoverage =
                ResolveRequiredCoverageFraction(
                    official);

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
                    < requiredCoverage)
                {
                    continue;
                }

                selected =
                    candidate;

                selectedCoverage =
                    coverage;

                break;
            }
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
            Cache[cacheKey] =
                result;
        }

        if (result.Reduced)
        {
            double stockArea =
                DssSphericalCapCoverage
                    .SingleCapAreaFraction(
                        result.StockCapAngularRadius);

            double actualArea =
                DssSphericalCapCoverage
                    .SingleCapAreaFraction(
                        result.ActualCapAngularRadius);

            Logger.Logger.Info(
                $"DSS PLAN engineering reduction: " +
                $"official={result.OfficialTargetCount}/{targetSource}; " +
                $"patch={dssModule.PatchRadius:0.###}; " +
                $"original={dssModule.OriginalPatchRadius:0.###}; " +
                $"areaMultiplier={result.ScannerRadiusMultiplier:0.###}x; " +
                $"stockAlpha={result.StockCapAngularRadius * 180d / Math.PI:0.00}deg; " +
                $"actualAlpha={result.ActualCapAngularRadius * 180d / Math.PI:0.00}deg; " +
                $"areaRatio={(stockArea > 0d ? actualArea / stockArea : 1d):0.###}x; " +
                $"optimal={result.TargetCount}; " +
                $"coverage={result.PredictedCoverage:0.000}; " +
                $"required={ResolveRequiredCoverageFraction(result.OfficialTargetCount):0.000}.");
        }

        return result;
    }

    private static string BuildCacheKey(
        int official,
        bool authoritativeTarget,
        DssModuleSnapshot dssModule,
        double multiplier) =>
        (authoritativeTarget
            ? "A|"
            : "S|")
        + official.ToString(
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
