using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssDisplayGeometrySmootherTests
{
    [Fact]
    public void MovingMeasurement_IsLatencyCompensatedInsteadOfDelayed()
    {
        var smoother =
            new DssDisplayGeometrySmoother();

        DateTimeOffset t0 =
            new(2026, 8, 26, 20, 0, 0, TimeSpan.Zero);

        _ = smoother.Update(
            t0,
            t0.AddMilliseconds(80),
            Geometry(900, 8));

        DssHudGeometry result =
            smoother.Update(
                t0.AddMilliseconds(66),
                t0.AddMilliseconds(146),
                Geometry(920, 7));

        // The old v31 buffer would still render around x=900 here.
        Assert.True(
            result.BodyCenterX >= 920);
        Assert.True(
            result.BodyCenterX <= 932.1);
    }

    [Fact]
    public void StopMeasurement_DoesNotCreepTowardOldPrediction()
    {
        var smoother =
            new DssDisplayGeometrySmoother();

        DateTimeOffset t0 =
            new(2026, 8, 26, 20, 10, 0, TimeSpan.Zero);

        _ = smoother.Update(
            t0,
            t0.AddMilliseconds(80),
            Geometry(900, 8));

        _ = smoother.Update(
            t0.AddMilliseconds(66),
            t0.AddMilliseconds(146),
            Geometry(920, 7));

        DssHudGeometry stopped =
            smoother.Update(
                t0.AddMilliseconds(132),
                t0.AddMilliseconds(212),
                Geometry(920.2, 7));

        Assert.InRange(
            stopped.BodyCenterX,
            919.9,
            920.5);
    }

    [Fact]
    public void CentreReticleOcclusion_HoldsGeometryForSeveralSeconds()
    {
        var smoother =
            new DssDisplayGeometrySmoother();

        DateTimeOffset t0 =
            new(2026, 8, 26, 20, 20, 0, TimeSpan.Zero);

        _ = smoother.Update(
            t0,
            t0.AddMilliseconds(80),
            Geometry(959, 1.1));

        DssHudGeometry held =
            smoother.Update(
                t0.AddSeconds(5),
                t0.AddSeconds(5.08),
                MissingGeometry());

        Assert.True(
            held.BodyCenterFound);
        Assert.True(
            held.HorizonMarkerFound);
    }

    private static DssHudGeometry Geometry(
        double x,
        double aimOffset) =>
        new(
            960,
            540,
            true,
            x,
            700,
            0.98,
            true,
            true,
            960,
            500,
            0.9,
            0,
            230,
            0,
            aimOffset);

    private static DssHudGeometry MissingGeometry() =>
        new(
            960,
            540,
            false,
            0,
            0,
            0,
            false,
            false,
            0,
            0,
            0,
            -1,
            0,
            0,
            0);
}
