using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssVisualMotionContinuityTests
{
    [Fact]
    public void MovingInnovationGate_AllowsAccelerationResidual()
    {
        double gate =
            DssVisualMotionPolicy.ResolveMaximumInnovationPixels(
                stationary: false,
                speedPixelsPerSecond: 600,
                dtSeconds: 0.05);

        Assert.InRange(
            gate,
            23.99,
            24.01);
    }

    [Fact]
    public void StationaryGate_RemainsStrict()
    {
        double gate =
            DssVisualMotionPolicy.ResolveMaximumInnovationPixels(
                stationary: true,
                speedPixelsPerSecond: 600,
                dtSeconds: 0.05);

        Assert.Equal(
            DssVisualMotionPolicy.StationaryHoldRadiusPixels,
            gate);
    }
}
