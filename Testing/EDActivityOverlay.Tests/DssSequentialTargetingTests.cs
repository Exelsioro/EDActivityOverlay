using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class DssSequentialTargetingTests
{
    [Fact]
    public void FirstSixSteps_MatchEmpiricalV24Progression()
    {
        const double theta = 24.0;

        AssertPoint(1, theta, 0d,
            -DssProbeAimSolver.EstimateSafeNormalizedRadius(theta),
            DssAimZone.FarSide);
        AssertPoint(2, theta, 0d, -0.90d, DssAimZone.Limb);
        AssertPoint(3, theta, 0d, 0d, DssAimZone.Disc);
        AssertPoint(4, theta, 0d, 0.98d, DssAimZone.Limb);
        AssertPoint(5, theta, 1.07d, 0d, DssAimZone.Limb);
        AssertPoint(6, theta, -1.12d, 0d, DssAimZone.Limb);
    }

    [Theory]
    [InlineData(21.0)]
    [InlineData(24.0)]
    [InlineData(28.0)]
    public void RecoverySteps_StayInsideCalibratedSafeRadius(double theta)
    {
        double safeRadius = DssProbeAimSolver.EstimateSafeNormalizedRadius(theta);

        for (int step = 2;
             step <= DssProbeAimSolver.TargetingV1MaximumSequentialStep;
             step++)
        {
            bool available =
                DssProbeAimSolver.TryResolveSequentialNormalizedPoint(
                    step, theta, out double x, out double y, out _);

            Assert.True(available);

            double radius = Math.Sqrt(x * x + y * y);

            Assert.True(
                radius < safeRadius,
                $"step {step}: {radius:0.###} must stay inside {safeRadius:0.###}");
        }
    }

    [Fact]
    public void Sequence_StopsAfterConfiguredMaximum()
    {
        bool available =
            DssProbeAimSolver.TryResolveSequentialNormalizedPoint(
                DssProbeAimSolver.TargetingV1MaximumSequentialStep + 1,
                24.0,
                out _,
                out _,
                out _);

        Assert.False(available);
    }

    private static void AssertPoint(
        int step,
        double theta,
        double expectedX,
        double expectedY,
        DssAimZone expectedZone)
    {
        bool available =
            DssProbeAimSolver.TryResolveSequentialNormalizedPoint(
                step,
                theta,
                out double x,
                out double y,
                out DssAimZone zone);

        Assert.True(available);
        Assert.Equal(expectedZone, zone);
        Assert.InRange(Math.Abs(x - expectedX), 0d, 0.000001d);
        Assert.InRange(Math.Abs(y - expectedY), 0d, 0.000001d);
    }
}
