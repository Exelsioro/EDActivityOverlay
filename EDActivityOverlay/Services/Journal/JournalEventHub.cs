using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Journal;

public interface IJournalDataConsumer
{
    void OnJournalEvent(JournalEventReceivedEventArgs journalEvent);
    void OnCompanionFile(CompanionFileReceivedEventArgs companionFile);
}

public sealed class CompanionFileReceivedEventArgs(
    string fileName,
    DateTimeOffset timestamp,
    System.Text.Json.JsonElement data) : EventArgs
{
    public string FileName { get; } = fileName;
    public DateTimeOffset Timestamp { get; } = timestamp;
    public System.Text.Json.JsonElement Data { get; } = data;
}

public sealed class JournalEventHub
{
    private readonly object sync = new();
    private readonly List<IJournalDataConsumer> consumers = new();

    public void Register(IJournalDataConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        lock (sync)
        {
            if (!consumers.Contains(consumer))
            {
                consumers.Add(consumer);
            }
        }
    }

    public void Unregister(IJournalDataConsumer consumer)
    {
        lock (sync)
        {
            consumers.Remove(consumer);
        }
    }

    internal void Publish(JournalEventReceivedEventArgs journalEvent)
    {
        foreach (IJournalDataConsumer consumer in Snapshot())
        {
            try
            {
                consumer.OnJournalEvent(journalEvent);
            }
            catch (Exception ex)
            {
                Logger.Logger.Warning($"Journal consumer {consumer.GetType().Name} failed for {journalEvent.EventName}: {ex.Message}");
            }
        }
    }

    internal void Publish(CompanionFileReceivedEventArgs companionFile)
    {
        foreach (IJournalDataConsumer consumer in Snapshot())
        {
            try
            {
                consumer.OnCompanionFile(companionFile);
            }
            catch (Exception ex)
            {
                Logger.Logger.Warning($"Journal consumer {consumer.GetType().Name} failed for {companionFile.FileName}: {ex.Message}");
            }
        }
    }

    private IJournalDataConsumer[] Snapshot()
    {
        lock (sync)
        {
            return consumers.ToArray();
        }
    }
}
