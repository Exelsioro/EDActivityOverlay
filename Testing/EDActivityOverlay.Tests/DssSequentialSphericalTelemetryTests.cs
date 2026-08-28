using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssSequentialSphericalTelemetryTests
{
    [Fact]
    public void FireTelemetry_UsesSphericalPlanAboveLegacyTwentyEightDegreeGate()
    {
        var body =
            new ExplorationBodySnapshot(
                23,
                "Test body",
                string.Empty,
                0d,
                false,
                false,
                false,
                false,
                0,
                Array.Empty<string>(),
                ExplorationInterest.None)
            {
                EfficiencyTarget = 6
            };

        GameStateSnapshot state =
            GameStateSnapshot.Empty with
            {
                DestinationBodyId = 23,
                ExplorationBodies =
                    new[] { body }
            };

        var readiness =
            new DssAssistantReadinessSnapshot(
                DssAssistantReadinessState.Ready,
                true,
                817_732d,
                14.6368395d,
                29.273679d,
                0d,
                0d,
                0d,
                0d,
                0d);

        var geometry =
            new DssHudGeometry(
                960,
                540,
                true,
                1256.086d,
                709.726d,
                0.90d,
                true,
                false,
                1000d,
                540d,
                0.80d,
                0d,
                267.121d,
                0d,
                0d);

        var module =
            new DssModuleSnapshot(
                "Int_DetailedSurfaceScanner",
                "Detailed Surface Scanner",
                26d,
                20d,
                "Sensor_Expanded",
                3);

        var frame =
            new DssProbeLaunchFrameSnapshot(
                222,
                DateTimeOffset.UtcNow,
                state,
                readiness,
                geometry,
                null,
                DssCoverageObservation.Empty,
                0,
                0,
                module);

        var launch =
            new DssProbeLaunchRecord(
                1,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                0d,
                222,
                "PrimaryFire",
                "Secondary",
                "Keyboard",
                "Key_A",
                true,
                "Ready",
                29.273679d,
                geometry.BodyCenterX,
                geometry.BodyCenterY,
                geometry.HorizonRadiusPixels,
                geometry.ReticleX,
                geometry.ReticleY,
                -1.108432d,
                -0.635388d,
                1.27763d,
                -150.177d,
                1,
                -1.091998d,
                -0.632835d,
                0.016631d,
                4.443d,
                6,
                "TARGETING_V3_TEST",
                false,
                0d);

        DssSequentialTargetTelemetry telemetry =
            DssSequentialTargetTelemetryBuilder.Build(
                1,
                false,
                frame,
                launch);

        Assert.True(telemetry.Available);
        Assert.True(double.IsFinite(telemetry.NormalizedX));
        Assert.True(double.IsFinite(telemetry.NormalizedY));
        Assert.True(telemetry.NormalizedRadius > 0d);
        Assert.Contains(
            "TARGETING_V3_",
            telemetry.TargetSource);
    }
}
