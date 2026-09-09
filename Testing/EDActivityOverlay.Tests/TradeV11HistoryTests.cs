using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeV11HistoryTests
{
    [Fact]
    public void HistoryPersistsReloadsAndSkipsDamagedJsonlRows()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                "edaa-trade-history-tests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            directory);

        string file =
            Path.Combine(
                directory,
                "history.jsonl");

        try
        {
            var service =
                new TradeHistoryService(
                    file,
                    maxRecords:
                        100,
                    loadExisting:
                        false);

            TradeHistoryRecord first =
                Record(
                    profit:
                        1_500_000,
                    seconds:
                        600,
                    sold:
                        100);

            service.Record(
                first);

            File.AppendAllText(
                file,
                "{broken json"
                + Environment.NewLine);

            var reloaded =
                new TradeHistoryService(
                    file,
                    maxRecords:
                        100,
                    loadExisting:
                        true);

            TradeHistorySnapshot snapshot =
                reloaded.Snapshot();

            TradeHistoryRecord loaded =
                Assert.Single(
                    snapshot.Recent);

            Assert.Equal(
                first.Id,
                loaded.Id);

            Assert.Equal(
                1_500_000L,
                snapshot.AllTime.Profit);
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive:
                    true);
        }
    }

    [Fact]
    public void SummaryUsesAggregateProfitOverAggregateDuration()
    {
        TradeHistorySummary summary =
            TradeHistoryService.Summarize(
                new[]
                {
                    Record(
                        profit:
                            1_000_000,
                        seconds:
                            600,
                        sold:
                            100),
                    Record(
                        profit:
                            2_000_000,
                        seconds:
                            1200,
                        sold:
                            200)
                });

        Assert.Equal(
            2,
            summary.Trades);

        Assert.Equal(
            3_000_000L,
            summary.Profit);

        Assert.Equal(
            6_000_000L,
            summary.ProfitPerHour);

        Assert.Equal(
            300L,
            summary.TotalCargoSold);

        Assert.Equal(
            2_000_000L,
            summary.BestTradeProfit);
    }

    [Fact]
    public void TrackerRecordsCompletionExactlyOnce()
    {
        string tracker =
            ReadProjectFile(
                "EDActivityOverlay",
                "Services",
                "Journal",
                "TradeRouteProgressTracker.cs");

        string compact =
            RemoveWhitespace(
                tracker);

        // Cargo-sale-only routes deliberately bypass the normal buy/sell trade
        // history writer. Keep this assertion about the actual invariant instead
        // of requiring the old guard to remain byte-for-byte identical.
        Assert.Contains(
            "historyRecorded",
            tracker,
            StringComparison.Ordinal);
        Assert.Contains(
            "||!completed",
            compact,
            StringComparison.Ordinal);

        Assert.Contains(
            "TradeHistoryService.Instance.Record(",
            tracker,
            StringComparison.Ordinal);

        Assert.Contains(
            "FinalizeCurrentHistoryLeg(",
            tracker,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FullTradeContainsPersistentHistoryPanel()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.History.cs");

        Assert.Contains(
            "x:Name=\"TradeHistoryPanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"TradeHistoryList\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "HistoryButton_Click",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "tradeHistoryService.Snapshot(",
            code,
            StringComparison.Ordinal);
    }

    private static TradeHistoryRecord Record(
        long profit,
        int seconds,
        int sold)
    {
        DateTimeOffset start =
            DateTimeOffset.UtcNow
            - TimeSpan.FromSeconds(
                seconds);

        return new TradeHistoryRecord
        {
            StartedAtUtc =
                start,
            CompletedAtUtc =
                start
                + TimeSpan.FromSeconds(
                    seconds),
            InitialPlannedProfit =
                profit,
            FinalPlannedProfit =
                profit,
            ActualProfit =
                profit,
            Legs =
                new[]
                {
                    new TradeHistoryLegRecord
                    {
                        LegNumber =
                            1,
                        Commodity =
                            "Gold",
                        SoldQuantity =
                            sold,
                        StartedAtUtc =
                            start,
                        CompletedAtUtc =
                            start
                            + TimeSpan.FromSeconds(
                                seconds)
                    }
                }
        };
    }

    private static string RemoveWhitespace(
        string value) =>
        string.Concat(
            value.Where(character =>
                !char.IsWhiteSpace(character)));

    private static string ReadProjectFile(
        params string[] relative)
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
        {
            string candidate =
                directory.FullName;

            foreach (string part
                     in relative)
            {
                candidate =
                    Path.Combine(
                        candidate,
                        part);
            }

            if (File.Exists(
                    candidate))
            {
                return File.ReadAllText(
                    candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
