using System;
using System.Collections.Generic;
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
    public void FirstCorrection_WaitsForWholeBaseBatchToImpact()
    {
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
    public void SecondCorrection_WaitsForFirstCorrectionImpact()
    {
        DssSphericalAimTarget beforeImpact =
            DssSphericalPlacementPlanner.Resolve(
                15,
                13,
                "BODY",
                30d,
                Module,
                1_000_000d,
                13,
                StrongNearCoverage,
                0);

        Assert.False(
            beforeImpact.Available);

        DssSphericalAimTarget afterImpact =
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

        Assert.True(
            afterImpact.Available);
    }

    [Fact]
    public void FirstN13Correction_PrefersLargeRearGapOverStrongNearCvHole()
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
