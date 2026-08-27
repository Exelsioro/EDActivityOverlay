using System;
using System.Collections.Generic;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Numerical spherical-cap coverage model on S^2.
///
/// Important unit rule:
/// Journal DSS_PatchRadius is a scanner stat, not a physical length in metres.
/// Never divide PatchRadius by BodyRadiusMeters. Engineering is represented by
/// the dimensionless ratio PatchRadius / OriginalPatchRadius.
///
/// The absolute stock cap angle is inferred from the body's official
/// EfficiencyTarget: for the stock layout at official N, solve the cap angle
/// that yields 90% union coverage. The actual engineered cap angle is then
/// scaled by the Journal ratio.
/// </summary>
internal static class DssSphericalCapCoverage
{
    private const int EvaluationSampleCount = 4096;

    private static readonly SphericalPoint[] SampleGrid =
        GenerateFibonacciGrid(
            EvaluationSampleCount);

    /// <summary>
    /// Actual scanner-radius multiplier from Journal values.
    /// Missing/invalid data is deliberately conservative (1.0); EngineeringLevel
    /// alone is not used to guess a footprint.
    /// </summary>
    public static double ResolveProbeRadiusMultiplier(
        DssModuleSnapshot dssModule)
    {
        if (dssModule.PatchRadius <= 0d
            || dssModule.OriginalPatchRadius <= 0d)
        {
            return 1d;
        }

        double multiplier =
            dssModule.PatchRadius
            / dssModule.OriginalPatchRadius;

        if (!double.IsFinite(multiplier)
            || multiplier <= 0d)
        {
            return 1d;
        }

        return
            Math.Clamp(
                multiplier,
                0.50d,
                3.00d);
    }

    /// <summary>
    /// Compatibility helper used by tests/research. BodyRadiusMeters is
    /// intentionally ignored because PatchRadius is not measured in metres.
    /// </summary>
    public static double CalculateCapAngularRadius(
        DssModuleSnapshot dssModule,
        double bodyRadiusMeters,
        int targetProbeCount)
    {
        _ = bodyRadiusMeters;

        int n =
            Math.Clamp(
                targetProbeCount,
                DssSphericalPlacementPlanner.MinimumTargetCount,
                DssSphericalPlacementPlanner.MaximumTargetCount);

        IReadOnlyList<SphericalPoint> stockLayout =
            DssSphericalPlacementPlanner
                .GenerateOptimalSphericalPoints(n);

        double stockAlpha =
            SolveCapAngularRadiusForCoverage(
                stockLayout,
                0.90d);

        double multiplier =
            ResolveProbeRadiusMultiplier(
                dssModule);

        return
            Math.Clamp(
                stockAlpha * multiplier,
                0d,
                Math.PI / 2d);
    }

    /// <summary>
    /// Finds the smallest cap angular radius whose union coverage reaches
    /// targetCoverageFraction for the supplied fixed layout.
    /// </summary>
    public static double SolveCapAngularRadiusForCoverage(
        IReadOnlyList<SphericalPoint> points,
        double targetCoverageFraction = 0.90d)
    {
        if (points is null
            || points.Count == 0)
        {
            return 0d;
        }

        double target =
            Math.Clamp(
                targetCoverageFraction,
                0d,
                1d);

        double low = 0d;
        double high = Math.PI / 2d;

        for (int iteration = 0;
             iteration < 48;
             iteration++)
        {
            double mid =
                (low + high) * 0.5d;

            double coverage =
                EvaluateUnionCoverage(
                    points,
                    mid);

            if (coverage >= target)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return high;
    }

    public static double EvaluateUnionCoverage(
        IReadOnlyList<SphericalPoint> points,
        double capAngularRadiusRadians)
    {
        if (points is null
            || points.Count == 0
            || capAngularRadiusRadians <= 0d)
        {
            return 0d;
        }

        double cosCapRadius =
            Math.Cos(
                capAngularRadiusRadians);

        int coveredCount = 0;

        for (int i = 0;
             i < SampleGrid.Length;
             i++)
        {
            SphericalPoint sample =
                SampleGrid[i];

            bool covered = false;

            for (int j = 0;
                 j < points.Count;
                 j++)
            {
                SphericalPoint center =
                    points[j];

                double dot =
                    sample.X * center.X
                    + sample.Y * center.Y
                    + sample.Z * center.Z;

                if (dot >= cosCapRadius)
                {
                    covered = true;
                    break;
                }
            }

            if (covered)
            {
                coveredCount++;
            }
        }

        return
            (double)coveredCount
            / SampleGrid.Length;
    }

    public static double SingleCapAreaFraction(
        double capAngularRadiusRadians) =>
        (1d
         - Math.Cos(
             Math.Clamp(
                 capAngularRadiusRadians,
                 0d,
                 Math.PI)))
        / 2d;

    private static SphericalPoint[] GenerateFibonacciGrid(
        int sampleCount)
    {
        var grid =
            new SphericalPoint[sampleCount];

        double goldenRatio =
            (1d + Math.Sqrt(5d))
            / 2d;

        for (int i = 0;
             i < sampleCount;
             i++)
        {
            double z =
                1d
                - (2d * i + 1d)
                  / sampleCount;

            double theta =
                Math.Acos(
                    Math.Clamp(
                        z,
                        -1d,
                        1d));

            double phi =
                2d * Math.PI * i
                / goldenRatio;

            grid[i] =
                new SphericalPoint(
                    theta,
                    phi);
        }

        return grid;
    }
}
