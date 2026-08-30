using System.IO;
using System.Text.Json;

namespace EDActivityOverlay.Services.Trading;

public sealed class TradeHistoryService
{
    private const int DefaultMaxRecords = 5000;

    private static readonly Lazy<TradeHistoryService> LazyInstance =
        new(() => new TradeHistoryService());

    private readonly object sync = new();
    private readonly string filePath;
    private readonly int maxRecords;
    private readonly JsonSerializerOptions jsonOptions =
        new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase
        };

    private readonly List<TradeHistoryRecord> records = new();
    private readonly HashSet<Guid> knownIds = new();
    private readonly HashSet<Guid> sessionIds = new();

    public static TradeHistoryService Instance =>
        LazyInstance.Value;

    public event EventHandler? HistoryChanged;

    public TradeHistoryService(
        string? filePath = null,
        int maxRecords = DefaultMaxRecords,
        bool loadExisting = true)
    {
        this.maxRecords =
            Math.Max(
                10,
                maxRecords);

        this.filePath =
            filePath
            ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "EDActivityOverlay",
                "trade-history.jsonl");

        if (loadExisting)
        {
            Load();
        }
    }

    public string FilePath =>
        filePath;

    public void Record(
        TradeHistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(
            record);

        bool changed =
            false;

        lock (sync)
        {
            if (!knownIds.Add(record.Id))
            {
                return;
            }

            records.Add(
                record);

            sessionIds.Add(
                record.Id);

            records.Sort(
                static (left, right) =>
                    left.CompletedAtUtc.CompareTo(
                        right.CompletedAtUtc));

            changed =
                true;

            Directory.CreateDirectory(
                Path.GetDirectoryName(filePath)
                ?? ".");

            try
            {
                string line =
                    JsonSerializer.Serialize(
                        record,
                        jsonOptions);

                File.AppendAllText(
                    filePath,
                    line
                    + Environment.NewLine);

                if (records.Count
                    > maxRecords)
                {
                    int removeCount =
                        records.Count
                        - maxRecords;

                    records.RemoveRange(
                        0,
                        removeCount);

                    knownIds.Clear();

                    foreach (TradeHistoryRecord item
                             in records)
                    {
                        knownIds.Add(
                            item.Id);
                    }

                    CompactFile();
                }

            }
            catch (Exception ex)
            {
                Logger.Logger.Warning(
                    $"Unable to persist trade history: {ex.Message}");
            }
        }

        if (changed)
        {
            HistoryChanged?.Invoke(
                this,
                EventArgs.Empty);
        }
    }

    public TradeHistorySnapshot Snapshot(
        int recentLimit = 200)
    {
        lock (sync)
        {
            TradeHistoryRecord[] all =
                records
                    .OrderByDescending(item =>
                        item.CompletedAtUtc)
                    .ToArray();

            TradeHistoryRecord[] session =
                all
                    .Where(item =>
                        sessionIds.Contains(
                            item.Id))
                    .ToArray();

            return new TradeHistorySnapshot
            {
                Session =
                    Summarize(
                        session),
                AllTime =
                    Summarize(
                        all),
                Recent =
                    all
                        .Take(
                            Math.Max(
                                1,
                                recentLimit))
                        .ToArray()
            };
        }
    }

    public static TradeHistorySummary Summarize(
        IEnumerable<TradeHistoryRecord> source)
    {
        TradeHistoryRecord[] items =
            source.ToArray();

        long profit =
            items.Sum(item =>
                item.ActualProfit);

        double seconds =
            items.Sum(item =>
                Math.Max(
                    0,
                    item.Duration.TotalSeconds));

        long rate =
            seconds <= 0
                ? 0
                : checked(
                    (long)Math.Round(
                        profit
                        * 3600d
                        / seconds));

        long cargo =
            items
                .SelectMany(item =>
                    item.Legs)
                .Sum(leg =>
                    (long)Math.Max(
                        0,
                        leg.SoldQuantity));

        return new TradeHistorySummary
        {
            Trades =
                items.Length,
            Profit =
                profit,
            Duration =
                TimeSpan.FromSeconds(
                    seconds),
            ProfitPerHour =
                rate,
            BestTradeProfit =
                items.Length == 0
                    ? 0
                    : items.Max(item =>
                        item.ActualProfit),
            TotalCargoSold =
                cargo
        };
    }

    private void Load()
    {
        lock (sync)
        {
            records.Clear();
            knownIds.Clear();
            sessionIds.Clear();

            if (!File.Exists(
                    filePath))
            {
                return;
            }

            try
            {
                foreach (string line
                         in File.ReadLines(
                             filePath))
                {
                    if (string.IsNullOrWhiteSpace(
                            line))
                    {
                        continue;
                    }

                    try
                    {
                        TradeHistoryRecord? item =
                            JsonSerializer.Deserialize<TradeHistoryRecord>(
                                line,
                                jsonOptions);

                        if (item is null
                            || !knownIds.Add(
                                item.Id))
                        {
                            continue;
                        }

                        records.Add(
                            item);
                    }
                    catch
                    {
                        // JSONL deliberately tolerates one damaged entry.
                    }
                }

                records.Sort(
                    static (left, right) =>
                        left.CompletedAtUtc.CompareTo(
                            right.CompletedAtUtc));

                if (records.Count
                    > maxRecords)
                {
                    records.RemoveRange(
                        0,
                        records.Count
                        - maxRecords);

                    knownIds.Clear();

                    foreach (TradeHistoryRecord item
                             in records)
                    {
                        knownIds.Add(
                            item.Id);
                    }

                    CompactFile();
                }
            }
            catch (Exception ex)
            {
                Logger.Logger.Warning(
                    $"Unable to load trade history: {ex.Message}");
            }
        }
    }

    private void CompactFile()
    {
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(filePath)
                ?? ".");

            string temp =
                filePath
                + ".tmp";

            File.WriteAllLines(
                temp,
                records.Select(item =>
                    JsonSerializer.Serialize(
                        item,
                        jsonOptions)));

            File.Move(
                temp,
                filePath,
                overwrite:
                    true);
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"Unable to compact trade history: {ex.Message}");
        }
    }
}
