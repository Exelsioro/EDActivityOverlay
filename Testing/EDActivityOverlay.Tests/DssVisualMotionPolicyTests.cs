using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssVisualMotionPolicyTests
{
    [Fact]
    public void StationaryTrack_UsesTightSearchAndInnovationGate()
    {
        bool stationary =
            DssVisualMotionPolicy.IsStationary(
                anchorVelocityX: 0,
                anchorVelocityY: 0,
                trackVelocityX: 0,
                trackVelocityY: 0);

        Assert.True(stationary);

        Assert.Equal(
            8,
            DssVisualMotionPolicy.ResolveSearchRadius(
                stationary,
                speedPixelsPerSecond: 0,
                dtSeconds: 0.05));

        Assert.Equal(
            DssVisualMotionPolicy.StationaryHoldRadiusPixels,
            DssVisualMotionPolicy.ResolveMaximumInnovationPixels(
                stationary,
                speedPixelsPerSecond: 0,
                dtSeconds: 0.05));
    }

    [Fact]
    public void ResearchFalseJump_IsOutsideStationaryGate()
    {
        // In research session 20260827-015317565 the stable LOCAL centre was
        // about 1303.7,684.0 and IMAGE intermittently returned 1309,686.
        double innovation =
            DssVisualMotionPolicy.Distance(
                1309,
                686,
                1303.7,
                684.0);

        Assert.True(
            innovation
            > DssVisualMotionPolicy.StationaryHoldRadiusPixels);
    }

    [Fact]
    public void MovingTrack_AllowsBoundedPredictionError()
    {
        bool stationary =
            DssVisualMotionPolicy.IsStationary(
                anchorVelocityX: 420,
                anchorVelocityY: 30,
                trackVelocityX: 390,
                trackVelocityY: 20);

        Assert.False(stationary);

        double gate =
            DssVisualMotionPolicy.ResolveMaximumInnovationPixels(
                stationary,
                speedPixelsPerSecond: 420,
                dtSeconds: 0.05);

        Assert.InRange(
            gate,
            22.24,
            22.26);
    }
}
