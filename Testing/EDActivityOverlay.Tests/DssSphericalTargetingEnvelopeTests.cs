using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssSphericalTargetingEnvelopeTests
{
    [Theory]
    [InlineData(28.751838d)]
    [InlineData(29.690294d)]
    [InlineData(29.992003d)]
    [InlineData(30.915180d)]
    [InlineData(31.687064d)]
    public void SphericalTargeting_RemainsOperationalAtObservedReadyDiameters(
        double angularDiameterDegrees)
    {
        Assert.True(
            DssProbeAimSolver.IsSphericalTargetingOperational(
                angularDiameterDegrees));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void SphericalTargeting_RejectsInvalidDiameter(
        double angularDiameterDegrees)
    {
        Assert.False(
            DssProbeAimSolver.IsSphericalTargetingOperational(
                angularDiameterDegrees));
    }

    [Fact]
    public void SphericalTargeting_RejectsNaN()
    {
        Assert.False(
            DssProbeAimSolver.IsSphericalTargetingOperational(
                double.NaN));
    }

    [Fact]
    public void LegacyV1Envelope_RemainsNarrowForPredictiveFallback()
    {
        Assert.True(
            DssProbeAimSolver.IsWithinTargetingV1Calibration(
                24d));

        Assert.False(
            DssProbeAimSolver.IsWithinTargetingV1Calibration(
                30d));
    }

    [Fact]
    public void RearAntipodeAtThirtyDegrees_RemainsWellInsideMissBoundary()
    {
        const double angularDiameterDegrees = 30d;

        double miss =
            DssSphericalProjection.EstimateBoundaryNormalizedRadius(
                angularDiameterDegrees);

        double rear =
            DssSphericalProjection.EstimateRearAntipodeNormalizedRadius(
                angularDiameterDegrees);

        double safe =
            DssSphericalProjection.EstimateSafeNormalizedRadius(
                angularDiameterDegrees);

        Assert.True(rear > 1d);
        Assert.True(rear < safe);
        Assert.True(safe < miss);
    }
}
