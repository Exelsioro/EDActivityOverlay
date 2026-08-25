using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssProbeAimSolverTests
{
    [Fact]
    public void ReadyGeometryProducesProjectedPattern()
    {
        GameStateSnapshot state =
            GameStateSnapshot.Empty with
            {
                DestinationBodyId = 7,
                DestinationName = "Test 7"
            };

        var readiness =
            new DssAssistantReadinessSnapshot(
                DssAssistantReadinessState.Ready,
                true,
                1_000_000,
                14,
                28,
                0,
                4_000_000,
                3_500_000,
                4_000_000,
                4_500_000);

        var geometry =
            new DssHudGeometry(
                960,
                540,
                true,
                1000,
                600,
                0.98,
                true,
                false,
                0,
                0,
                0.7,
                200,
                300,
                0,
                0);

        DssProjectedAimPlan plan =
            DssProbeAimSolver.Solve(
                state,
                readiness,
                geometry);

        Assert.True(
            plan.IsAvailable);

        Assert.NotEmpty(
            plan.Points);
    }

    [Fact]
    public void NotReadyDoesNotExposeAimPoints()
    {
        var readiness =
            new DssAssistantReadinessSnapshot(
                DssAssistantReadinessState.TooClose,
                true,
                1_000_000,
                20,
                40,
                0,
                0,
                0,
                0,
                0);

        var geometry =
            new DssHudGeometry(
                960,
                540,
                true,
                960,
                540,
                0.98,
                true,
                false,
                0,
                0,
                0.7,
                200,
                300,
                0,
                0);

        Assert.False(
            DssProbeAimSolver
                .Solve(
                    GameStateSnapshot.Empty,
                    readiness,
                    geometry)
                .IsAvailable);
    }

    [Fact]
    public void MissingTrustedHorizonDoesNotExposeAimPoints()
    {
        var readiness =
            new DssAssistantReadinessSnapshot(
                DssAssistantReadinessState.Ready,
                true,
                1_000_000,
                14,
                28,
                0,
                4_000_000,
                3_500_000,
                4_000_000,
                4_500_000);

        DssHudGeometry geometry =
            DssHudGeometry.Empty(
                1920,
                1080) with
            {
                BodyCenterFound = true,
                BodyCenterX = 960,
                BodyCenterY = 540,
                BodyCenterConfidence = 0.98
            };

        Assert.False(
            DssProbeAimSolver
                .Solve(
                    GameStateSnapshot.Empty,
                    readiness,
                    geometry)
                .IsAvailable);
    }
}
