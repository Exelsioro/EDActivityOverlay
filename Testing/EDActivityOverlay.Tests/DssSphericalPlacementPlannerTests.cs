using System;
using System.Collections.Generic;
using System.Linq;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssSphericalPlacementPlannerTests
{
    // =========================================================================
    // Req 2 & 4: Spherical coordinates and Rotational Symmetry
    // =========================================================================

    [Fact]
    public void SphericalPoint_CartesianConversion_PreservesUnitNormAndAngles()
    {
        SphericalPoint p = new(Math.PI / 3d, Math.PI / 4d); // theta = 60 deg, phi = 45 deg

        double norm = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        Assert.InRange(norm, 0.999999, 1.000001);

        SphericalPoint reconstructed = SphericalPoint.FromCartesian(p.X, p.Y, p.Z);
        Assert.InRange(reconstructed.Theta, p.Theta - 1e-6, p.Theta + 1e-6);
        Assert.InRange(reconstructed.Phi, p.Phi - 1e-6, p.Phi + 1e-6);
    }

    [Fact]
    public void RotationalSymmetry_ScreenAzimuthMatchesSphericalAzimuth()
    {
        const double angularDiameter = 24.5d;

        foreach (double phiDeg in new[] { 0d, 45d, 90d, 135d, 180d, -45d, -90d, -135d })
        {
            double phiRad = phiDeg * Math.PI / 180d;
            SphericalPoint p = new(Math.PI / 3d, phiRad);

            (double nx, double ny, double k) = DssSphericalProjection.ProjectSphericalToScreenAim(
                p,
                angularDiameter);

            double screenPhi = Math.Atan2(ny, nx);
            double expectedPhi = p.Phi;

            double diff = Math.Abs(Math.Atan2(Math.Sin(screenPhi - expectedPhi), Math.Cos(screenPhi - expectedPhi)));
            Assert.True(diff < 1e-6, $"Screen azimuth {screenPhi * 180 / Math.PI}° does not match spherical azimuth {expectedPhi * 180 / Math.PI}°");
        }
    }

    // =========================================================================
    // Req 3: Polyhedral validation ground truth (N=4, 6, 8, 12)
    // =========================================================================

    [Fact]
    public void PolyhedralCatalog_Tetrahedron_HasExactMutualAngles()
    {
        IReadOnlyList<SphericalPoint> vertices = PolyhedralValidationCatalog.GetTetrahedron();
        Assert.Equal(4, vertices.Count);

        double expectedAngleDeg = Math.Acos(-1d / 3d) * 180d / Math.PI; // ~109.4712 deg

        for (int i = 0; i < vertices.Count; i++)
        {
            for (int j = i + 1; j < vertices.Count; j++)
            {
                double distDeg = vertices[i].AngularDistanceToDegrees(vertices[j]);
                Assert.InRange(distDeg, expectedAngleDeg - 0.05, expectedAngleDeg + 0.05);
            }
        }
    }

    [Fact]
    public void PolyhedralCatalog_Octahedron_HasExactNearestNeighborAngles()
    {
        IReadOnlyList<SphericalPoint> vertices = PolyhedralValidationCatalog.GetOctahedron();
        Assert.Equal(6, vertices.Count);

        const double expectedNearestDeg = 90.0d;

        for (int i = 0; i < vertices.Count; i++)
        {
            var otherDistances = vertices
                .Where((_, idx) => idx != i)
                .Select(v => vertices[i].AngularDistanceToDegrees(v))
                .OrderBy(d => d)
                .ToList();

            // 4 nearest neighbors at 90 deg, 1 opposite at 180 deg
            for (int k = 0; k < 4; k++)
            {
                Assert.InRange(otherDistances[k], expectedNearestDeg - 0.05, expectedNearestDeg + 0.05);
            }
            Assert.InRange(otherDistances[4], 179.95, 180.05);
        }
    }

    [Fact]
    public void PolyhedralCatalog_Cube_HasExactNearestNeighborAngles()
    {
        IReadOnlyList<SphericalPoint> vertices = PolyhedralValidationCatalog.GetCube();
        Assert.Equal(8, vertices.Count);

        double expectedNearestDeg = Math.Acos(1d / 3d) * 180d / Math.PI; // ~70.5288 deg

        for (int i = 0; i < vertices.Count; i++)
        {
            var otherDistances = vertices
                .Where((_, idx) => idx != i)
                .Select(v => vertices[i].AngularDistanceToDegrees(v))
                .OrderBy(d => d)
                .ToList();

            // 3 nearest neighbors at ~70.53 deg
            for (int k = 0; k < 3; k++)
            {
                Assert.InRange(otherDistances[k], expectedNearestDeg - 0.05, expectedNearestDeg + 0.05);
            }
        }
    }

    [Fact]
    public void PolyhedralCatalog_Icosahedron_HasExactNearestNeighborAngles()
    {
        IReadOnlyList<SphericalPoint> vertices = PolyhedralValidationCatalog.GetIcosahedron();
        Assert.Equal(12, vertices.Count);

        double expectedNearestDeg = Math.Acos(1d / Math.Sqrt(5d)) * 180d / Math.PI; // ~63.4349 deg

        for (int i = 0; i < vertices.Count; i++)
        {
            var otherDistances = vertices
                .Where((_, idx) => idx != i)
                .Select(v => vertices[i].AngularDistanceToDegrees(v))
                .OrderBy(d => d)
                .ToList();

            // 5 nearest neighbors at ~63.43 deg
            for (int k = 0; k < 5; k++)
            {
                Assert.InRange(otherDistances[k], expectedNearestDeg - 0.05, expectedNearestDeg + 0.05);
            }
        }
    }

    // =========================================================================
    // Req 5, 6, 7: Calibrated Projection & Native MISS Boundary
    // =========================================================================

    [Theory]
    [InlineData(21.0)]
    [InlineData(24.5)]
    [InlineData(28.0)]
    public void CalibratedProjection_EndpointsAndMonotonicity(double angularDiameter)
    {
        double k0 = DssSphericalProjection.ProjectSurfacePolarAngleToDssAim(0d, angularDiameter);
        Assert.InRange(k0, -1e-6, 1e-6);

        double kLimb = DssSphericalProjection.ProjectSurfacePolarAngleToDssAim(Math.PI / 2d, angularDiameter);
        Assert.InRange(kLimb, 0.9999, 1.0001);

        double kBack = DssSphericalProjection.ProjectSurfacePolarAngleToDssAim(Math.PI, angularDiameter);
        double kSafe = DssSphericalProjection.EstimateSafeNormalizedRadius(angularDiameter);
        Assert.InRange(kBack, kSafe - 1e-4, kSafe + 1e-4);

        // Monotonic check
        double prevK = -1;
        for (double theta = 0; theta <= Math.PI; theta += 0.1)
        {
            double k = DssSphericalProjection.ProjectSurfacePolarAngleToDssAim(theta, angularDiameter);
            Assert.True(k >= prevK, $"K({theta}) = {k} is less than previous K = {prevK}");
            prevK = k;
        }
    }

    [Theory]
    [InlineData(21.0)]
    [InlineData(24.5)]
    [InlineData(28.0)]
    public void NativeMissConstraint_SafeRadiusEnforcesMargin(double angularDiameter)
    {
        double kBoundary = DssSphericalProjection.EstimateBoundaryNormalizedRadius(angularDiameter);
        double kSafe = DssSphericalProjection.EstimateSafeNormalizedRadius(angularDiameter);

        Assert.InRange(kBoundary - kSafe, DssSphericalProjection.SafetyMarginNormalized - 1e-6, DssSphericalProjection.SafetyMarginNormalized + 1e-6);
        Assert.True(kSafe < kBoundary);
    }

    [Theory]
    [InlineData(21.0)]
    [InlineData(24.5)]
    [InlineData(28.0)]
    public void CalibratedProjection_RoundtripConsistency(double angularDiameter)
    {
        for (double theta = 0; theta <= Math.PI; theta += 0.15)
        {
            double k = DssSphericalProjection.ProjectSurfacePolarAngleToDssAim(theta, angularDiameter);
            double reconstructedTheta = DssSphericalProjection.ProjectDssAimToSurfacePolarAngle(k, angularDiameter);

            Assert.InRange(reconstructedTheta, theta - 0.02, theta + 0.02);
        }
    }

    // =========================================================================
    // Req 8 & 10: Spherical-Cap Coverage Model
    // =========================================================================

    [Fact]
    public void SphericalCapCoverage_EngineeringLevel_IncreasesFootprint()
    {
        var baseModule = DssModuleSnapshot.Empty;
        var g3Module = new DssModuleSnapshot("item", "name", 0, 0, "blueprint", 3);
        var g5Module = new DssModuleSnapshot("item", "name", 0, 0, "blueprint", 5);

        double alphaBase = DssSphericalCapCoverage.CalculateCapAngularRadius(baseModule, 0, 6);
        double alphaG3 = DssSphericalCapCoverage.CalculateCapAngularRadius(g3Module, 0, 6);
        double alphaG5 = DssSphericalCapCoverage.CalculateCapAngularRadius(g5Module, 0, 6);

        Assert.True(alphaG3 > alphaBase);
        Assert.True(alphaG5 > alphaG3);
    }

    [Fact]
    public void SphericalCapCoverage_PolyhedralOctahedron_AchievesTargetCoverage()
    {
        IReadOnlyList<SphericalPoint> octahedron = PolyhedralValidationCatalog.GetOctahedron();
        double capRadius = 45d * Math.PI / 180d; // 45 deg radius caps

        double coverage = DssSphericalCapCoverage.EvaluateUnionCoverage(octahedron, capRadius);
        Assert.InRange(coverage, 0.85, 1.00);
    }

    // =========================================================================
    // Req 9, 11, 13: Layout generation, Shot ordering, Instant advance
    // =========================================================================

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(12)]
    public void SphericalPlacementPlanner_GeneratesValidOrderedPlans(int n)
    {
        const double angularDiameter = 24.0d;
        var module = DssModuleSnapshot.Empty;

        IReadOnlyList<DssSphericalAimTarget> plan = DssSphericalPlacementPlanner.GenerateOrderedSphericalPlan(
            n,
            angularDiameter,
            module,
            bodyRadiusMeters: 6000000);

        Assert.Equal(n, plan.Count);

        double kSafe = DssSphericalProjection.EstimateSafeNormalizedRadius(angularDiameter);

        foreach (DssSphericalAimTarget target in plan)
        {
            Assert.True(target.Available);
            Assert.InRange(target.AimRadiusNormalized, 0d, kSafe + 1e-4);
            Assert.True(double.IsFinite(target.NormalizedX));
            Assert.True(double.IsFinite(target.NormalizedY));
        }

        // Shot ordering check (Req 11): Deep far probes are placed before center/near
        if (n >= 4)
        {
            // First shot should be a far probe (r/Rh > 1.0)
            Assert.True(plan[0].AimRadiusNormalized > 1.0d, $"First shot K={plan[0].AimRadiusNormalized} should be far-side");

            // Center or near probe should be the last shot
            DssSphericalAimTarget lastTarget = plan[^1];
            Assert.True(lastTarget.AimRadiusNormalized <= 1.0d || lastTarget.Role == "BATCH_CENTER" || lastTarget.Role == "BATCH_NEAR",
                $"Last target role was '{lastTarget.Role}', expected center or near");
        }
    }

    [Fact]
    public void SphericalPlacementPlanner_InstantProgression_DoesNotBlockOnImpacts()
    {
        const double angularDiameter = 24.0d;
        var module = DssModuleSnapshot.Empty;

        // Sequential step 1..6 should all resolve immediately even with 0 confirmed impacts
        for (int step = 1; step <= 6; step++)
        {
            DssSphericalAimTarget target = DssSphericalPlacementPlanner.Resolve(
                sequentialStep: step,
                requestedTarget: 6,
                targetSource: "BODY",
                angularDiameterDegrees: angularDiameter,
                dssModule: module,
                bodyRadiusMeters: 5000000,
                confirmedImpactCount: 0,
                coverageObservation: null,
                usedCoverageCandidates: 0);

            Assert.True(target.Available);
            Assert.Equal(step, target.Sequence);
            Assert.Equal(6, target.TotalPlanCount);
        }
    }

    // =========================================================================
    // Req 12: Coverage CV as correction feedback only
    // =========================================================================

    [Fact]
    public void SphericalPlacementPlanner_CorrectionTail_UsesCoverageFeedback()
    {
        const double angularDiameter = 24.0d;
        var module = DssModuleSnapshot.Empty;

        var coverageObservation = new DssCoverageObservation(
            Available: true,
            Settling: false,
            CoveredFraction: 0.82d,
            Confidence: 0.85d,
            SuggestedCandidateId: 3,
            SuggestedNormalizedX: 0.35d,
            SuggestedNormalizedY: -0.40d,
            SuggestedUncoveredScore: 0.45d);

        // Step 7 on an N=6 body is the first correction shot
        DssSphericalAimTarget correction = DssSphericalPlacementPlanner.Resolve(
            sequentialStep: 7,
            requestedTarget: 6,
            targetSource: "BODY",
            angularDiameterDegrees: angularDiameter,
            dssModule: module,
            bodyRadiusMeters: 5000000,
            confirmedImpactCount: 6,
            coverageObservation: coverageObservation,
            usedCoverageCandidates: 0);

        Assert.True(correction.Available);
        Assert.Equal("CORRECTION_COVERAGE", correction.Role);
        Assert.Equal(3, correction.CandidateId);
    }

    // =========================================================================
    // Req 14: Fallback strategy retention
    // =========================================================================

    [Fact]
    public void SphericalPlacementPlanner_FallbackMode_ProducesValidV30Targets()
    {
        try
        {
            DssSphericalPlacementPlanner.ActiveStrategy = DssPlannerStrategy.EmpiricalV30Fallback;

            DssSphericalAimTarget fallbackTarget = DssSphericalPlacementPlanner.Resolve(
                sequentialStep: 1,
                requestedTarget: 6,
                targetSource: "BODY",
                angularDiameterDegrees: 24.0d,
                dssModule: DssModuleSnapshot.Empty,
                bodyRadiusMeters: 5000000,
                confirmedImpactCount: 0,
                coverageObservation: null,
                usedCoverageCandidates: 0);

            Assert.True(fallbackTarget.Available);
            Assert.Equal("BATCH_FAR_DEEP", fallbackTarget.Role);
        }
        finally
        {
            DssSphericalPlacementPlanner.ActiveStrategy = DssPlannerStrategy.SphericalCalibrated;
        }
    }
}
