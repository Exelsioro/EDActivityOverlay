using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssProbeFlightCorrelatorTests
{
    [Fact]
    public void SinglePendingLaunchCorrelatesWithHighConfidenceMethod()
    {
        var correlator =
            new DssProbeFlightCorrelator();

        DateTimeOffset launchUtc =
            DateTimeOffset.UtcNow;

        correlator.RegisterLaunch(
            Launch(
                1,
                launchUtc,
                0.62));

        DssProbeImpactCorrelation impact =
            correlator.RegisterImpact(
                launchUtc
                    .AddSeconds(4),
                100,
                0.03);

        Assert.Equal(
            1,
            impact.MatchedLaunchSequence);

        Assert.Equal(
            "SINGLE_PENDING",
            impact.CorrelationMethod);

        Assert.Equal(
            1,
            impact.CandidateCount);

        Assert.InRange(
            impact.FlightMilliseconds,
            3999,
            4001);
    }

    [Fact]
    public void MultiplePendingLaunchesRemainExplicitlyAmbiguous()
    {
        var correlator =
            new DssProbeFlightCorrelator();

        DateTimeOffset t0 =
            DateTimeOffset.UtcNow;

        correlator.RegisterLaunch(
            Launch(
                1,
                t0,
                1.4));

        correlator.RegisterLaunch(
            Launch(
                2,
                t0.AddSeconds(1),
                0.5));

        DssProbeImpactCorrelation impact =
            correlator.RegisterImpact(
                t0.AddSeconds(5),
                200,
                0.03);

        Assert.Equal(
            1,
            impact.MatchedLaunchSequence);

        Assert.Equal(
            "FIFO_AMBIGUOUS",
            impact.CorrelationMethod);

        Assert.Equal(
            2,
            impact.CandidateCount);
    }

    [Fact]
    public void OldUnmatchedLaunchExpiresAsUnresolvedNotCertainMiss()
    {
        var correlator =
            new DssProbeFlightCorrelator();

        DateTimeOffset t0 =
            DateTimeOffset.UtcNow;

        correlator.RegisterLaunch(
            Launch(
                1,
                t0,
                1.8));

        var expired =
            correlator.Expire(
                t0.AddSeconds(46));

        DssProbeUnresolvedLaunch item =
            Assert.Single(
                expired);

        Assert.Equal(
            "NO_IMPACT_WITHIN_45S",
            item.Reason);
    }

    private static DssProbeLaunchRecord Launch(
        int sequence,
        DateTimeOffset utc,
        double radius) =>
        new(
            sequence,
            utc,
            utc,
            0,
            sequence,
            "SecondaryFire",
            "Primary",
            "Keyboard",
            "Key_5",
            true,
            "Ready",
            25,
            960,
            540,
            220,
            960,
            540,
            radius,
            0,
            radius,
            0,
            0,
            0,
            0,
            0,
            0,
            12,
            "SETTINGS");
}
