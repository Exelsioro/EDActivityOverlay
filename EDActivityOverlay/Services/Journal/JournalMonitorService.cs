using System.Text;
using System.IO;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;

namespace EDActivityOverlay.Services.Journal;

public sealed class JournalMonitorService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(350);
    private readonly JournalStateReducer reducer = new(new ExplorationProgressStore());
    private readonly Dictionary<string, DateTime> companionWriteTimes = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? cancellation;
    private Task? monitorTask;
    private string? currentJournal;
    private long journalOffset;
    private byte[] journalRemainder = Array.Empty<byte>();
    private bool disposed;

    public static JournalMonitorService Instance { get; } = new();
    public JournalEventHub Events { get; } = new();

    public event EventHandler<GameStateChangedEventArgs>? StateChanged
    {
        add => reducer.StateChanged += value;
        remove => reducer.StateChanged -= value;
    }

    public event EventHandler<JournalEventReceivedEventArgs>? JournalEventReceived
    {
        add => reducer.JournalEventReceived += value;
        remove => reducer.JournalEventReceived -= value;
    }

    public GameStateSnapshot Current => reducer.Current;
    public string JournalDirectory { get; private set; } = string.Empty;

    private JournalMonitorService()
    {
        reducer.JournalEventReceived += (_, journalEvent) => Events.Publish(journalEvent);
    }

    public void Start(string? configuredDirectory = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (monitorTask is { IsCompleted: false })
        {
            return;
        }

        JournalDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? JournalPathResolver.GetDefaultJournalDirectory()
            : configuredDirectory;
        cancellation = new CancellationTokenSource();
        monitorTask = Task.Run(() => MonitorAsync(cancellation.Token));
        Logger.Logger.Info($"Journal monitor started: {JournalDirectory}");
    }

    public void Stop()
    {
        CancellationTokenSource? source = cancellation;
        Task? task = monitorTask;
        source?.Cancel();
        if (task != null && Task.CurrentId != task.Id)
        {
            try
            {
                task.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is TaskCanceledException or OperationCanceledException))
            {
            }
        }
        source?.Dispose();
        cancellation = null;
        monitorTask = null;
    }

    public void Restart(string? configuredDirectory = null)
    {
        Stop();
        currentJournal = null;
        journalOffset = 0;
        journalRemainder = Array.Empty<byte>();
        companionWriteTimes.Clear();
        Start(configuredDirectory);
    }

    private async Task MonitorAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                bool available = Directory.Exists(JournalDirectory);
                reducer.SetJournalAvailability(JournalDirectory, available);
                if (available)
                {
                    await ReadJournalAsync(token).ConfigureAwait(false);
                    await ReadCompanionFilesAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Logger.Warning($"Journal monitor iteration failed: {ex.Message}");
            }

            await Task.Delay(PollInterval, token).ConfigureAwait(false);
        }
    }

    private async Task ReadJournalAsync(CancellationToken token)
    {
        string? latest = Directory.EnumerateFiles(JournalDirectory, "Journal.*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (latest is null)
        {
            return;
        }

        if (!string.Equals(latest, currentJournal, StringComparison.OrdinalIgnoreCase))
        {
            currentJournal = latest;
            journalOffset = 0;
            journalRemainder = Array.Empty<byte>();
            Logger.Logger.Info($"Journal file selected: {Path.GetFileName(latest)}");
        }

        await using FileStream stream = new(
            latest,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length < journalOffset)
        {
            journalOffset = 0;
            journalRemainder = Array.Empty<byte>();
        }
        if (stream.Length == journalOffset)
        {
            return;
        }

        stream.Position = journalOffset;
        int newLength = checked((int)(stream.Length - journalOffset));
        byte[] bytes = new byte[newLength];
        int read = 0;
        while (read < bytes.Length)
        {
            int chunk = await stream.ReadAsync(bytes.AsMemory(read), token).ConfigureAwait(false);
            if (chunk == 0)
            {
                break;
            }
            read += chunk;
        }
        journalOffset += read;

        byte[] combined = new byte[journalRemainder.Length + read];
        Buffer.BlockCopy(journalRemainder, 0, combined, 0, journalRemainder.Length);
        Buffer.BlockCopy(bytes, 0, combined, journalRemainder.Length, read);

        int lineStart = 0;
        for (int index = 0; index < combined.Length; index++)
        {
            if (combined[index] != (byte)'\n')
            {
                continue;
            }

            int length = index - lineStart;
            if (length > 0 && combined[index - 1] == (byte)'\r')
            {
                length--;
            }
            string line = Encoding.UTF8.GetString(combined, lineStart, length);
            TryApplyJournalLine(line);
            lineStart = index + 1;
        }

        journalRemainder = combined[lineStart..];
    }

    private async Task ReadCompanionFilesAsync(CancellationToken token)
    {
        await TryReadCompanionAsync("Status.json", reducer.ApplyStatusJson, token).ConfigureAwait(false);
        await TryReadCompanionAsync("Cargo.json", reducer.ApplyCargoJson, token).ConfigureAwait(false);
        await TryReadCompanionAsync("NavRoute.json", reducer.ApplyNavRouteJson, token).ConfigureAwait(false);
        await TryReadCompanionAsync("Market.json", reducer.ApplyMarketJson, token).ConfigureAwait(false);
        await TryReadCompanionAsync("Backpack.json", null, token).ConfigureAwait(false);
        await TryReadCompanionAsync("ShipLocker.json", null, token).ConfigureAwait(false);
        await TryReadCompanionAsync("ModulesInfo.json", null, token).ConfigureAwait(false);
    }

    private async Task TryReadCompanionAsync(string fileName, Action<string>? apply, CancellationToken token)
    {
        string path = Path.Combine(JournalDirectory, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(path);
        if (companionWriteTimes.TryGetValue(path, out DateTime processed) && processed == writeTime)
        {
            return;
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new(stream, Encoding.UTF8, true);
                string json = await reader.ReadToEndAsync(token).ConfigureAwait(false);
                apply?.Invoke(json);
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
                DateTimeOffset timestamp = DateTimeOffset.UtcNow;
                if (document.RootElement.TryGetProperty("timestamp", out System.Text.Json.JsonElement timestampElement)
                    && DateTimeOffset.TryParse(timestampElement.GetString(), out DateTimeOffset parsedTimestamp))
                {
                    timestamp = parsedTimestamp;
                }
                Events.Publish(new CompanionFileReceivedEventArgs(fileName, timestamp, document.RootElement.Clone()));
                companionWriteTimes[path] = writeTime;
                return;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                await Task.Delay(40 * (attempt + 1), token).ConfigureAwait(false);
            }
        }
    }

    private void TryApplyJournalLine(string line)
    {
        try
        {
            reducer.ApplyJournalLine(line);
        }
        catch (System.Text.Json.JsonException ex)
        {
            Logger.Logger.Warning($"Skipping malformed journal entry: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Stop();
    }
}
