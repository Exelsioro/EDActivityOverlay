using System;
using System.Collections.Generic;
using System.Linq;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssFlightOrderAndBaseReserveTests
{
    [Fact]
    public void ShotOrdering_IsStrictlyFarToNearBySurfaceDepth()
    {
        var input =
            new[]
            {
                SphericalPoint.FromDegrees(95d, 40d),
                SphericalPoint.FromDegrees(170d, -20d),
                SphericalPoint.FromDegrees(35d, 10d),
                SphericalPoint.FromDegrees(125d, 80d),
                SphericalPoint.FromDegrees(0d, 0d),
                SphericalPoint.FromDegrees(150d, 30d)
            };

        IReadOnlyList<SphericalPoint> ordered =
            DssSphericalPlacementPlanner
                .OptimizeShotOrdering(
                    input);

        for (int i = 1;
             i < ordered.Count;
             i++)
        {
            Assert.True(
                ordered[i - 1].Theta
                >= ordered[i].Theta
                   - 1e-12d);
        }

        Assert.InRange(
            ordered[0].Theta,
            Degrees(169.9d),
            Degrees(170.1d));

        Assert.InRange(
            ordered[^1].Theta,
            0d,
            Degrees(0.1d));
    }

    [Fact]
    public void ProvenLowN7Case_KeepsSixProbeBase()
    {
        var module =
            new DssModuleSnapshot(
                "dss",
                "DSS",
                26d,
                20d,
                "expanded",
                3);

        DssEngineeringTargetResolution resolution =
            DssEngineeringTargetResolver.Resolve(
                7,
                "HUD_CV",
                module);

        Assert.Equal(
            6,
            resolution.TargetCount);

        Assert.Equal(
            0.90d,
            DssEngineeringTargetResolver
                .ResolveRequiredCoverageFraction(
                    7),
            12);
    }

    [Fact]
    public void ApolloN18Patch26Case_UsesSixteenProbeBase()
    {
        var module =
            new DssModuleSnapshot(
                "dss",
                "DSS",
                26d,
                20d,
                "expanded",
                3);

        DssEngineeringTargetResolution resolution =
            DssEngineeringTargetResolver.Resolve(
                18,
                "HUD_CV",
                module);

        Assert.Equal(
            16,
            resolution.TargetCount);

        Assert.InRange(
            resolution.PredictedCoverage,
            0.94d,
            0.97d);

        Assert.Equal(
            0.936d,
            DssEngineeringTargetResolver
                .ResolveRequiredCoverageFraction(
                    18),
            12);
    }

    [Fact]
    public void N21Patch26Case_AddsHighComplexityReserve()
    {
        var module =
            new DssModuleSnapshot(
                "dss",
                "DSS",
                26d,
                20d,
                "expanded",
                3);

        DssEngineeringTargetResolution resolution =
            DssEngineeringTargetResolver.Resolve(
                21,
                "HUD_CV",
                module);

        Assert.Equal(
            18,
            resolution.TargetCount);

        Assert.InRange(
            resolution.PredictedCoverage,
            0.94d,
            0.97d);

        Assert.Equal(
            0.94d,
            DssEngineeringTargetResolver
                .ResolveRequiredCoverageFraction(
                    21),
            12);
    }

    [Theory]
    [InlineData(6, 0.90)]
    [InlineData(7, 0.90)]
    [InlineData(9, 0.90)]
    [InlineData(10, 0.904)]
    [InlineData(18, 0.936)]
    [InlineData(21, 0.94)]
    [InlineData(32, 0.94)]
    public void RequiredCoverageReserve_IsBounded(
        int official,
        double expected)
    {
        Assert.Equal(
            expected,
            DssEngineeringTargetResolver
                .ResolveRequiredCoverageFraction(
                    official),
            12);
    }

    private static double Degrees(
        double value) =>
        value * Math.PI / 180d;
}
