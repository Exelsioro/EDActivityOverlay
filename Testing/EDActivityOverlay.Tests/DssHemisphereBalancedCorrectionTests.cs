using System;
using System.Collections.Generic;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

[Collection("DssNativeEfficiencyTargetRuntime")]
public sealed class DssHemisphereBalancedCorrectionTests : IDisposable
{
    public DssHemisphereBalancedCorrectionTests()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();
        DssNativeScanProgressRuntime.ResetForTests();
    }

    public void Dispose()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();
        DssNativeScanProgressRuntime.ResetForTests();
    }

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
    public void FirstCorrection_WaitsForWholeBaseBatchToImpact()
    {
        DssNativeScanProgressRuntime.SetForTests(
            coverage: 82,
            hits: 12,
            stableAge: TimeSpan.FromSeconds(3));

        DssSphericalAimTarget whileInFlight =
            DssSphericalPlacementPlanner.Resolve(
                14,
                13,
                "BODY",
                30d,
                Module,
                1_000_000d,
                12,
                StrongNearCoverage,
                0);

        Assert.False(
            whileInFlight.Available);

        DssNativeScanProgressRuntime.SetForTests(
            coverage: 82,
            hits: 13,
            stableAge: TimeSpan.FromSeconds(3));

        DssSphericalAimTarget afterBaseImpacts =
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

        Assert.True(
            afterBaseImpacts.Available);
    }

    [Fact]
    public void SecondCorrection_DoesNotTrustAbsoluteHitCountAtStepEntry()
    {
        // Entering correction #2 records the current native hit count as the
        // launch baseline for correction #1. A pre-existing absolute count
        // cannot authorize the next shot by itself.
        DssNativeScanProgressRuntime.SetForTests(
            coverage: 82,
            hits: 14,
            stableAge: TimeSpan.FromSeconds(3));

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

        Assert.False(
            target.Available);
    }
    [Fact]
    public void FirstN13Correction_PrefersLargeRearGapOverStrongNearCvHole()
    {
        DssNativeScanProgressRuntime.SetForTests(
            coverage: 82,
            hits: 13,
            stableAge: TimeSpan.FromSeconds(3));

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

        Assert.True(
            target.Available);

        Assert.True(
            target.SurfacePoint.Theta
            > Math.PI / 2d);

        Assert.True(
            target.AimRadiusNormalized > 1d);

        Assert.Equal(
            "CORRECTION_MODEL_REAR",
            target.Role);
    }

    [Fact]
    public void RearCorrection_DoesNotFlipWhileCoverageObserverSettles()
    {
        DssNativeScanProgressRuntime.SetForTests(
            coverage: 82,
            hits: 13,
            stableAge: TimeSpan.FromSeconds(3));

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
            "CORRECTION_MODEL_REAR",
            whileSettling.Role);
    }

    [Fact]
    public void N6FirstCorrection_CanUseNearbyNearSideCoverageCv()
    {
        DssNativeScanProgressRuntime.SetForTests(
            coverage: 82,
            hits: 6,
            stableAge: TimeSpan.FromSeconds(3));

        var coverage =
            new DssCoverageObservation(
                true,
                false,
                0.82d,
                0.85d,
                3,
                0.35d,
                -0.40d,
                0.45d);

        DssSphericalAimTarget target =
            DssSphericalPlacementPlanner.Resolve(
                7,
                6,
                "BODY",
                24d,
                Module,
                5_000_000d,
                6,
                coverage,
                0);

        Assert.True(
            target.Available);

        Assert.Equal(
            "CORRECTION_COVERAGE",
            target.Role);

        Assert.Equal(
            3,
            target.CandidateId);
    }

    [Fact]
    public void GlobalCorrectionPlan_HasDistinctPoints()
    {
        IReadOnlyList<SphericalPoint> basePoints =
            DssSphericalPlacementPlanner
                .GenerateOptimalSphericalPoints(
                    13);

        double capAlpha =
            DssSphericalCapCoverage
                .SolveCapAngularRadiusForCoverage(
                    basePoints,
                    0.90d);

        IReadOnlyList<DssCoverageCorrectionPoint> corrections =
            DssSphericalCapCoverage
                .GenerateGreedyCorrectionPlan(
                    basePoints,
                    capAlpha,
                    8);

        var seen =
            new System.Collections.Generic.HashSet<string>();

        foreach (DssCoverageCorrectionPoint correction
                 in corrections)
        {
            string key =
                $"{correction.Point.Theta:F8}|" +
                $"{correction.Point.Phi:F8}";

            Assert.True(
                seen.Add(
                    key));

            Assert.True(
                correction.IncrementalCoverage > 0d);
        }
    }
}
