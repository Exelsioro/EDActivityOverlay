using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class FsdScoResearchLoggerTests
{
    [Fact]
    public void DecoderReadsExistingFsdFlagsAndScoResearchBit()
    {
        ulong flags =
            (1UL << 4)
            | (1UL << 16)
            | (1UL << 17)
            | (1UL << 18);

        ulong flags2 =
            1UL << 20;

        FsdScoResearchSample sample =
            FsdScoResearchSample.Decode(
                $$"""
                {
                  "timestamp":"2026-09-04T20:00:00Z",
                  "Flags":{{flags}},
                  "Flags2":{{flags2}}
                }
                """,
                DateTimeOffset.Parse(
                    "2026-09-04T20:00:00.250Z"));

        Assert.True(sample.InSupercruise);
        Assert.True(sample.FsdMassLocked);
        Assert.True(sample.FsdCharging);
        Assert.True(sample.FsdCooldown);
        Assert.True(sample.ScoActive);
        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-09-04T20:00:00Z"),
            sample.StatusUtc);
    }

    [Fact]
    public void DecoderMarksScoOffAndCooldownOnTransition()
    {
        FsdScoResearchSample active =
            FsdScoResearchSample.Decode(
                $$"""
                {
                  "Flags":{{1UL << 4}},
                  "Flags2":{{1UL << 20}}
                }
                """,
                DateTimeOffset.Parse(
                    "2026-09-04T20:00:00Z"));

        FsdScoResearchSample stopped =
            FsdScoResearchSample.Decode(
                $$"""
                {
                  "Flags":{{(1UL << 4) | (1UL << 18)}},
                  "Flags2":0
                }
                """,
                DateTimeOffset.Parse(
                    "2026-09-04T20:00:00.400Z"),
                active);

        Assert.Contains(
            "SCO_OFF",
            stopped.Transitions);
        Assert.Contains(
            "FSD_COOLDOWN_ON",
            stopped.Transitions);
        Assert.Equal(
            400,
            stopped.MillisecondsSincePrevious);
    }

    [Fact]
    public void LoggerWritesOneJsonLinePerStatusSample()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                $"edao-fsd-sco-{Guid.NewGuid():N}");

        try
        {
            var logger =
                new FsdScoResearchLogger(
                    directory);

            logger.RecordStatusJson(
                """{"Flags":16,"Flags2":0}""");

            string path =
                Assert.IsType<string>(
                    logger.CurrentLogPath);

            string[] lines =
                File.ReadAllLines(path);

            Assert.Single(lines);
            Assert.Contains(
                "\"fsdCooldown\":false",
                lines[0],
                StringComparison.Ordinal);
            Assert.Contains(
                "\"scoActive\":false",
                lines[0],
                StringComparison.Ordinal);
            Assert.Contains(
                "\"rawStatus\"",
                lines[0],
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
        }
    }
}
