using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssRearTrajectoryProjectionTests
{
    [Fact]
    public void V51DeepRearPoint_UsesOuterEquivalentNearObservedManualRadius()
    {
        const double angularDiameterDegrees = 29d;

        double theta =
            InnerAimToTheta(
                1.2104d,
                angularDiameterDegrees);

        double phi =
            Math.Atan2(
                0.317234d,
                -1.168089d);

        var point =
            new SphericalPoint(
                theta,
                phi);

        (double nx, double ny, double k) =
            DssSphericalProjection.ProjectSphericalToScreenAim(
                point,
                angularDiameterDegrees);

        Assert.InRange(
            k,
            1.47d,
            1.52d);

        double aimPhi =
            Math.Atan2(
                ny,
                nx);

        double expectedOppositePhi =
            Normalize(
                phi + Math.PI);

        Assert.True(
            AngularDifference(
                aimPhi,
                expectedOppositePhi)
            < 0.02d);
    }

    [Fact]
    public void OuterBranch_RoundTripsRearSurfacePoint()
    {
        const double angularDiameterDegrees = 29d;

        var source =
            new SphericalPoint(
                2.42d,
                -2.10d);

        (double nx, double ny, double k) =
            DssSphericalProjection.ProjectSphericalToScreenAim(
                source,
                angularDiameterDegrees);

        Assert.True(
            k
            > DssSphericalProjection
                .EstimateRearAntipodeNormalizedRadius(
                    angularDiameterDegrees));

        SphericalPoint restored =
            DssSphericalProjection.ProjectScreenAimToSpherical(
                nx,
                ny,
                angularDiameterDegrees);

        Assert.InRange(
            Math.Abs(
                restored.Theta
                - source.Theta),
            0d,
            1e-9d);

        Assert.True(
            AngularDifference(
                restored.Phi,
                source.Phi)
            < 1e-9d);
    }

    [Fact]
    public void NearRearHorizon_KeepsSafeInnerBranch()
    {
        const double angularDiameterDegrees = 29d;

        double theta =
            Math.PI / 2d
            + 0.08d;

        Assert.False(
            DssSphericalProjection.ShouldUseOuterFarBranch(
                theta,
                angularDiameterDegrees));

        var point =
            new SphericalPoint(
                theta,
                0.4d);

        (_, _, double k) =
            DssSphericalProjection.ProjectSphericalToScreenAim(
                point,
                angularDiameterDegrees);

        Assert.True(
            k
            < DssSphericalProjection
                .EstimateRearAntipodeNormalizedRadius(
                    angularDiameterDegrees));
    }

    private static double InnerAimToTheta(
        double k,
        double angularDiameterDegrees)
    {
        double rear =
            DssSphericalProjection
                .EstimateRearAntipodeNormalizedRadius(
                    angularDiameterDegrees);

        double ratio =
            Math.Clamp(
                (k - 1d)
                / (rear - 1d),
                0d,
                1d);

        return
            Math.PI / 2d
            + Math.PI / 2d
              * ratio;
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

    private static double AngularDifference(
        double a,
        double b) =>
        Math.Abs(
            Normalize(
                a - b));
}
