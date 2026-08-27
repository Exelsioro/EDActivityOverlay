using System;
using System.Diagnostics;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssWgcFrameTimingTests
{
    [Fact]
    public void SystemRelativeTime_IsMappedBackByQpcAge()
    {
        DateTimeOffset nowUtc =
            new(
                2026,
                8,
                27,
                12,
                0,
                0,
                TimeSpan.Zero);

        const double frameQpcSeconds = 1000d;
        const double ageSeconds = 0.037d;

        long nowQpc =
            checked(
                (long)Math.Round(
                    (frameQpcSeconds + ageSeconds)
                    * Stopwatch.Frequency));

        DateTimeOffset mapped =
            DssWindowGraphicsCapture.MapSystemRelativeTimeToUtc(
                TimeSpan.FromSeconds(
                    frameQpcSeconds),
                nowUtc,
                nowQpc);

        double mappedAgeMilliseconds =
            (nowUtc - mapped)
            .TotalMilliseconds;

        Assert.InRange(
            mappedAgeMilliseconds,
            36.9d,
            37.1d);
    }

    [Fact]
    public void ImplausibleQpcMismatch_FallsBackToNow()
    {
        DateTimeOffset nowUtc =
            DateTimeOffset.UtcNow;

        DateTimeOffset mapped =
            DssWindowGraphicsCapture.MapSystemRelativeTimeToUtc(
                TimeSpan.Zero,
                nowUtc,
                checked(
                    Stopwatch.Frequency * 120L));

        Assert.Equal(
            nowUtc,
            mapped);
    }
}
