using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssProbeLaunchCorrelationTests
{
    [Fact]
    public void FireInputFreezesReticleAimRelativeToTrackedCenter()
    {
        var binding =
            new DssFireInputBinding(
                "PrimaryFire",
                "Primary",
                new DssPhysicalInputToken(
                    DssPhysicalInputKind.Joystick,
                    "SaitekX52Pro",
                    "Joy_1",
                    0,
                    1),
                Array.Empty<
                    DssPhysicalInputToken>());

        DateTimeOffset frameUtc =
            DateTimeOffset.UtcNow;

        var input =
            new DssFireInputEvent(
                frameUtc
                    .AddMilliseconds(25),
                binding);

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
                900,
                540,
                0.98,
                true,
                false,
                0,
                0,
                0.8,
                100,
                300,
                0,
                0);

        var frame =
            new DssProbeLaunchFrameSnapshot(
                100,
                frameUtc,
                GameStateSnapshot.Empty,
                readiness,
                geometry);

        DssProbeLaunchRecord launch =
            DssProbeLaunchCorrelator
                .Correlate(
                    1,
                    input,
                    frame);

        Assert.True(
            launch.GeometryValid);

        Assert.InRange(
            launch.FrameAgeMilliseconds,
            24.9,
            25.1);

        Assert.InRange(
            launch.AimNormalizedX,
            0.199,
            0.201);

        Assert.InRange(
            launch.AimNormalizedY,
            -0.001,
            0.001);

        Assert.InRange(
            launch.AimNormalizedRadius,
            0.199,
            0.201);
    }
}
