using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class FsdScoCooldownResearchLoggerTests
{
    [Fact]
    public void LoggerCreatesJsonlAndRecordsScoState()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                $"edao-sco-cooldown-{Guid.NewGuid():N}");

        try
        {
            var logger =
                new FsdScoCooldownResearchLogger(
                    directory);

            logger.Record(
                $$"""
                {
                  "timestamp":"2026-09-05T00:00:00Z",
                  "Flags":{{1UL << 4}},
                  "Flags2":{{1UL << 20}}
                }
                """);

            string path =
                Assert.IsType<string>(
                    logger.CurrentLogPath);

            string line =
                Assert.Single(
                    File.ReadAllLines(path));

            Assert.Contains(
                "\"scoActive\":true",
                line,
                StringComparison.Ordinal);

            Assert.Contains(
                "\"rawStatus\"",
                line,
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
