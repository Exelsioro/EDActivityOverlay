using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssAimVisualTargetLatchTests
{
    [Fact]
    public void CenterTarget_RemainsUntilStepChanges()
    {
        var latch =
            new DssAimVisualTargetLatch();

        DateTimeOffset t0 =
            new(2026, 8, 26, 20, 30, 0, TimeSpan.Zero);

        _ = latch.Resolve(
            t0,
            3,
            false,
            Plan(
                3,
                0,
                0));

        DssProjectedAimPlan held =
            latch.Resolve(
                t0.AddSeconds(8),
                3,
                false,
                DssProjectedAimPlan.Empty);

        Assert.True(
            held.IsAvailable);
    }

    [Fact]
    public void CenterTarget_ClearsOnFireDrivenStepChange()
    {
        var latch =
            new DssAimVisualTargetLatch();

        DateTimeOffset t0 =
            new(2026, 8, 26, 20, 40, 0, TimeSpan.Zero);

        _ = latch.Resolve(
            t0,
            3,
            false,
            Plan(
                3,
                0,
                0));

        DssProjectedAimPlan result =
            latch.Resolve(
                t0.AddMilliseconds(50),
                4,
                false,
                DssProjectedAimPlan.Empty);

        Assert.False(
            result.IsAvailable);
    }

    [Fact]
    public void NonCenterTarget_StillExpiresAfterShortDropout()
    {
        var latch =
            new DssAimVisualTargetLatch();

        DateTimeOffset t0 =
            new(2026, 8, 26, 20, 50, 0, TimeSpan.Zero);

        _ = latch.Resolve(
            t0,
            5,
            false,
            Plan(
                5,
                0.78,
                0));

        DssProjectedAimPlan result =
            latch.Resolve(
                t0.AddSeconds(3),
                5,
                false,
                DssProjectedAimPlan.Empty);

        Assert.False(
            result.IsAvailable);
    }

    private static DssProjectedAimPlan Plan(
        int step,
        double x,
        double y) =>
        new(
            10,
            "TARGETING_V2_TEST/BODY/N10",
            new[]
            {
                new DssProjectedAimPoint(
                    step,
                    x,
                    y,
                    960,
                    540,
                    DssAimZone.Disc)
            });
}
