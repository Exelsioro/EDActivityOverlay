using System;
using System.Linq;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssCoverageOptimizedPlannerTests
{
    private static readonly DssModuleSnapshot LivePatch26Module =
        new(
            "dss",
            "DSS",
            26d,
            20d,
            "expanded",
            3);

    [Fact]
    public void LiveN18Patch26Case_KeepsPredictedBatchAtFifteen()
    {
        DssEngineeringTargetResolution resolution =
            DssEngineeringTargetResolver.Resolve(
                18,
                "HUD_CV",
                LivePatch26Module);

        Assert.Equal(
            15,
            resolution.TargetCount);
    }

    [Fact]
    public void OptimizedN15Layout_MateriallyImprovesWholeSphereCoverage()
    {
        DssEngineeringTargetResolution resolution =
            DssEngineeringTargetResolver.Resolve(
                18,
                "HUD_CV",
                LivePatch26Module);

        Assert.Equal(
            15,
            resolution.TargetCount);

        var legacy =
            DssSphericalPlacementPlanner
                .GenerateOptimalSphericalPoints(
                    resolution.TargetCount);

        var optimized =
            DssSphericalPlacementPlanner
                .GetCoverageOptimizedBasePoints(
                    resolution.TargetCount,
                    resolution.ActualCapAngularRadius);

        double legacyCoverage =
            DssSphericalCapCoverage
                .EvaluateUnionCoverage(
                    legacy,
                    resolution.ActualCapAngularRadius);

        double optimizedCoverage =
            DssSphericalCapCoverage
                .EvaluateUnionCoverage(
                    optimized,
                    resolution.ActualCapAngularRadius);

        Assert.True(
            optimizedCoverage
            >= legacyCoverage);

        // The discrete whole-sphere optimizer should provide a useful margin,
        // not merely reshuffle equivalent Fibonacci points.
        Assert.True(
            optimizedCoverage
            >= legacyCoverage + 0.015d);

        Assert.True(
            optimizedCoverage >= 0.94d);
    }

    [Fact]
    public void OptimizedLayout_PinsOneShotAtVisibleCenter()
    {
        DssEngineeringTargetResolution resolution =
            DssEngineeringTargetResolver.Resolve(
                18,
                "HUD_CV",
                LivePatch26Module);

        var optimized =
            DssSphericalPlacementPlanner
                .GetCoverageOptimizedBasePoints(
                    resolution.TargetCount,
                    resolution.ActualCapAngularRadius);

        Assert.Contains(
            optimized,
            point =>
                point.Theta <= 1e-9d);
    }

    [Fact]
    public void OrderedPlan_FiresRearFirstAndCenterLast()
    {
        DssEngineeringTargetResolution resolution =
            DssEngineeringTargetResolver.Resolve(
                18,
                "HUD_CV",
                LivePatch26Module);

        var plan =
            DssSphericalPlacementPlanner
                .GenerateOrderedSphericalPlan(
                    resolution.TargetCount,
                    30d,
                    LivePatch26Module,
                    1_000_000d,
                    resolution.ActualCapAngularRadius);

        Assert.Equal(
            15,
            plan.Count);

        Assert.True(
            plan[0].SurfacePoint.Theta
            > Math.PI / 2d);

        Assert.True(
            plan[^1].SurfacePoint.Theta
            <= 0.18d);

        Assert.Equal(
            "BATCH_CENTER",
            plan[^1].Role);
    }

    [Fact]
    public void OptimizedLayout_IsDeterministicAndContainsUniquePoints()
    {
        DssEngineeringTargetResolution resolution =
            DssEngineeringTargetResolver.Resolve(
                18,
                "HUD_CV",
                LivePatch26Module);

        var first =
            DssSphericalPlacementPlanner
                .GetCoverageOptimizedBasePoints(
                    resolution.TargetCount,
                    resolution.ActualCapAngularRadius);

        var second =
            DssSphericalPlacementPlanner
                .GetCoverageOptimizedBasePoints(
                    resolution.TargetCount,
                    resolution.ActualCapAngularRadius);

        Assert.Equal(
            first.Count,
            second.Count);

        for (int i = 0;
             i < first.Count;
             i++)
        {
            Assert.Equal(
                first[i].Theta,
                second[i].Theta,
                12);

            Assert.Equal(
                first[i].Phi,
                second[i].Phi,
                12);
        }

        int unique =
            first
                .Select(point =>
                    $"{point.X:R}|{point.Y:R}|{point.Z:R}")
                .Distinct(
                    StringComparer.Ordinal)
                .Count();

        Assert.Equal(
            first.Count,
            unique);
    }
}
