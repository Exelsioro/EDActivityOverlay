using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeHistoryStorageSettingsTests
{
    [Fact]
    public void EmptyHistoryDirectoryPreservesOriginalAppDataLocation()
    {
        string expected =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "EDActivityOverlay",
                "trade-history.jsonl");

        Assert.Equal(
            Path.GetFullPath(
                expected),
            TradeHistoryPathResolver.ResolveFilePath(
                string.Empty));
    }

    [Fact]
    public void ConfiguringAnotherDirectorySwitchesAndReloadsHistoryWithoutMovingOldFile()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "edaa-trade-history-storage-tests",
                Guid.NewGuid().ToString("N"));

        string firstDirectory =
            Path.Combine(
                root,
                "first");

        string secondDirectory =
            Path.Combine(
                root,
                "second");

        Directory.CreateDirectory(
            firstDirectory);

        Directory.CreateDirectory(
            secondDirectory);

        try
        {
            string firstFile =
                Path.Combine(
                    firstDirectory,
                    TradeHistoryPathResolver.FileName);

            var service =
                new TradeHistoryService(
                    firstFile,
                    maxRecords:
                        100,
                    loadExisting:
                        false);

            TradeHistoryRecord first =
                Record(
                    1_000);

            service.Record(
                first);

            Assert.True(
                File.Exists(
                    firstFile));

            service.ConfigureDirectory(
                secondDirectory);

            Assert.Empty(
                service.Snapshot().Recent);

            TradeHistoryRecord second =
                Record(
                    2_000);

            service.Record(
                second);

            Assert.True(
                File.Exists(
                    Path.Combine(
                        secondDirectory,
                        TradeHistoryPathResolver.FileName)));

            service.ConfigureDirectory(
                firstDirectory);

            TradeHistoryRecord loaded =
                Assert.Single(
                    service.Snapshot().Recent);

            Assert.Equal(
                first.Id,
                loaded.Id);

            Assert.True(
                File.Exists(
                    firstFile));
        }
        finally
        {
            if (Directory.Exists(
                    root))
            {
                Directory.Delete(
                    root,
                    recursive:
                        true);
            }
        }
    }

    [Fact]
    public void SettingsExposeTradeHistoryDirectoryControls()
    {
        string repository =
            FindRepositoryRoot();

        string settingsService =
            File.ReadAllText(
                Path.Combine(
                    repository,
                    "EDActivityOverlay",
                    "Services",
                    "SettingsService.cs"));

        string settingsXaml =
            File.ReadAllText(
                Path.Combine(
                    repository,
                    "EDActivityOverlay",
                    "Windows",
                    "SettingsWindow.xaml"));

        string settingsCode =
            File.ReadAllText(
                Path.Combine(
                    repository,
                    "EDActivityOverlay",
                    "Windows",
                    "SettingsWindow.xaml.cs"));

        Assert.Contains(
            "TradeHistoryDirectory",
            settingsService,
            StringComparison.Ordinal);

        Assert.Contains(
            "TradeHistoryDirectoryTextBox",
            settingsXaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "BrowseTradeHistoryDirectoryButton_Click",
            settingsXaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "TradeHistoryService.Instance.ConfigureDirectory(",
            settingsCode,
            StringComparison.Ordinal);
    }

    private static TradeHistoryRecord Record(
        long profit)
    {
        DateTimeOffset started =
            DateTimeOffset.UtcNow
            - TimeSpan.FromMinutes(
                10);

        return new TradeHistoryRecord
        {
            StartedAtUtc =
                started,
            CompletedAtUtc =
                DateTimeOffset.UtcNow,
            InitialPlannedProfit =
                profit,
            FinalPlannedProfit =
                profit,
            ActualProfit =
                profit
        };
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "EDActivityOverlay",
                        "EDActivityOverlay.csproj")))
            {
                return
                    directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}
