using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssMissAwareFlightCorrelatorTests
{
    [Fact]
    public void NativeHudMissIsNotQueuedForImpact()
    {
        var correlator =
            new DssProbeFlightCorrelator();

        DssProbeLaunchRecord launch =
            Launch(
                hudMissVisible: true);

        bool queued =
            correlator.RegisterLaunch(
                launch);

        Assert.False(
            queued);

        Assert.False(
            correlator.HasPendingLaunches);
    }

    [Fact]
    public void NonMissLaunchIsQueuedForImpact()
    {
        var correlator =
            new DssProbeFlightCorrelator();

        bool queued =
            correlator.RegisterLaunch(
                Launch(
                    hudMissVisible: false));

        Assert.True(
            queued);

        Assert.True(
            correlator.HasPendingLaunches);
    }

    private static DssProbeLaunchRecord Launch(
        bool hudMissVisible) =>
        new(
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            0,
            1,
            "SecondaryFire",
            "Primary",
            "Keyboard",
            "Key_5",
            true,
            "Ready",
            22,
            600,
            540,
            195,
            960,
            540,
            1.8,
            0,
            1.8,
            0,
            0,
            0,
            0,
            0,
            0,
            12,
            "SETTINGS",
            hudMissVisible,
            hudMissVisible ? 0.06 : 0.0);
}
