using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssEngineeringTargetResolverTests
{
    [Fact]
    public void StockScanner_KeepsOfficialSixProbeTarget()
    {
        var module =
            new DssModuleSnapshot(
                "dss",
                "DSS",
                20d,
                20d,
                string.Empty,
                0);

        DssEngineeringTargetResolution result =
            DssEngineeringTargetResolver.Resolve(
                6,
                "BODY",
                module);

        Assert.Equal(
            6,
            result.TargetCount);

        Assert.False(
            result.Reduced);

        Assert.InRange(
            result.ScannerRadiusMultiplier,
            0.9999d,
            1.0001d);

        Assert.True(
            result.StockCapAngularRadius > 0d);

        Assert.InRange(
            result.ActualCapAngularRadius,
            result.StockCapAngularRadius - 1e-9d,
            result.StockCapAngularRadius + 1e-9d);
    }

    [Fact]
    public void FiftyPercentExpandedScanner_UsesAreaScaling()
    {
        var module =
            new DssModuleSnapshot(
                "dss",
                "DSS",
                30d,
                20d,
                "expanded",
                5);

        DssEngineeringTargetResolution result =
            DssEngineeringTargetResolver.Resolve(
                6,
                "BODY",
                module);

        double stockArea =
            DssSphericalCapCoverage
                .SingleCapAreaFraction(
                    result.StockCapAngularRadius);

        double actualArea =
            DssSphericalCapCoverage
                .SingleCapAreaFraction(
                    result.ActualCapAngularRadius);

        Assert.InRange(
            actualArea / stockArea,
            1.499d,
            1.501d);

        // With the current deterministic N=5 layout, +50% area is sufficient
        // for 90%, while N=4 is not. Old angular-radius multiplication
        // incorrectly collapsed this BODY to four probes.
        Assert.Equal(
            5,
            result.TargetCount);

        Assert.True(
            result.Reduced);

        Assert.True(
            result.PredictedCoverage >= 0.90d);
    }

    [Fact]
    public void PreEngineeredDoubleAreaScanner_ReducesSixProbeBodyToFour()
    {
        var module =
            new DssModuleSnapshot(
                "dss",
                "DSS",
                40d,
                20d,
                "preengineered",
                5);

        DssEngineeringTargetResolution result =
            DssEngineeringTargetResolver.Resolve(
                6,
                "BODY",
                module);

        Assert.Equal(
            4,
            result.TargetCount);

        Assert.True(
            result.Reduced);

        Assert.True(
            result.PredictedCoverage >= 0.90d);
    }

    [Fact]
    public void SettingsTarget_IsNeverReducedButKeepsActualFootprint()
    {
        var module =
            new DssModuleSnapshot(
                "dss",
                "DSS",
                30d,
                20d,
                "expanded",
                5);

        DssEngineeringTargetResolution result =
            DssEngineeringTargetResolver.Resolve(
                6,
                "SETTINGS",
                module);

        Assert.Equal(
            6,
            result.TargetCount);

        Assert.False(
            result.Reduced);

        Assert.True(
            result.ActualCapAngularRadius
            > result.StockCapAngularRadius);
    }

    [Theory]
    [InlineData(21.0)]
    [InlineData(24.5)]
    [InlineData(28.0)]
    public void RearAntipode_IsMidwayBetweenHorizonAndMiss(
        double angularDiameter)
    {
        double miss =
            DssSphericalProjection
                .EstimateBoundaryNormalizedRadius(
                    angularDiameter);

        double rear =
            DssSphericalProjection
                .EstimateRearAntipodeNormalizedRadius(
                    angularDiameter);

        double projectedRear =
            DssSphericalProjection
                .ProjectSurfacePolarAngleToDssAim(
                    Math.PI,
                    angularDiameter);

        Assert.InRange(
            projectedRear,
            rear - 1e-6d,
            rear + 1e-6d);

        Assert.InRange(
            rear - 1d,
            miss - rear - 1e-6d,
            miss - rear + 1e-6d);
    }

    [Theory]
    [InlineData(21.0)]
    [InlineData(24.5)]
    [InlineData(28.0)]
    public void VisibleHorizon_MapsExactlyToOneRh(
        double angularDiameter)
    {
        double k =
            DssSphericalProjection
                .ProjectSurfacePolarAngleToDssAim(
                    Math.PI / 2d,
                    angularDiameter);

        Assert.InRange(
            k,
            0.999999d,
            1.000001d);
    }
}
