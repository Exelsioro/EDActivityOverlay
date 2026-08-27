using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssHemisphereBalancedCorrectionTests
{
    private static readonly DssModuleSnapshot Module =
        new(
            "dss",
            "DSS",
            20d,
            20d,
            string.Empty,
            0);

    private static DssCoverageObservation StrongNearCoverage =>
        new(
            true,
            false,
            0.60d,
            0.90d,
            1,
            0d,
            -0.68d,
            0.80d);

    [Fact]
    public void FirstCorrection_IsRearEvenWhenNearCoverageLooksVeryUncovered()
    {
        DssSphericalAimTarget target =
            DssSphericalPlacementPlanner.Resolve(
                14,
                13,
                "BODY",
                30d,
                Module,
                1_000_000d,
                13,
                StrongNearCoverage,
                0);

        Assert.True(target.Available);
        Assert.True(target.AimRadiusNormalized > 1d);
        Assert.True(target.SurfacePoint.Theta > System.Math.PI / 2d);
        Assert.Equal(
            "CORRECTION_FAR_BALANCE",
            target.Role);
    }

    [Fact]
    public void RearCorrection_DoesNotFlipWhenCoverageObserverSettles()
    {
        DssSphericalAimTarget withCoverage =
            DssSphericalPlacementPlanner.Resolve(
                14,
                13,
                "BODY",
                30d,
                Module,
                1_000_000d,
                13,
                StrongNearCoverage,
                0);

        DssSphericalAimTarget whileSettling =
            DssSphericalPlacementPlanner.Resolve(
                14,
                13,
                "BODY",
                30d,
                Module,
                1_000_000d,
                13,
                DssCoverageObservation.Empty with
                {
                    Settling = true
                },
                0);

        Assert.Equal(
            withCoverage.NormalizedX,
            whileSettling.NormalizedX,
            8);

        Assert.Equal(
            withCoverage.NormalizedY,
            whileSettling.NormalizedY,
            8);

        Assert.Equal(
            "CORRECTION_FAR_BALANCE",
            whileSettling.Role);
    }

    [Fact]
    public void SecondCorrection_UsesStrongNearCoverage()
    {
        DssSphericalAimTarget target =
            DssSphericalPlacementPlanner.Resolve(
                15,
                13,
                "BODY",
                30d,
                Module,
                1_000_000d,
                14,
                StrongNearCoverage,
                0);

        Assert.True(target.Available);
        Assert.True(target.AimRadiusNormalized < 1d);
        Assert.Equal(
            "CORRECTION_COVERAGE_NEAR",
            target.Role);
    }

    [Fact]
    public void FourRearCorrectionSlots_AreDistinct()
    {
        var seen =
            new System.Collections.Generic.HashSet<string>();

        foreach (int step
                 in new[]
                 {
                     14,
                     16,
                     18,
                     20
                 })
        {
            DssSphericalAimTarget target =
                DssSphericalPlacementPlanner.Resolve(
                    step,
                    13,
                    "BODY",
                    30d,
                    Module,
                    1_000_000d,
                    13,
                    StrongNearCoverage,
                    0);

            Assert.True(target.Available);
            Assert.True(target.AimRadiusNormalized > 1d);

            string key =
                $"{target.SurfacePoint.Theta:F6}|{target.SurfacePoint.Phi:F6}";

            Assert.True(
                seen.Add(key));
        }
    }
}
