using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssReadinessDistanceTests
{
    [Fact]
    public void FarReadyBand_UsesNewDistanceOptimizedAngles()
    {
        Assert.Equal(
            21.5d,
            DssAssistantReadinessEvaluator
                .MinimumReadyAngularDiameterDegrees,
            12);

        Assert.Equal(
            23d,
            DssAssistantReadinessEvaluator
                .TargetAngularDiameterDegrees,
            12);

        Assert.Equal(
            28d,
            DssAssistantReadinessEvaluator
                .MaximumReadyAngularDiameterDegrees,
            12);
    }

    [Fact]
    public void TargetDistance_IsAboutFiveBodyRadiiFromCenter()
    {
        const double radius = 1_000_000d;

        (double near,
         double target,
         double far) =
            DssAssistantReadinessEvaluator
                .CalculateRecommendedCenterDistancesMeters(
                    radius);

        Assert.InRange(
            target / radius,
            5.00d,
            5.03d);

        Assert.InRange(
            near / radius,
            4.12d,
            4.15d);

        Assert.InRange(
            far / radius,
            5.34d,
            5.38d);

        Assert.True(
            near < target);

        Assert.True(
            target < far);
    }

    [Fact]
    public void NewTargetRequiresAboutTwentyOnePercentLessApproachThanOld28DegreeTarget()
    {
        const double radius = 1d;

        double oldCenterDistance =
            DssAssistantReadinessEvaluator
                .CalculateCenterDistanceMeters(
                    radius,
                    14d);

        double newCenterDistance =
            DssAssistantReadinessEvaluator
                .CalculateCenterDistanceMeters(
                    radius,
                    11.5d);

        Assert.InRange(
            newCenterDistance / oldCenterDistance,
            1.20d,
            1.23d);
    }
}
