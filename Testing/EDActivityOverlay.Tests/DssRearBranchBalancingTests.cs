using System;
using System.Collections.Generic;
using System.Linq;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssRearBranchBalancingTests
{
    private const double RecordedDiameterDegrees = 28.4d;

    // Surface points reconstructed from the repeated N21 -> N17 live layout.
    // Under v52 automatic branch selection these became:
    //
    //   inner: 1.009, 1.020, 1.034
    //   outer: 1.448, 1.487, 1.508, 1.528, 1.622, 1.645
    //
    // leaving the exact K~1.15..1.30 region where manual finishing shots
    // repeatedly produced the useful native coverage gain.
    private static readonly IReadOnlyList<SphericalPoint>
        RecordedN17RearPoints =
            new[]
            {
                SphericalPoint.FromDegrees(
                    154.642d,
                    -150.334d),
                SphericalPoint.FromDegrees(
                    134.052d,
                    -50.023d),
                SphericalPoint.FromDegrees(
                    144.598d,
                    27.298d),
                SphericalPoint.FromDegrees(
                    139.272d,
                    99.884d),
                SphericalPoint.FromDegrees(
                    98.838d,
                    -149.216d),
                SphericalPoint.FromDegrees(
                    104.323d,
                    -104.356d),
                SphericalPoint.FromDegrees(
                    92.239d,
                    -4.045d),
                SphericalPoint.FromDegrees(
                    95.229d,
                    60.877d),
                SphericalPoint.FromDegrees(
                    110.265d,
                    162.997d)
            };

    [Fact]
    public void RecordedN17RearBatch_MixesTrajectoryFamilies()
    {
        IReadOnlyList<bool> branches =
            DssSphericalPlacementPlanner
                .SelectBalancedRearBranches(
                    RecordedN17RearPoints,
                    RecordedDiameterDegrees);

        Assert.Equal(
            RecordedN17RearPoints.Count,
            branches.Count);

        int outer =
            branches.Count(
                value => value);

        int safeDual =
            RecordedN17RearPoints.Count(
                point =>
                    DssSphericalProjection
                        .ShouldUseOuterFarBranch(
                            point.Theta,
                            RecordedDiameterDegrees));

        Assert.InRange(
            outer,
            1,
            safeDual - 1);

        Assert.Contains(
            branches,
            value => value);

        Assert.Contains(
            branches,
            value => !value);
    }

    [Fact]
    public void RecordedN17RearBatch_FillsPreviouslyEmptyInnerAnnulus()
    {
        IReadOnlyList<bool> branches =
            DssSphericalPlacementPlanner
                .SelectBalancedRearBranches(
                    RecordedN17RearPoints,
                    RecordedDiameterDegrees);

        var radii =
            ProjectRadii(
                RecordedN17RearPoints,
                branches);

        int innerMidBand =
            radii.Count(
                k =>
                    k >= 1.15d
                    && k <= 1.30d);

        Assert.True(
            innerMidBand >= 2,
            $"Expected at least two K=1.15..1.30 aims; got [{string.Join(", ", radii.Select(k => k.ToString("0.000")))}].");
    }

    [Fact]
    public void BalancedRearBatch_MateriallyShrinksV52RadialHole()
    {
        IReadOnlyList<bool> balancedBranches =
            DssSphericalPlacementPlanner
                .SelectBalancedRearBranches(
                    RecordedN17RearPoints,
                    RecordedDiameterDegrees);

        var v52Branches =
            RecordedN17RearPoints
                .Select(
                    point =>
                        DssSphericalProjection
                            .ShouldUseOuterFarBranch(
                                point.Theta,
                                RecordedDiameterDegrees))
                .ToArray();

        double balancedGap =
            MaximumRadialGap(
                ProjectRadii(
                    RecordedN17RearPoints,
                    balancedBranches));

        double oldGap =
            MaximumRadialGap(
                ProjectRadii(
                    RecordedN17RearPoints,
                    v52Branches));

        Assert.True(
            oldGap > 0.35d);

        Assert.True(
            balancedGap <= 0.28d,
            $"Balanced maximum gap was {balancedGap:0.000}.");

        Assert.True(
            balancedGap
            <= oldGap - 0.10d);
    }

    [Fact]
    public void NearRearHorizon_StillCannotUseUnsafeOuterBranch()
    {
        var points =
            new[]
            {
                SphericalPoint.FromDegrees(
                    92.2d,
                    0d),
                SphericalPoint.FromDegrees(
                    145d,
                    90d),
                SphericalPoint.FromDegrees(
                    135d,
                    -90d)
            };

        IReadOnlyList<bool> branches =
            DssSphericalPlacementPlanner
                .SelectBalancedRearBranches(
                    points,
                    RecordedDiameterDegrees);

        Assert.False(
            branches[0]);
    }

    [Fact]
    public void ExplicitInnerAndOuterProjection_AreOppositeAimFamilies()
    {
        var point =
            SphericalPoint.FromDegrees(
                139.272d,
                99.884d);

        (double innerX, double innerY, double innerK) =
            DssSphericalProjection
                .ProjectSphericalToScreenAim(
                    point,
                    RecordedDiameterDegrees,
                    false);

        (double outerX, double outerY, double outerK) =
            DssSphericalProjection
                .ProjectSphericalToScreenAim(
                    point,
                    RecordedDiameterDegrees,
                    true);

        Assert.InRange(
            innerK,
            1.17d,
            1.22d);

        Assert.InRange(
            outerK,
            1.48d,
            1.54d);

        double innerPhi =
            Math.Atan2(
                innerY,
                innerX);

        double outerPhi =
            Math.Atan2(
                outerY,
                outerX);

        Assert.True(
            Math.Abs(
                Math.Abs(
                    Normalize(
                        outerPhi
                        - innerPhi))
                - Math.PI)
            < 1e-9d);
    }

    [Fact]
    public void CorrectionRearBranch_StartsInnerThenAlternatesOuter()
    {
        var point =
            SphericalPoint.FromDegrees(
                140d,
                0d);

        Assert.False(
            DssSphericalPlacementPlanner
                .ShouldUseOuterCorrectionBranch(
                    1,
                    point,
                    RecordedDiameterDegrees));

        Assert.True(
            DssSphericalPlacementPlanner
                .ShouldUseOuterCorrectionBranch(
                    2,
                    point,
                    RecordedDiameterDegrees));

        Assert.False(
            DssSphericalPlacementPlanner
                .ShouldUseOuterCorrectionBranch(
                    3,
                    point,
                    RecordedDiameterDegrees));
    }

    private static IReadOnlyList<double> ProjectRadii(
        IReadOnlyList<SphericalPoint> points,
        IReadOnlyList<bool> branches)
    {
        var result =
            new List<double>(
                points.Count);

        for (int i = 0;
             i < points.Count;
             i++)
        {
            (_, _, double k) =
                DssSphericalProjection
                    .ProjectSphericalToScreenAim(
                        points[i],
                        RecordedDiameterDegrees,
                        branches[i]);

            result.Add(k);
        }

        return result;
    }

    private static double MaximumRadialGap(
        IReadOnlyList<double> radii)
    {
        double safeK =
            DssSphericalProjection
                .EstimateSafeNormalizedRadius(
                    RecordedDiameterDegrees);

        double[] sorted =
            radii
                .OrderBy(
                    value => value)
                .ToArray();

        double previous = 1d;
        double maximum = 0d;

        foreach (double radius
                 in sorted)
        {
            maximum =
                Math.Max(
                    maximum,
                    radius - previous);

            previous =
                radius;
        }

        maximum =
            Math.Max(
                maximum,
                safeK - previous);

        return maximum;
    }

    private static double Normalize(
        double radians)
    {
        while (radians <= -Math.PI)
        {
            radians +=
                Math.PI * 2d;
        }

        while (radians > Math.PI)
        {
            radians -=
                Math.PI * 2d;
        }

        return radians;
    }
}
