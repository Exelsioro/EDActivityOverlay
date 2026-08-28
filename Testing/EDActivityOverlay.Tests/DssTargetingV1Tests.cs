using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class DssTargetingV1Tests
{
    [Theory]
    // Clean v23 radial sweeps. The fit is intentionally conservative and
    // only needs to stay close to the recorded MISS boundary in the
    // calibrated 21-28 degree range.
    [InlineData(21.39, 1.7402)]
    [InlineData(22.48, 1.7414)]
    [InlineData(23.21, 1.7326)]
    [InlineData(24.22, 1.7225)]
    public void BoundaryFit_TracksRecordedV23Calibration(
        double angularDiameterDegrees,
        double recordedBoundary)
    {
        double actual =
            DssProbeAimSolver.EstimateBoundaryNormalizedRadius(
                angularDiameterDegrees);

        Assert.InRange(
            actual,
            recordedBoundary - 0.02,
            recordedBoundary + 0.02);
    }

    [Fact]
    public void SafeRadius_StaysInsideBoundaryByConfiguredMargin()
    {
        const double theta = 24.0;

        double boundary =
            DssProbeAimSolver.EstimateBoundaryNormalizedRadius(theta);

        double safe =
            DssProbeAimSolver.EstimateSafeNormalizedRadius(theta);

        Assert.InRange(
            Math.Abs(
                boundary
                - safe
                - DssProbeAimSolver.TargetingV1SafetyMarginNormalized),
            0d,
            0.000001d);
    }

    [Theory]
    [InlineData(20.99, false)]
    [InlineData(21.00, true)]
    [InlineData(24.50, true)]
    [InlineData(28.00, true)]
    [InlineData(28.01, false)]
    public void CalibrationRange_IsExplicit(
        double angularDiameterDegrees,
        bool expected)
    {
        Assert.Equal(
            expected,
            DssProbeAimSolver.IsWithinTargetingV1Calibration(
                angularDiameterDegrees));
    }
}
