using System;
using System.Collections.Generic;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Evaluates planetary surface coverage using a spherical-cap model on S^2.
/// Incorporates probe PatchRadius and engineering into the angular footprint.
/// </summary>
internal static class DssSphericalCapCoverage
{
    private const int EvaluationSampleCount = 1000;
    private static readonly SphericalPoint[] SampleGrid = GenerateFibonacciGrid(EvaluationSampleCount);

    /// <summary>
    /// Computes the angular cap radius alpha (radians) for a probe impact on a planet.
    /// Uses module PatchRadius and BodyRadius if available, or theoretical baseline for the target N.
    /// </summary>
    public static double CalculateCapAngularRadius(
        DssModuleSnapshot dssModule,
        double bodyRadiusMeters,
        int targetProbeCount)
    {
        if (dssModule.PatchRadius > 0 && bodyRadiusMeters > 0)
        {
            // Arc length = PatchRadius -> angular radius alpha = PatchRadius / BodyRadius
            double alpha = dssModule.PatchRadius / bodyRadiusMeters;
            return Math.Clamp(alpha, 0.10d, Math.PI / 2d);
        }

        // Standard theoretical baseline required for 90% coverage with N probes:
        // Area(N caps) ~ 4 * pi * 0.90 / N -> 1 - cos(alpha) ~ 1.8 / N
        int n = Math.Clamp(targetProbeCount, 2, 18);
        double engineeringMultiplier = dssModule.EngineeringLevel > 0
            ? 1.0d + 0.10d * dssModule.EngineeringLevel
            : 1.0d;

        double baseAlpha = n switch
        {
            2 => 1.35d,              // ~77 deg
            3 => 1.10d,              // ~63 deg
            4 => 0.95d,              // ~54.5 deg (tetrahedral overlap)
            5 => 0.85d,              // ~48.7 deg
            6 => 0.785d,             // ~45.0 deg (octahedral)
            7 => 0.72d,              // ~41.2 deg
            8 => 0.68d,              // ~38.9 deg (cubic)
            <= 12 => 0.58d,          // ~33.2 deg
            _ => 0.48d               // ~27.5 deg
        };

        return Math.Clamp(baseAlpha * engineeringMultiplier, 0.15d, Math.PI / 2d);
    }

    /// <summary>
    /// Computes the union surface area coverage fraction in [0.0, 1.0] for a collection of spherical caps.
    /// </summary>
    public static double EvaluateUnionCoverage(
        IReadOnlyList<SphericalPoint> points,
        double capAngularRadiusRadians)
    {
        if (points == null || points.Count == 0 || capAngularRadiusRadians <= 0)
        {
            return 0d;
        }

        double cosCapRadius = Math.Cos(capAngularRadiusRadians);
        int coveredCount = 0;

        for (int i = 0; i < SampleGrid.Length; i++)
        {
            SphericalPoint sample = SampleGrid[i];
            bool covered = false;

            for (int j = 0; j < points.Count; j++)
            {
                SphericalPoint center = points[j];
                double dot = sample.X * center.X + sample.Y * center.Y + sample.Z * center.Z;
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

        return (double)coveredCount / SampleGrid.Length;
    }

    /// <summary>
    /// Single spherical cap area on unit sphere: A = 2 * pi * (1 - cos(alpha)).
    /// </summary>
    public static double SingleCapAreaFraction(double capAngularRadiusRadians) =>
        (1d - Math.Cos(Math.Clamp(capAngularRadiusRadians, 0d, Math.PI))) / 2d;

    /// <summary>
    /// Generates a uniform Fibonacci lattice of points on the unit sphere for numerical integration.
    /// </summary>
    private static SphericalPoint[] GenerateFibonacciGrid(int sampleCount)
    {
        var grid = new SphericalPoint[sampleCount];
        double goldenRatio = (1d + Math.Sqrt(5d)) / 2d;

        for (int i = 0; i < sampleCount; i++)
        {
            double z = 1d - (2d * i + 1d) / sampleCount; // [-1, 1]
            double theta = Math.Acos(Math.Clamp(z, -1d, 1d));
            double phi = 2d * Math.PI * i / goldenRatio;

            grid[i] = new SphericalPoint(theta, phi);
        }

        return grid;
    }
}
